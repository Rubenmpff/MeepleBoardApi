using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Calculators;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;

namespace MeepleBoard.Services.Implementations;

/// <summary>
/// One person's statistics. Reads through IStatisticsRepository (this participant and this person's diary only) and
/// calculates with StatisticsCalculator and CompanyCalculator. Company comparisons require an accepted friend.
/// </summary>
public sealed class StatisticsService(IStatisticsRepository statistics) : IStatisticsService
{
    private async Task<(List<StatisticsRow> Rows, TimeZoneInfo Zone)> Load(Guid userId, StatisticsQuery query, CancellationToken ct)
    {
        if (userId == Guid.Empty) throw new UnauthorizedAccessException();
        var (zone, start, end) = StatisticsCalculator.Validate(query);
        // SQL selects only this participant and this person's diary; no friend IDs, names, notes or prices.
        var rows = await statistics.GetPlayedRowsAsync(userId, start, end, ct);
        return (rows.Where(r => query.Mode == null || StatisticsCalculator.Mode(r) == query.Mode).DistinctBy(r => r.MatchId).ToList(), zone);
    }
    public async Task<StatisticsSummary> Summary(Guid userId, StatisticsQuery query, CancellationToken ct)
    {
        var (rows, zone) = await Load(userId, query, ct);
        return StatisticsCalculator.Summary(rows, query, zone);
    }
    public async Task<StatisticsMatchPage> Matches(Guid userId, StatisticsMatchesQuery query, CancellationToken ct)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > 100) throw new ArgumentException("Paginação inválida.");
        CompanyCalculator.ValidateContext(query.GroupContext);
        if (!query.FriendId.HasValue && query.GroupContext != "all") throw new ArgumentException("Seleciona um amigo para filtrar o contexto de companhia.");
        if (query.FriendId.HasValue) return await CompanyMatches(userId, query, ct);
        StatisticsCalculator.MatchesMetric(new(Guid.Empty, Guid.Empty, "", null, DateTime.UtcNow, null, null, null, null, null, null), query.Metric);
        var filter = query.ToQuery(); var (rows, zone) = await Load(userId, filter, ct);
        var result = rows.Where(r => !query.GameId.HasValue || r.GameId == query.GameId)
            .Where(r => StatisticsCalculator.MatchesMetric(r, query.Metric))
            .Where(r => !query.ScoreValue.HasValue || r.Score == query.ScoreValue)
            .Where(r => query.Bucket == null || StatisticsCalculator.Bucket(r, filter, zone) == query.Bucket)
            .OrderByDescending(r => r.MatchDate).ThenBy(r => r.MatchId).ToList();
        return new(result.Count, query.Offset, query.Limit, result.Skip(query.Offset).Take(query.Limit).Select(r => new StatisticsMatch(r.MatchId, r.GameId, r.GameName,
            r.GameImageUrl, DateTime.SpecifyKind(r.MatchDate, DateTimeKind.Utc), StatisticsCalculator.Mode(r), StatisticsCalculator.Outcome(r),
            r.Result == null ? "Legacy" : "Explicit", r.DurationInMinutes, r.Score, r.PersonalRating)).ToList());
    }

 private static StatisticsMatchFilter Filter(Guid userId, StatisticsQuery query) {
  var (_, start, end) = StatisticsCalculator.Validate(query);
  return new(userId, start, end, query.GameId, query.Mode, KnownModes);
 }
 /// <summary>The three explicit modes; anything else is «UNKNOWN».</summary>
 private static readonly string[] KnownModes = StatisticsCalculator.Modes.Take(3).ToArray();
 private async Task RequireFriend(Guid userId, Guid friendId, CancellationToken ct) {
  if (userId == Guid.Empty || friendId == Guid.Empty || friendId == userId || !await statistics.AreAcceptedFriendsAsync(userId, friendId, ct))
   throw new UnauthorizedAccessException("Esta comparação só está disponível para amigos aceites.");
 }
 private async Task<List<Companion>> Companions(Guid userId, StatisticsQuery query, CancellationToken ct, string groupContext = "all") {
  var friends = await statistics.GetAcceptedFriendIdsAsync(userId, ct);
  var names = await statistics.GetCompanionsAsync(Filter(userId, query), friends, groupContext, ct);
  return names.Select(n => new Companion(n.Id, n.UserName ?? "", n.Matches))
   .OrderByDescending(c => c.Matches).ThenBy(c => c.FriendId).ToList();
 }
 private async Task<(List<PairRow> Rows, TimeZoneInfo Zone)> Pairs(Guid userId, Guid friendId, StatisticsQuery query, CancellationToken ct) {
  await RequireFriend(userId, friendId, ct);
  var (ownRows, zone) = await Load(userId, query, ct);
  var partners = await statistics.GetPartnerRowsAsync(Filter(userId, query), friendId, ct);
  var lookup = ownRows.ToDictionary(r => r.MatchId);
  return (partners.Select(p => new PairRow(lookup[p.MatchId], p.Score, p.Outcome, p.OtherWin, p.OtherDraw, p.ParticipantCount)).DistinctBy(p => p.Own.MatchId).ToList(), zone);
 }
 public async Task<CompanyReport> Company(Guid userId, StatisticsQuery query, Guid? friendId, CancellationToken ct, string groupContext = "all") {
  StatisticsCalculator.Validate(query);
  CompanyCalculator.ValidateContext(groupContext);
  var companions = await Companions(userId, query, ct, groupContext);
  if (!friendId.HasValue) return new(null, null, 0, CompanyCalculator.Results([]), companions, [], [], []) { GroupContext = groupContext };
  var (rows, zone) = await Pairs(userId, friendId.Value, query, ct);
  var counts = CompanyCalculator.ContextCounts(rows);
  rows = rows.Where(p => CompanyCalculator.InContext(p, groupContext)).ToList();
  var name = await statistics.GetUserNameAsync(friendId.Value, ct);
  var games = rows.GroupBy(p => new { p.Own.GameId, Mode = StatisticsCalculator.Mode(p.Own) }).Select(g => {
   var sample = g.ToList(); var first = sample[0].Own;
   return new PairGame(first.GameId, first.GameName, first.GameImageUrl, g.Key.Mode, sample.Count, CompanyCalculator.Results(sample),
    sample.Count(p => p.Own.Score.HasValue && p.FriendScore.HasValue), sample.OrderBy(p => p.Own.MatchDate).Select(p => new ScoreComparison(p.Own.MatchId, DateTime.SpecifyKind(p.Own.MatchDate, DateTimeKind.Utc), p.Own.Score, p.FriendScore)).ToList());
  }).OrderByDescending(g => g.Matches).ThenBy(g => g.GameId).ToList();
  var buckets = StatisticsCalculator.Evolution(rows.Select(p => p.Own).ToList(), query, zone).Select(b => {
   var sample = rows.Where(p => StatisticsCalculator.Bucket(p.Own, query, zone) == b.Key).ToList();
   return new PairBucket(b.Key, sample.Count, CompanyCalculator.Results(sample));
  }).ToList();
  return new(friendId, name, rows.Count, CompanyCalculator.Results(rows), companions,
   StatisticsCalculator.Games(rows.Select(p => p.Own)), games, buckets) { GroupContext = groupContext, ContextCounts = counts };
 }
 private async Task<StatisticsMatchPage> CompanyMatches(Guid userId, StatisticsMatchesQuery query, CancellationToken ct) {
  CompanyCalculator.ValidateContext(query.GroupContext);
  var (rows, zone) = await Pairs(userId, query.FriendId!.Value, query.ToQuery(), ct);
  // Validate even for empty samples.
  if (!CompanyCalculator.Metrics.Contains(query.Metric) && query.Metric != "all") throw new ArgumentException("Indicador inválido.");
  var result = rows.Where(p => CompanyCalculator.InContext(p, query.GroupContext)).Where(p => CompanyCalculator.Matches(p, query.Metric == "all" ? "pair-all" : query.Metric))
   .Where(p => query.Bucket == null || StatisticsCalculator.Bucket(p.Own, query.ToQuery(), zone) == query.Bucket)
   .OrderByDescending(p => p.Own.MatchDate).ThenBy(p => p.Own.MatchId).ToList();
  return new(result.Count, query.Offset, query.Limit, result.Skip(query.Offset).Take(query.Limit).Select(p => {
   var r = p.Own;
   return new StatisticsMatch(r.MatchId, r.GameId, r.GameName, r.GameImageUrl, DateTime.SpecifyKind(r.MatchDate, DateTimeKind.Utc), StatisticsCalculator.Mode(r),
    StatisticsCalculator.Outcome(r), r.Result == null ? "Legacy" : "Explicit", r.DurationInMinutes, r.Score, r.PersonalRating, p.FriendScore,
    r.Result == "Undefined" ? "Undefined" : r.Result is "Win" or "Loss" or "Draw" && p.FriendOutcome is "Win" or "Loss" or "Draw" ? p.FriendOutcome : null);
  }).ToList());
 }
 public async Task<ExploreReport> Explore(Guid userId, StatisticsQuery query, CancellationToken ct) {
  var (available, zone) = await Load(userId, query, ct);
  var rows = available.Where(r => !query.GameId.HasValue || r.GameId == query.GameId).ToList();
  var first = await statistics.GetFirstPlayedDatesAsync(userId, ct);
  var games = rows.GroupBy(r => new { r.GameId, Mode = StatisticsCalculator.Mode(r) }).Select(g => {
   var sample = g.ToList(); var scores = sample.Where(r => r.Score.HasValue).Select(r => r.Score!.Value).ToList();
   var ratings = sample.Where(r => r.PersonalRating.HasValue).Select(r => r.PersonalRating!.Value).ToList(); var r = sample[0];
   return new ExploreGame(r.GameId, r.GameName, r.GameImageUrl, g.Key.Mode, sample.Count, StatisticsCalculator.Results(sample), scores.Count,
    scores.Count == 0 ? null : scores.Min(), scores.Count == 0 ? null : scores.Max(), scores.Count == 0 ? null : Math.Round(scores.Average(), 2),
    ratings.Count, ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2), DateTime.SpecifyKind(first[r.GameId], DateTimeKind.Utc));
  }).OrderByDescending(g => g.Matches).ThenBy(g => g.GameId).ToList();
  var ratingBuckets = StatisticsCalculator.Evolution(rows, query, zone).Select(b => {
   var ratings = rows.Where(r => StatisticsCalculator.Bucket(r, query, zone) == b.Key && r.PersonalRating.HasValue).Select(r => r.PersonalRating!.Value).ToList();
   return new RatingBucket(b.Key, ratings.Count, ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2));
  }).ToList();
  var (_, start, end) = StatisticsCalculator.Validate(query);
  var libraries = await statistics.GetLibraryAddedAsync(userId, start, end, query.GameId, ct);
  var modeGames = query.Mode == null ? null : (await statistics.GetGameIdsPlayedInModeAsync(userId, query.Mode, KnownModes, ct)).ToHashSet();
  var collection = libraries.Where(l => modeGames == null || modeGames.Contains(l.GameId)).Select(l => new LibraryStatistic(l.Id, l.GameId, l.Name, l.ImageUrl,
   l.Status.ToString(), l.PricePaid, DateTime.SpecifyKind(l.AddedAt, DateTimeKind.Utc), first.ContainsKey(l.GameId), rows.Count(r => r.GameId == l.GameId))).ToList();
  return new(games, ratingBuckets, collection);
 }
 public async Task<YearReport> Year(Guid userId, int year, string timeZone, Guid? gameId, string? mode, CancellationToken ct) {
  if (year is < 1900 or > 9998) throw new ArgumentException("Ano inválido.");
  var query = new StatisticsQuery { Start = new(year, 1, 1), EndExclusive = new(year + 1, 1, 1), TimeZone = timeZone, GameId = gameId, Mode = mode };
  var summary = await Summary(userId, query, ct); var explore = await Explore(userId, query, ct); var companions = (await Companions(userId, query, ct)).Where(c => c.Matches > 0).ToList();
  var (_, start, end) = StatisticsCalculator.Validate(query);
  var games = summary.Games;
  var mostPlayed = games.Count == 0 ? [] : games.Where(g => g.Matches == games.Max(g => g.Matches)).Select(g => new YearGame(g.GameId, g.Name, g.ImageUrl, g.Matches, 0, 0)).ToList();
  var wins = explore.Games.Where(g => g.Results.Wins > 0).ToList();
  var mostWins = wins.Count == 0 ? [] : wins.Where(g => g.Results.Wins == wins.Max(g => g.Results.Wins)).Select(g => new YearGame(g.GameId, g.Name, g.ImageUrl, g.Matches, g.Results.Wins, g.Results.Known, g.Mode)).ToList();
  var rates = explore.Games.Where(g => g.Results.Known >= 5).ToList();
  var rateLeader = rates.OrderByDescending(g => (double)g.Results.Wins / g.Results.Known).FirstOrDefault();
  var bestRate = rateLeader == null ? [] : rates.Where(g => (long)g.Results.Wins * rateLeader.Results.Known == (long)rateLeader.Results.Wins * g.Results.Known).Select(g => new YearGame(g.GameId, g.Name, g.ImageUrl, g.Matches, g.Results.Wins, g.Results.Known, g.Mode)).ToList();
  var months = summary.Evolution.Where(b => b.Matches > 0).ToList();
  var active = months.Count == 0 ? [] : months.Where(b => b.Matches == months.Max(b => b.Matches)).ToList();
  var firstGames = explore.Games.Where(g => g.FirstRecorded >= start && g.FirstRecorded < end).DistinctBy(g => g.GameId)
   .Select(g => new StatisticsGame(g.GameId, g.Name, g.ImageUrl, summary.Games.First(x => x.GameId == g.GameId).Matches)).ToList();
  return new(year, timeZone, summary, mostPlayed, mostWins, bestRate, 5,
   companions.Count == 0 ? null : companions.Max(c => c.Matches), companions.Count == 0 ? 0 : companions.Count(c => c.Matches == companions.Max(x => x.Matches)), active, firstGames);
 }
}
