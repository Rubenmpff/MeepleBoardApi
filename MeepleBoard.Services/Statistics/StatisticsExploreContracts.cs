namespace MeepleBoard.Services.Statistics;

public sealed record Companion(Guid FriendId, string Name, int Matches);
public sealed record PairResults(int MyWins, int FriendWins, int SharedWins, int MyOnlyWins, int FriendOnlyWins,
 int Draws, int DrawsTogether, int OtherWins, int OtherSharedWins, int OtherDraws, int TeamWins, int TeamLosses, int TeamDraws, int Known, int Unknown);
public sealed record PairBucket(string Key, int Matches, PairResults Results);
public sealed record ScoreComparison(Guid MatchId, DateTime Date, int? Mine, int? Friend);
public sealed record PairGame(Guid GameId, string Name, string? ImageUrl, string Mode, int Matches, PairResults Results,
 int CompleteScores, IReadOnlyList<ScoreComparison> Scores);
public sealed record CompanyReport(Guid? FriendId, string? FriendName, int Matches, PairResults Results,
 IReadOnlyList<Companion> Companions, IReadOnlyList<StatisticsGame> Games, IReadOnlyList<PairGame> Comparisons,
 IReadOnlyList<PairBucket> Evolution) {
 public string GroupContext { get; init; } = "all";
 public PairContextCounts ContextCounts { get; init; } = new(0, 0, 0, 0);
}
public sealed record PairContextCounts(int All, int OnlyTwo, int Group, int Unclassified);
public sealed record ExploreGame(Guid GameId, string Name, string? ImageUrl, string Mode, int Matches,
 StatisticsResults Results, int Scored, int? Minimum, int? Maximum, double? AverageScore,
 int Rated, double? AverageRating, DateTime FirstRecorded);
public sealed record LibraryStatistic(Guid EntryId, Guid GameId, string Name, string? ImageUrl, string Status,
 decimal? PricePaid, DateTime AddedAt, bool HasRecordedMatch, int MatchesInPeriod);
public sealed record RatingBucket(string Key, int Rated, double? Average);
public sealed record ExploreReport(IReadOnlyList<ExploreGame> Games, IReadOnlyList<RatingBucket> Ratings,
 IReadOnlyList<LibraryStatistic> Collection);
// Public export payload deliberately contains no person IDs/names or private diaries.
public sealed record YearGame(Guid GameId, string Name, string? ImageUrl, int Matches, int Wins, int Known, string? Mode = null);
public sealed record YearReport(int Year, string TimeZone, StatisticsSummary Summary, IReadOnlyList<YearGame> MostPlayed,
 IReadOnlyList<YearGame> MostWins, IReadOnlyList<YearGame> BestWinRate, int RateMinimumSample,
 int? MostFrequentCompanyMatches, int MostFrequentCompanyTies, IReadOnlyList<StatisticsBucket> ActiveMonths,
 IReadOnlyList<StatisticsGame> FirstRecordedGames);
