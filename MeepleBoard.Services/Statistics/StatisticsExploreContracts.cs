namespace MeepleBoard.Services.Statistics;

public sealed record Companion(Guid FriendId, string Name, int Matches);
public sealed record PairRow(StatisticsRow Own, int? FriendScore, string? FriendOutcome, bool OtherWin, bool OtherDraw);
public sealed record PairResults(int MyWins, int FriendWins, int SharedWins, int MyOnlyWins, int FriendOnlyWins,
 int Draws, int DrawsTogether, int OtherWins, int OtherSharedWins, int OtherDraws, int TeamWins, int TeamLosses, int TeamDraws, int Known, int Unknown);
public sealed record PairBucket(string Key, int Matches, PairResults Results);
public sealed record ScoreComparison(Guid MatchId, DateTime Date, int? Mine, int? Friend);
public sealed record PairGame(Guid GameId, string Name, string? ImageUrl, string Mode, int Matches, PairResults Results,
 int CompleteScores, IReadOnlyList<ScoreComparison> Scores);
public sealed record CompanyReport(Guid? FriendId, string? FriendName, int Matches, PairResults Results,
 IReadOnlyList<Companion> Companions, IReadOnlyList<StatisticsGame> Games, IReadOnlyList<PairGame> Comparisons,
 IReadOnlyList<PairBucket> Evolution);
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

public static class CompanyCalculator
{
 public static string Category(PairRow pair) {
  var r = pair.Own; var mine = StatisticsCalculator.Outcome(r);
  var other = r.Result is "Win" or "Loss" or "Draw" && pair.FriendOutcome is "Win" or "Loss" or "Draw" ? pair.FriendOutcome : null;
  if (mine == null || other == null) return "unknown-pair";
  if (StatisticsCalculator.Mode(r) == "COOPERATIVE")
   return mine == other && mine == r.Result ? "team-" + mine.ToLowerInvariant() : "unknown-pair";
  if (StatisticsCalculator.Mode(r) != "COMPETITIVE") return "unknown-pair";
  return (r.Result, mine, other) switch {
   ("Win", "Win", "Win") => "shared-win", ("Win", "Win", "Loss") => "my-only-win",
   ("Win", "Loss", "Win") => "friend-only-win", ("Win", "Loss", "Loss") when pair.OtherWin => "other-win",
   ("Draw", "Draw", "Draw") => "draw-together", ("Draw", "Draw", "Loss") or ("Draw", "Loss", "Draw") => "draw-with-other",
   ("Draw", "Loss", "Loss") when pair.OtherDraw => "other-draw", _ => "unknown-pair"
  };
 }
 public static readonly string[] Metrics = ["pair-all", "my-win", "friend-win", "shared-win", "my-only-win", "friend-only-win", "pair-draw", "draw-together", "draw-with-other", "other-win", "other-shared-win", "other-draw", "team-win", "team-loss", "team-draw", "pair-known", "unknown-pair", "paired-scores"];
 public static bool Matches(PairRow row, string metric) {
  var category = Category(row);
  return metric switch {
   "pair-all" => true, "my-win" => category is "my-only-win" or "shared-win", "friend-win" => category is "friend-only-win" or "shared-win",
   "pair-draw" => category is "draw-together" or "draw-with-other", "pair-known" => category != "unknown-pair",
   "other-shared-win" => row.OtherWin && category is "my-only-win" or "friend-only-win" or "shared-win",
   "paired-scores" => row.Own.Score.HasValue && row.FriendScore.HasValue,
   _ when Metrics.Contains(metric) => category == metric,
   _ => throw new ArgumentException("Indicador de companhia inválido.")
  };
 }
 public static PairResults Results(IReadOnlyList<PairRow> rows) {
  int Count(string metric) => rows.Count(r => Matches(r, metric));
  return new(Count("my-win"), Count("friend-win"), Count("shared-win"), Count("my-only-win"), Count("friend-only-win"),
   Count("pair-draw"), Count("draw-together"), Count("other-win"), Count("other-shared-win"), Count("other-draw"), Count("team-win"), Count("team-loss"), Count("team-draw"), Count("pair-known"), Count("unknown-pair"));
 }
}
