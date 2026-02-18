public interface IFriendshipRepository
{
    Task<Friendship?> GetByPairAsync(Guid userId1, Guid userId2, CancellationToken ct = default);
    Task AddAsync(Friendship friendship, CancellationToken ct = default);
    Task UpdateAsync(Friendship friendship, CancellationToken ct = default);
    Task<bool> ExistsAcceptedAsync(Guid userId1, Guid userId2, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid FriendId, string FriendUserName)>> GetFriendsLiteAsync(
        Guid currentUserId, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid RequestId, Guid FromUserId, string FromUserName, DateTime CreatedAt)>>
        GetIncomingRequestsAsync(Guid currentUserId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<Friendship?> GetByIdAsync(Guid id, CancellationToken ct = default);

}
