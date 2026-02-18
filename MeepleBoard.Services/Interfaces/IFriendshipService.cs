using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    public interface IFriendshipService
    {
        Task<IReadOnlyList<FriendLiteDto>> GetMyFriendsAsync(Guid currentUserId, CancellationToken ct = default);
        Task<IReadOnlyList<FriendRequestDto>> GetIncomingAsync(Guid currentUserId, CancellationToken ct = default);

        Task RequestFriendshipAsync(Guid currentUserId, Guid toUserId, CancellationToken ct = default);
        Task AcceptAsync(Guid currentUserId, Guid requestId, CancellationToken ct = default);
        Task RejectAsync(Guid currentUserId, Guid requestId, CancellationToken ct = default);
    }

}
