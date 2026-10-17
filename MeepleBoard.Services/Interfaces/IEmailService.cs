namespace MeepleBoard.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendConfirmationEmailAsync(string recipientEmail, string recipientName, string confirmationLink);

        Task SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetLink);
        /// <summary>Single-use link (account deletion requested through support), sent to the account's own address.</summary>
        Task SendAccountDeletionLinkAsync(string recipientEmail, string recipientName, string confirmationLink);
        /// <summary>Confirms the deletion to the address the account had; the address is not kept afterwards.</summary>
        Task SendAccountDeletedAsync(string recipientEmail, string recipientName);
    }
}