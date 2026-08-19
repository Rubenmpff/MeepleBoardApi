
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
        return list.Select(x => new FriendLiteDto(x.FriendId, x.FriendUserName,
            x.ProfilePictureUrl, x.SharedMatchesCount, x.LastPlayedAt, x.MostPlayedGame, x.IsOnline)).ToList();
    }

    public async Task<IReadOnlyList<OutgoingFriendRequestDto>> GetOutgoingAsync(
        Guid currentUserId, CancellationToken ct = default)
    {
        var list = await _repo.GetOutgoingRequestsAsync(currentUserId, ct);
        return list.Select(x => new OutgoingFriendRequestDto(
            x.RequestId, x.ToUserId, x.ToUserName, x.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<UserSearchResultDto>> SearchUsersAsync(
        Guid currentUserId, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return Array.Empty<UserSearchResultDto>();

        var list = await _repo.SearchUsersAsync(currentUserId, query, 30, ct);
        return list.Select(x => new UserSearchResultDto(
            x.Id, x.UserName, x.ProfilePictureUrl,
            RelationshipStatus(x.Status, x.InitiatorId, currentUserId), x.RequestId, x.IsOnline)).ToList();
    }

    public async Task<UserProfileDto?> GetProfileAsync(
        Guid currentUserId, Guid targetUserId, CancellationToken ct = default)
    {
        var p = await _repo.GetProfileAsync(currentUserId, targetUserId, ct);
        if (p is null) return null;

        return new UserProfileDto(
            p.Id, p.UserName, p.ProfilePictureUrl,
            RelationshipStatus(p.Status, p.InitiatorId, currentUserId), p.RequestId,
            p.FriendsSince, p.TotalMatches, p.TotalGamesPlayed, p.TotalGamesOwned,
            p.SharedMatches, p.SharedGames, p.SharedMinutes, p.SharedSessions,
            p.CurrentUserWins, p.OtherUserWins, p.Draws,
            p.TopSharedGames.Select(MapGame).ToList(),
            p.CommonOwnedGames.Select(MapGame).ToList(),
            p.RecentSharedMatches.Select(x => new SharedMatchDto(
                x.MatchId, x.GameId, x.GameName, x.ImageUrl, x.MatchDate, x.Result)).ToList(),
            p.RecentSharedSessions.Select(x => new SharedSessionDto(
                x.SessionId, x.Name, x.Date, x.MatchesCount)).ToList(),
            p.LibraryPrivacy, p.CanViewLibrary, p.IsOnline);
    }

    public async Task<SharedMatchesPageDto> GetSharedMatchesAsync(
        Guid currentUserId, Guid targetUserId, int page, int pageSize, CancellationToken ct = default)
    {
        await EnsureAcceptedFriendsAsync(currentUserId, targetUserId, ct);

        page = Math.Max(0, page);
        pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 50);

        var (items, totalCount) = await _repo.GetSharedMatchesAsync(currentUserId, targetUserId, page, pageSize, ct);
        return new SharedMatchesPageDto(items.Select(MapMatchDetail).ToList(), totalCount, page, pageSize);
    }

    public async Task<IReadOnlyList<SharedMatchDetailDto>> GetSharedMatchesForGameAsync(
        Guid currentUserId, Guid targetUserId, Guid gameId, CancellationToken ct = default)
    {
        await EnsureAcceptedFriendsAsync(currentUserId, targetUserId, ct);

        var items = await _repo.GetSharedMatchesForGameAsync(currentUserId, targetUserId, gameId, ct);
        return items.Select(MapMatchDetail).ToList();
    }

    /// <summary>
    /// Garante que os dois utilizadores são amigos aceites — as partidas partilhadas
    /// são informação privada dos dois, não é para se ver sem essa relação.
    /// </summary>
    private async Task EnsureAcceptedFriendsAsync(Guid currentUserId, Guid targetUserId, CancellationToken ct)
    {
        var friendship = await _repo.GetByPairAsync(currentUserId, targetUserId, ct);
        if (friendship?.Status != FriendshipStatus.Accepted)
            throw new UnauthorizedAccessException("Só podes ver isto para amigos.");
    }

    private static SharedMatchDetailDto MapMatchDetail(SharedMatchDetailProjection x)
        => new(x.MatchId, x.GameId, x.GameName, x.ImageUrl, x.MatchDate, x.Result,
            x.DurationInMinutes, x.Location, x.CurrentUserScore, x.OtherUserScore);

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

        if (existing is not null)
        {
            existing.InitiatorId = currentUserId;
            existing.Status = FriendshipStatus.Pending;
            existing.BlockedById = null;
            existing.CreatedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(existing, ct);
            await _repo.SaveChangesAsync(ct);
            return;
        }

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

    public async Task CancelAsync(Guid currentUserId, Guid requestId, CancellationToken ct = default)
    {
        var f = await _repo.GetByIdAsync(requestId, ct)
            ?? throw new KeyNotFoundException("Request not found.");
        if (f.Status != FriendshipStatus.Pending || f.InitiatorId != currentUserId)
            throw new UnauthorizedAccessException("Not allowed.");

        f.Status = FriendshipStatus.Rejected;
        f.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(f, ct);
        await _repo.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid currentUserId, Guid friendUserId, CancellationToken ct = default)
    {
        var f = await _repo.GetByPairAsync(currentUserId, friendUserId, ct)
            ?? throw new KeyNotFoundException("Friendship not found.");
        if (f.Status != FriendshipStatus.Accepted)
            throw new InvalidOperationException("Users are not friends.");

        f.Status = FriendshipStatus.Rejected;
        f.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(f, ct);
        await _repo.SaveChangesAsync(ct);
    }

    private static string RelationshipStatus(
        FriendshipStatus? status, Guid? initiatorId, Guid currentUserId)
        => status switch
        {
            FriendshipStatus.Accepted => "friends",
            FriendshipStatus.Pending when initiatorId == currentUserId => "outgoingPending",
            FriendshipStatus.Pending => "incomingPending",
            FriendshipStatus.Blocked => "blocked",
            _ => "none"
        };

    private static SharedGameDto MapGame(SharedGameProjection x)
        => new(x.GameId, x.Name, x.ImageUrl, x.MatchesCount,
            x.CurrentUserWins, x.OtherUserWins, x.TeamWins, x.IsCooperative);
}