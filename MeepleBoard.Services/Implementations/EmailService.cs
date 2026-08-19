using brevo_csharp.Api;
using brevo_csharp.Client;
using brevo_csharp.Model;
using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Evita ambiguidade com brevo_csharp.Model.Task
using Task = System.Threading.Tasks.Task;

namespace MeepleBoard.Services.Implementations
{
    public class EmailService : IEmailService
    {
        private readonly string _apiKey;
        private readonly string _senderEmail;
        private readonly string _senderName;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IConfiguration configuration,
            ILogger<EmailService> logger)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(logger);

            _apiKey = configuration["Brevo:ApiKey"]
                ?? throw new InvalidOperationException(
                    "A chave API da Brevo não está configurada.");

            _senderEmail = configuration["Email:SenderEmail"]
                ?? throw new InvalidOperationException(
                    "O email remetente não está configurado.");

            _senderName = configuration["Email:SenderName"]
                ?? "MeepleBoard";

            _logger = logger;
        }

        /// <summary>
        /// Envia um e-mail de confirmação de conta.
        /// </summary>
        public async Task SendConfirmationEmailAsync(
            string clientEmail,
            string clientName,
            string confirmationLink)
        {
            ValidateRecipient(clientEmail, clientName);

            if (string.IsNullOrWhiteSpace(confirmationLink))
            {
                throw new ArgumentException(
                    "O link de confirmação não pode ser vazio.",
                    nameof(confirmationLink));
            }

            var email = CreateEmail(
                clientEmail,
                clientName,
                "Confirmação de Email - MeepleBoard",
                $"""
                <p>Olá {clientName},</p>
                <p>Para ativares a tua conta, utiliza o link abaixo:</p>
                <p>
                    <a href="{confirmationLink}" target="_blank" rel="noopener noreferrer">
                        Confirmar Email
                    </a>
                </p>
                <p>Se não foste tu que criaste esta conta, podes ignorar este email.</p>
                <p>Atenciosamente,<br>MeepleBoard</p>
                """
            );

            await SendEmailAsync(
                email,
                "Email de confirmação",
                clientEmail);
        }

        /// <summary>
        /// Envia um e-mail de redefinição de password.
        /// </summary>
        public async Task SendPasswordResetEmailAsync(
            string clientEmail,
            string clientName,
            string resetLink)
        {
            ValidateRecipient(clientEmail, clientName);

            if (string.IsNullOrWhiteSpace(resetLink))
            {
                throw new ArgumentException(
                    "O link de redefinição não pode ser vazio.",
                    nameof(resetLink));
            }

            var email = CreateEmail(
                clientEmail,
                clientName,
                "Redefinição de Password - MeepleBoard",
                $"""
                <p>Olá {clientName},</p>
                <p>Recebemos um pedido para redefinir a tua password.</p>
                <p>
                    <a href="{resetLink}" target="_blank" rel="noopener noreferrer">
                        Redefinir Password
                    </a>
                </p>
                <p>Se não fizeste este pedido, podes ignorar este email.</p>
                <p>Atenciosamente,<br>MeepleBoard</p>
                """
            );

            await SendEmailAsync(
                email,
                "Email de redefinição de password",
                clientEmail);
        }

        /// <summary>
        /// Cria a mensagem a enviar através da Brevo.
        /// </summary>
        private SendSmtpEmail CreateEmail(
            string recipientEmail,
            string recipientName,
            string subject,
            string htmlContent)
        {
            return new SendSmtpEmail
            {
                Sender = new SendSmtpEmailSender(
                    email: _senderEmail,
                    name: _senderName),

                To = new List<SendSmtpEmailTo>
                {
                    new SendSmtpEmailTo(
                        email: recipientEmail,
                        name: recipientName)
                },

                Subject = subject,
                HtmlContent = htmlContent
            };
        }

        /// <summary>
        /// Envia uma mensagem através da API transacional da Brevo.
        /// </summary>
        private async Task SendEmailAsync(
            SendSmtpEmail email,
            string emailType,
            string recipientEmail)
        {
            try
            {
                var configuration = new Configuration
                {
                    ApiKey = new Dictionary<string, string>
                    {
                        ["api-key"] = _apiKey
                    }
                };

                var apiInstance =
                    new TransactionalEmailsApi(configuration);

                var result =
                    await apiInstance.SendTransacEmailAsync(email);

                _logger.LogInformation(
                    "{EmailType} enviado com sucesso para {RecipientEmail}. MessageId: {MessageId}",
                    emailType,
                    recipientEmail,
                    result.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Erro ao enviar {EmailType} para {RecipientEmail} através da Brevo.",
                    emailType,
                    recipientEmail);

                throw new InvalidOperationException(
                    "Não foi possível enviar o email através da Brevo.",
                    ex);
            }
        }

        /// <summary>
        /// Valida os dados mínimos do destinatário.
        /// </summary>
        private static void ValidateRecipient(
            string clientEmail,
            string clientName)
        {
            if (string.IsNullOrWhiteSpace(clientEmail))
            {
                throw new ArgumentException(
                    "O email do cliente não pode ser vazio.",
                    nameof(clientEmail));
            }

            if (string.IsNullOrWhiteSpace(clientName))
            {
                throw new ArgumentException(
                    "O nome do cliente não pode ser vazio.",
                    nameof(clientName));
            }
        }
    }
}