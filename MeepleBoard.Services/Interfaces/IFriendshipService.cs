using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    public interface IFriendshipService
    {
        Task<IReadOnlyList<FriendLiteDto>> GetMyFriendsAsync(Guid currentUserId, CancellationToken ct = default);
        Task<IReadOnlyList<FriendRequestDto>> GetIncomingAsync(Guid currentUserId, CancellationToken ct = default);
        Task<IReadOnlyList<OutgoingFriendRequestDto>> GetOutgoingAsync(Guid currentUserId, CancellationToken ct = default);
        Task<IReadOnlyList<UserSearchResultDto>> SearchUsersAsync(Guid currentUserId, string query, CancellationToken ct = default);
        Task<UserProfileDto?> GetProfileAsync(Guid currentUserId, Guid targetUserId, CancellationToken ct = default);

        Task<SharedMatchesPageDto> GetSharedMatchesAsync(Guid currentUserId, Guid targetUserId, int page, int pageSize, CancellationToken ct = default);

        Task<IReadOnlyList<SharedMatchDetailDto>> GetSharedMatchesForGameAsync(Guid currentUserId, Guid targetUserId, Guid gameId, CancellationToken ct = default);

        Task RequestFriendshipAsync(Guid currentUserId, Guid toUserId, CancellationToken ct = default);
        Task AcceptAsync(Guid currentUserId, Guid requestId, CancellationToken ct = default);
        Task RejectAsync(Guid currentUserId, Guid requestId, CancellationToken ct = default);
        Task CancelAsync(Guid currentUserId, Guid requestId, CancellationToken ct = default);
        Task RemoveAsync(Guid currentUserId, Guid friendUserId, CancellationToken ct = default);
    }
}