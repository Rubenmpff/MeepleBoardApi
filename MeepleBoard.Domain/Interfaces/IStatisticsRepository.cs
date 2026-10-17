using MeepleBoard.Domain.Enums;

namespace MeepleBoard.Domain.Interfaces
{
    /// <summary>
    /// Reads for one person's statistics: their matches, accepted friends and library. Read-only; selects only this
    /// participant and this person's diary — no friend notes, ratings or prices.
    /// </summary>
    public interface IStatisticsRepository
    {
        /// <summary>One row per match this person played in the period (UTC bounds), with their own diary rating.</summary>
        Task<IReadOnlyList<StatisticsRow>> GetPlayedRowsAsync(Guid userId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default);
        Task<IReadOnlyList<Guid>> GetAcceptedFriendIdsAsync(Guid userId, CancellationToken ct = default);
        Task<bool> AreAcceptedFriendsAsync(Guid userId, Guid friendId, CancellationToken ct = default);
        /// <summary>Friends with at least one shared match under the filter, with how many, in the given company context.</summary>
        Task<IReadOnlyList<CompanionProjection>> GetCompanionsAsync(StatisticsMatchFilter filter, IReadOnlyList<Guid> friendIds, string groupContext, CancellationToken ct = default);
        /// <summary>The friend's side of every match under the filter that both played.</summary>
        Task<IReadOnlyList<PartnerProjection>> GetPartnerRowsAsync(StatisticsMatchFilter filter, Guid friendId, CancellationToken ct = default);
        /// <summary>The account name; fails when the account does not exist.</summary>
        Task<string?> GetUserNameAsync(Guid userId, CancellationToken ct = default);
        /// <summary>First recorded match date of every game this person played.</summary>
        Task<Dictionary<Guid, DateTime>> GetFirstPlayedDatesAsync(Guid userId, CancellationToken ct = default);
        Task<IReadOnlyList<LibraryEntryProjection>> GetLibraryAddedAsync(Guid userId, DateTime startUtc, DateTime endUtc, Guid? gameId, CancellationToken ct = default);
        /// <summary>Games this person played in a mode; «UNKNOWN» is any mode outside the known ones.</summary>
        Task<IReadOnlyList<Guid>> GetGameIdsPlayedInModeAsync(Guid userId, string mode, IReadOnlyList<string> knownModes, CancellationToken ct = default);
    }

    public sealed record StatisticsRow(Guid MatchId, Guid GameId, string GameName, string? GameImageUrl,
        DateTime MatchDate, string? GameMode, string? Result, string? Outcome, int? DurationInMinutes,
        int? Score, double? PersonalRating);

    /// <summary>This person's matches in a period (UTC bounds), optionally one game and one mode («UNKNOWN»: outside KnownModes).</summary>
    public sealed record StatisticsMatchFilter(Guid UserId, DateTime StartUtc, DateTime EndUtc, Guid? GameId, string? Mode, IReadOnlyList<string> KnownModes);

    public sealed record CompanionProjection(Guid Id, string? UserName, int Matches);

    public sealed record PartnerProjection(Guid MatchId, int? Score, string? Outcome, int ParticipantCount, bool OtherWin, bool OtherDraw);

    public sealed record LibraryEntryProjection(Guid Id, Guid GameId, string Name, string? ImageUrl, GameLibraryStatus Status, decimal? PricePaid, DateTime AddedAt);
}
