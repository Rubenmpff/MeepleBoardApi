using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    public interface IUserService
    {
        Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<UserDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
        Task<IEnumerable<UserDto>> GetAllAsync(CancellationToken cancellationToken = default);
        Task AddAsync(UserDto userDto, CancellationToken cancellationToken = default);
        Task UpdateAsync(UserDto userDto, CancellationToken cancellationToken = default);
        /// <summary>Deletes (anonymises) an account; campaignTransfers maps campaigns the person created to their new manager.</summary>
        Task DeleteAsync(Guid id, IReadOnlyDictionary<Guid, Guid>? campaignTransfers = null, CancellationToken cancellationToken = default);

        /// <summary>What deleting the account would affect; null for an unknown or deleted account.</summary>
        Task<AccountDeletionSummary?> GetDeletionSummaryAsync(Guid id, CancellationToken cancellationToken = default);
        Task<UserDto> GetUserStatisticsAsync(Guid userId, CancellationToken cancellationToken = default);

        Task UpdatePushTokenAsync(Guid userId, string expoPushToken, CancellationToken cancellationToken = default);

        Task UpdateLibraryPrivacyAsync(Guid userId, LibraryPrivacy privacy, CancellationToken cancellationToken = default);
        Task UpdateDurationSharingAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default);
    }
}