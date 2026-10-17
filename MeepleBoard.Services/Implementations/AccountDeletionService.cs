using MeepleBoard.Domain.Entities;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.Implementations
{
    public class AccountDeletionService(UserManager<User> users, IUserService userService, IEmailService email, ILogger<AccountDeletionService> logger) : IAccountDeletionService
    {
        // Identity's data-protection token: 24 h by default, and invalid once the account is deleted (new security stamp).
        public const string TokenPurpose = "AccountDeletion";

        public async Task<OwnAccountDeletionOutcome> DeleteOwnAsync(Guid userId, string? password, IReadOnlyDictionary<Guid, Guid> campaignTransfers, CancellationToken ct = default)
        {
            var user = await users.FindByIdAsync(userId.ToString());
            if (user == null || user.DeletedAt.HasValue) return OwnAccountDeletionOutcome.NotFound;
            // The account lockout of sign-in also limits guesses here.
            if (await users.IsLockedOutAsync(user)) return OwnAccountDeletionOutcome.LockedOut;
            if (string.IsNullOrEmpty(password) || !await users.CheckPasswordAsync(user, password))
            {
                await users.AccessFailedAsync(user);
                return await users.IsLockedOutAsync(user) ? OwnAccountDeletionOutcome.LockedOut : OwnAccountDeletionOutcome.WrongPassword;
            }
            await users.ResetAccessFailedCountAsync(user);
            try { await userService.DeleteAsync(userId, campaignTransfers, ct); }
            catch (KeyNotFoundException) { return OwnAccountDeletionOutcome.NotFound; }
            return OwnAccountDeletionOutcome.Deleted;
        }

        public async Task RequestThroughSupportAsync(string address, string confirmationUrl, CancellationToken ct = default)
        {
            var user = string.IsNullOrWhiteSpace(address) ? null : await users.FindByEmailAsync(address.Trim());
            if (user == null || user.DeletedAt.HasValue || string.IsNullOrEmpty(user.Email)) return;
            var token = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, TokenPurpose);
            var link = $"{confirmationUrl}?u={user.Id}&t={Uri.EscapeDataString(token)}";
            await email.SendAccountDeletionLinkAsync(user.Email, user.UserName ?? "", link);
        }

        public async Task<bool> IsValidLinkAsync(Guid userId, string token, CancellationToken ct = default)
            => await ValidUserAsync(userId, token) != null;

        public async Task<bool> ConfirmThroughSupportAsync(Guid userId, string token, CancellationToken ct = default)
        {
            var user = await ValidUserAsync(userId, token);
            if (user == null) return false;
            // Captured before anonymising, only to confirm the deletion; it is not kept.
            var (address, name) = (user.Email!, user.UserName ?? "");
            try { await userService.DeleteAsync(userId, null, ct); }
            catch (KeyNotFoundException) { return false; }
            try { await email.SendAccountDeletedAsync(address, name); }
            catch (Exception ex) { logger.LogWarning(ex, "Confirmação de eliminação de conta não enviada."); }
            return true;
        }

        async Task<User?> ValidUserAsync(Guid userId, string token)
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token)) return null;
            var user = await users.FindByIdAsync(userId.ToString());
            if (user == null || user.DeletedAt.HasValue || string.IsNullOrEmpty(user.Email)) return null;
            return await users.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, TokenPurpose, token) ? user : null;
        }
    }
}
