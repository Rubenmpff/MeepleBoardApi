public interface IFriendshipRepository
{
    Task<Friendship?> GetByPairAsync(Guid userId1, Guid userId2, CancellationToken ct = default);
    Task AddAsync(Friendship friendship, CancellationToken ct = default);
    Task UpdateAsync(Friendship friendship, CancellationToken ct = default);
    Task<bool> ExistsAcceptedAsync(Guid userId1, Guid userId2, CancellationToken ct = default);

    Task<IReadOnlyList<FriendListProjection>> GetFriendsLiteAsync(
        Guid currentUserId, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid RequestId, Guid FromUserId, string FromUserName, DateTime CreatedAt)>>
        GetIncomingRequestsAsync(Guid currentUserId, CancellationToken ct = default);

    Task<IReadOnlyList<(Guid RequestId, Guid ToUserId, string ToUserName, DateTime CreatedAt)>>
        GetOutgoingRequestsAsync(Guid currentUserId, CancellationToken ct = default);

    Task<IReadOnlyList<FriendSearchProjection>> SearchUsersAsync(
        Guid currentUserId, string query, int take, CancellationToken ct = default);

    Task<FriendProfileProjection?> GetProfileAsync(
        Guid currentUserId, Guid targetUserId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<Friendship?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Partidas onde ambos os utilizadores participaram, paginadas e ordenadas
    /// da mais recente para a mais antiga. Usado na tab "Partidas" do perfil de amigo.
    /// </summary>
    Task<(IReadOnlyList<SharedMatchDetailProjection> Items, int TotalCount)> GetSharedMatchesAsync(
        Guid currentUserId, Guid targetUserId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Histórico de partidas de um jogo específico onde ambos os utilizadores participaram.
    /// Usado no ecrã "Histórico em conjunto".
    /// </summary>
    Task<IReadOnlyList<SharedMatchDetailProjection>> GetSharedMatchesForGameAsync(
        Guid currentUserId, Guid targetUserId, Guid gameId, CancellationToken ct = default);
}

public sealed record FriendSearchProjection(
    Guid Id, string UserName, string? ProfilePictureUrl, FriendshipStatus? Status,
    Guid? RequestId, Guid? InitiatorId, bool IsOnline);

public sealed record FriendListProjection(
    Guid FriendId, string FriendUserName, string? ProfilePictureUrl,
    int SharedMatchesCount, DateTime? LastPlayedAt, string? MostPlayedGame, bool IsOnline);

public sealed record SharedGameProjection(
    Guid GameId, string Name, string? ImageUrl, int MatchesCount,
    int CurrentUserWins, int OtherUserWins, int TeamWins, bool IsCooperative);

public sealed record SharedMatchProjection(
    Guid MatchId, Guid GameId, string GameName, string? ImageUrl,
    DateTime MatchDate, string Result);

public sealed record SharedSessionProjection(
    Guid SessionId, string Name, DateTime Date, int MatchesCount);

public sealed record FriendProfileProjection(
    Guid Id, string UserName, string? ProfilePictureUrl, FriendshipStatus? Status,
    Guid? RequestId, Guid? InitiatorId, DateTime? FriendsSince,
    int TotalMatches, int TotalGamesPlayed, int TotalGamesOwned,
    int SharedMatches, int SharedGames, int SharedMinutes, int SharedSessions,
    int CurrentUserWins, int OtherUserWins, int Draws,
    IReadOnlyList<SharedGameProjection> TopSharedGames,
    IReadOnlyList<SharedGameProjection> CommonOwnedGames,
    IReadOnlyList<SharedMatchProjection> RecentSharedMatches,
    IReadOnlyList<SharedSessionProjection> RecentSharedSessions,
    string LibraryPrivacy, bool CanViewLibrary, bool IsOnline);

/// <summary>
/// Partida partilhada com detalhe suficiente para a tab "Partidas" e o
/// "Histórico em conjunto" (pontuações, duração, local) — mais rica que
/// <see cref="SharedMatchProjection"/>, que só serve o resumo do perfil.
/// </summary>
public sealed record SharedMatchDetailProjection(
    Guid MatchId, Guid GameId, string GameName, string? ImageUrl,
    DateTime MatchDate, string Result, int? DurationInMinutes, string? Location,
    int? CurrentUserScore, int? OtherUserScore);