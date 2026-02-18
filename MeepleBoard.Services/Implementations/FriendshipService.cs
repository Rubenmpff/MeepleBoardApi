
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;

public sealed class FriendshipService : IFriendshipService
{
    private readonly IFriendshipRepository _repo;

    public FriendshipService(IFriendshipRepository repo)
    {
        _repo = repo;
    }

    public async Task<IReadOnlyList<FriendLiteDto>> GetMyFriendsAsync(Guid currentUserId, CancellationToken ct = default)
    {
        var list = await _repo.GetFriendsLiteAsync(currentUserId, ct);
        return list.Select(x => new FriendLiteDto(x.FriendId, x.FriendUserName)).ToList();
    }

    public async Task<IReadOnlyList<FriendRequestDto>> GetIncomingAsync(Guid currentUserId, CancellationToken ct = default)
    {
        var list = await _repo.GetIncomingRequestsAsync(currentUserId, ct);
        return list.Select(x => new FriendRequestDto(x.RequestId, x.FromUserId, x.FromUserName, x.CreatedAt)).ToList();
    }

    public async Task RequestFriendshipAsync(Guid currentUserId, Guid toUserId, CancellationToken ct = default)
    {
        if (currentUserId == toUserId) throw new InvalidOperationException("You cannot add yourself.");

        var existing = await _repo.GetByPairAsync(currentUserId, toUserId, ct);
        if (existing is not null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
                throw new InvalidOperationException("Already friends.");
            if (existing.Status == FriendshipStatus.Pending)
                throw new InvalidOperationException("Friend request already pending.");
            if (existing.Status == FriendshipStatus.Blocked)
                throw new InvalidOperationException("Cannot request friendship.");
        }

        // criar novo pedido
        var (a, b) = currentUserId.CompareTo(toUserId) < 0 ? (currentUserId, toUserId) : (toUserId, currentUserId);

        var friendship = new Friendship
        {
            Id = Guid.NewGuid(),
            UserAId = a,
            UserBId = b,
            InitiatorId = currentUserId,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _repo.AddAsync(friendship, ct);
        await _repo.SaveChangesAsync(ct);
    }

    public async Task AcceptAsync(Guid currentUserId, Guid requestId, CancellationToken ct = default)
    {
        var f = await _repo.GetByIdAsync(requestId, ct);
        if (f is null) throw new KeyNotFoundException("Request not found.");

        // garantir que o currentUser faz parte do par e NÃO é o iniciador
        var isInPair = f.UserAId == currentUserId || f.UserBId == currentUserId;
        if (!isInPair) throw new UnauthorizedAccessException("Not allowed.");
        if (f.Status != FriendshipStatus.Pending) throw new InvalidOperationException("Request is not pending.");
        if (f.InitiatorId == currentUserId) throw new InvalidOperationException("You cannot accept your own request.");

        f.Status = FriendshipStatus.Accepted;
        f.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(f, ct);
        await _repo.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid currentUserId, Guid requestId, CancellationToken ct = default)
    {
        var f = await _repo.GetByIdAsync(requestId, ct);
        if (f is null) throw new KeyNotFoundException("Request not found.");

        var isInPair = f.UserAId == currentUserId || f.UserBId == currentUserId;
        if (!isInPair) throw new UnauthorizedAccessException("Not allowed.");
        if (f.Status != FriendshipStatus.Pending) throw new InvalidOperationException("Request is not pending.");
        if (f.InitiatorId == currentUserId) throw new InvalidOperationException("You cannot reject your own request.");

        f.Status = FriendshipStatus.Rejected;
        f.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(f, ct);
        await _repo.SaveChangesAsync(ct);
    }
}
