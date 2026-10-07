namespace MeepleBoard.Services.Statistics;

public sealed class StatisticsQuery
{
    public DateOnly Start { get; set; }
    public DateOnly EndExclusive { get; set; }
    public string TimeZone { get; set; } = "UTC";
    public Guid? GameId { get; set; }
    public string? Mode { get; set; }
}
public sealed class StatisticsMatchesQuery : StatisticsQueryBase
{
    public string Metric { get; set; } = "all";
    public string? Bucket { get; set; }
    public Guid? FriendId { get; set; }
    public int? ScoreValue { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; } = 25;
}
// Same period/filter contract on summary and its supporting rows.
public class StatisticsQueryBase
{
    public DateOnly Start { get; set; }
    public DateOnly EndExclusive { get; set; }
    public string TimeZone { get; set; } = "UTC";
    public Guid? GameId { get; set; }
    public string? Mode { get; set; }
    public StatisticsQuery ToQuery() => new() { Start = Start, EndExclusive = EndExclusive, TimeZone = TimeZone, GameId = GameId, Mode = Mode };
}
public sealed record StatisticsRow(Guid MatchId, Guid GameId, string GameName, string? GameImageUrl,
    DateTime MatchDate, string? GameMode, string? Result, string? Outcome, int? DurationInMinutes,
    int? Score, double? PersonalRating);
public sealed record StatisticsResults(int Wins, int Losses, int Draws, int Known, int WithoutResult, int Legacy, double? WinRate);
public sealed record StatisticsMode(string Mode, int Matches, StatisticsResults Results);
public sealed record StatisticsGame(Guid GameId, string Name, string? ImageUrl, int Matches);
public sealed record StatisticsBucket(string Key, int Matches);
public sealed record StatisticsSummary(DateOnly Start, DateOnly EndExclusive, string TimeZone, Guid? GameId, string? Mode,
    int Matches, int DistinctGames, long? RecordedMinutes, int MatchesWithDuration, int MatchesWithoutDuration,
    double? AveragePersonalRating, int RatedMatches, StatisticsResults Results, IReadOnlyList<StatisticsMode> Modes,
    string BucketUnit, IReadOnlyList<StatisticsBucket> Evolution, IReadOnlyList<StatisticsGame> Games,
    IReadOnlyList<StatisticsGame> GameOptions);
public sealed record StatisticsMatch(Guid Id, Guid GameId, string GameName, string? GameImageUrl, DateTime MatchDate,
    string GameMode, string? Outcome, string ResultSource, int? DurationInMinutes, int? Score, double? PersonalRating, int? FriendScore = null, string? FriendOutcome = null);
public sealed record StatisticsMatchPage(int Total, int Offset, int Limit, IReadOnlyList<StatisticsMatch> Items);
