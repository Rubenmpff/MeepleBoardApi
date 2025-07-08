namespace MeepleBoard.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendConfirmationEmailAsync(string recipientEmail, string recipientName, string confirmationLink);

        Task SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetLink);
    }
}