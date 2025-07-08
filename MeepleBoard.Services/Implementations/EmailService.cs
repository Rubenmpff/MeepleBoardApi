using brevo_csharp.Api;
using brevo_csharp.Client;
using brevo_csharp.Model;
using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// 🔹 Corrigindo ambiguidade da classe Task
using Task = System.Threading.Tasks.Task;

namespace MeepleBoard.Services.Implementations
{
    public class EmailService : IEmailService
    {
        private readonly string _apiKey;
        private readonly ILogger<EmailService> _logger;

        private const string SenderEmail = "ruben_mpff@hotmail.com";
        private const string SenderName = "MeepleBoard";

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _apiKey = configuration["Brevo:ApiKey"]
                      ?? throw new InvalidOperationException("A chave API da Brevo não está configurada no appsettings.json.");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Envia um e-mail de confirmação de conta.
        /// </summary>
        public async Task SendConfirmationEmailAsync(string clientEmail, string clientName, string confirmationLink)
        {
            if (string.IsNullOrWhiteSpace(clientEmail))
                throw new ArgumentException("O e-mail do cliente não pode ser vazio.", nameof(clientEmail));
            if (string.IsNullOrWhiteSpace(clientName))
                throw new ArgumentException("O nome do cliente não pode ser vazio.", nameof(clientName));

            var sendSmtpEmail = CreateEmail(
                clientEmail,
                clientName,
                "Confirmação de Email - MeepleBoard",
                $@"
                    <p>Olá {clientName},</p>
                    <p>Para ativar sua conta, clique no link abaixo:</p>
                    <p><a href='{confirmationLink}' target='_blank'>Confirmar Email</a></p>
                    <p>Se não foi você que fez este registro, ignore este e-mail.</p>
                    <p>Atenciosamente,<br/>MeepleBoard</p>"
            );

            await SendEmailAsync(sendSmtpEmail, $"E-mail de confirmação enviado para {clientEmail}.");
        }

        /// <summary>
        /// Envia um e-mail de redefinição de senha.
        /// </summary>
        public async Task SendPasswordResetEmailAsync(string clientEmail, string clientName, string resetLink)
        {
            if (string.IsNullOrWhiteSpace(clientEmail))
                throw new ArgumentException("O e-mail do cliente não pode ser vazio.", nameof(clientEmail));
            if (string.IsNullOrWhiteSpace(clientName))
                throw new ArgumentException("O nome do cliente não pode ser vazio.", nameof(clientName));

            var sendSmtpEmail = CreateEmail(
                clientEmail,
                clientName,
                "Redefinição de Senha - MeepleBoard",
                $@"
                    <p>Olá {clientName},</p>
                    <p>Recebemos uma solicitação para redefinir sua senha.</p>
                    <p>Clique no link abaixo para redefinir sua senha:</p>
                    <p><a href='{resetLink}' target='_blank'>Redefinir Senha</a></p>
                    <p>Se você não solicitou a redefinição, ignore este e-mail.</p>
                    <p>Atenciosamente,<br/>MeepleBoard</p>"
            );

            await SendEmailAsync(sendSmtpEmail, $"E-mail de redefinição de senha enviado para {clientEmail}.");
        }

        /// <summary>
        /// Método privado para criar um e-mail.
        /// </summary>
        private SendSmtpEmail CreateEmail(string recipientEmail, string recipientName, string subject, string htmlContent)
        {
            return new SendSmtpEmail
            {
                Sender = new SendSmtpEmailSender(email: SenderEmail, name: SenderName),
                To = new List<SendSmtpEmailTo> { new SendSmtpEmailTo(email: recipientEmail, name: recipientName) },
                Subject = subject,
                HtmlContent = htmlContent
            };
        }

        /// <summary>
        /// Método privado para enviar um e-mail via API da Brevo.
        /// </summary>
        private async Task SendEmailAsync(SendSmtpEmail email, string successMessage)
        {
            var apiInstance = new TransactionalEmailsApi(new Configuration { ApiKey = new Dictionary<string, string> { { "api-key", _apiKey } } });

            try
            {
                var result = await apiInstance.SendTransacEmailAsync(email);
                _logger.LogInformation($"{successMessage} | Message ID: {result.MessageId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao enviar o e-mail: {ex.Message}");
                throw new InvalidOperationException("Erro ao enviar o e-mail via API da Brevo.", ex);
            }
        }
    }
}