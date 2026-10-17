namespace MeepleBoard.Services.Interfaces
{
    public enum OwnAccountDeletionOutcome { Deleted, WrongPassword, LockedOut, NotFound }

    /// <summary>
    /// Deleting one's own account (docs/proposta-eliminacao-conta.md in the app): in the app with the current password,
    /// or through support with a single-use link sent to the account's own address. Both end in IUserService.DeleteAsync.
    /// </summary>
    public interface IAccountDeletionService
    {
        Task<OwnAccountDeletionOutcome> DeleteOwnAsync(Guid userId, string? password, IReadOnlyDictionary<Guid, Guid> campaignTransfers, CancellationToken ct = default);
        /// <summary>Sends the link when the address belongs to an account; says nothing either way.</summary>
        Task RequestThroughSupportAsync(string email, string confirmationUrl, CancellationToken ct = default);
        Task<bool> IsValidLinkAsync(Guid userId, string token, CancellationToken ct = default);
        /// <summary>False for an unknown, used or expired link; nothing changes then.</summary>
        Task<bool> ConfirmThroughSupportAsync(Guid userId, string token, CancellationToken ct = default);
    }
}
