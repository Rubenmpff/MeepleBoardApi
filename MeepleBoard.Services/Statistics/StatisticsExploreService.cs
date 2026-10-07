using Microsoft.EntityFrameworkCore;
using MeepleBoard.Domain.Enums;
namespace MeepleBoard.Services.Statistics;

public sealed partial class StatisticsService
{
 private IQueryable<Guid> OwnMatches(Guid userId, StatisticsQuery query) {
  var (_, start, end) = StatisticsCalculator.Validate(query);
  return db.MatchPlayers.Where(p => p.UserId == userId && p.Match!.MatchDate >= start && p.Match.MatchDate < end
   && (!query.GameId.HasValue || p.Match.GameId == query.GameId)
   && (query.Mode == null || (query.Mode == "UNKNOWN" ? p.Match.GameMode == null || !StatisticsCalculator.Modes.Take(3).Contains(p.Match.GameMode) : p.Match.GameMode == query.Mode)))
   .Select(p => p.MatchId);
 }
 private async Task<List<Guid>> AcceptedFriends(Guid userId, CancellationToken ct) => await db.Friendships.AsNoTracking()
  .Where(f => f.Status == FriendshipStatus.Accepted && (f.UserAId == userId || f.UserBId == userId))
  .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId).ToListAsync(ct);
 private async Task RequireFriend(Guid userId, Guid friendId, CancellationToken ct) {
  if (userId == Guid.Empty || friendId == Guid.Empty || friendId == userId || !await db.Friendships.AnyAsync(f => f.Status == FriendshipStatus.Accepted
   && ((f.UserAId == userId && f.UserBId == friendId) || (f.UserBId == userId && f.UserAId == friendId)), ct))
   throw new UnauthorizedAccessException("Esta comparação só está disponível para amigos aceites.");
 }
 private async Task<List<Companion>> Companions(Guid userId, StatisticsQuery query, CancellationToken ct) {
  var friends = await AcceptedFriends(userId, ct); var own = OwnMatches(userId, query);
  var counts = await db.MatchPlayers.AsNoTracking().Where(p => friends.Contains(p.UserId) && own.Contains(p.MatchId))
   .GroupBy(p => p.UserId).Select(g => new { Id = g.Key, Matches = g.Select(p => p.MatchId).Distinct().Count() }).ToListAsync(ct);
  var names = await db.Users.AsNoTracking().Where(u => friends.Contains(u.Id)).Select(u => new { u.Id, u.UserName }).ToListAsync(ct);
  return names.Select(n => new Companion(n.Id, n.UserName ?? "", counts.FirstOrDefault(c => c.Id == n.Id)?.Matches ?? 0))
   .OrderByDescending(c => c.Matches).ThenBy(c => c.FriendId).ToList();
 }
 private async Task<(List<PairRow> Rows, TimeZoneInfo Zone)> Pairs(Guid userId, Guid friendId, StatisticsQuery query, CancellationToken ct) {
  await RequireFriend(userId, friendId, ct);
  var (ownRows, zone) = await Load(userId, query, ct); var own = OwnMatches(userId, query);
  var partners = await db.MatchPlayers.AsNoTracking().Where(p => p.UserId == friendId && own.Contains(p.MatchId))
   .Select(p => new { p.MatchId, p.Score, p.Outcome,
    OtherWin = p.Match!.MatchPlayers.Any(x => x.UserId != userId && x.UserId != friendId && x.Outcome == "Win"),
    OtherDraw = p.Match.MatchPlayers.Any(x => x.UserId != userId && x.UserId != friendId && x.Outcome == "Draw") }).ToListAsync(ct);
  var lookup = ownRows.ToDictionary(r => r.MatchId);
  return (partners.Select(p => new PairRow(lookup[p.MatchId], p.Score, p.Outcome, p.OtherWin, p.OtherDraw)).DistinctBy(p => p.Own.MatchId).ToList(), zone);
 }
 public async Task<CompanyReport> Company(Guid userId, StatisticsQuery query, Guid? friendId, CancellationToken ct) {
  StatisticsCalculator.Validate(query);
  var companions = await Companions(userId, query, ct);
  if (!friendId.HasValue) return new(null, null, 0, CompanyCalculator.Results([]), companions, [], [], []);
  var (rows, zone) = await Pairs(userId, friendId.Value, query, ct);
  var name = await db.Users.Where(u => u.Id == friendId).Select(u => u.UserName).SingleAsync(ct);
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
   StatisticsCalculator.Games(rows.Select(p => p.Own)), games, buckets);
 }
 private async Task<StatisticsMatchPage> CompanyMatches(Guid userId, StatisticsMatchesQuery query, CancellationToken ct) {
  var (rows, zone) = await Pairs(userId, query.FriendId!.Value, query.ToQuery(), ct);
  // Validate even for empty samples.
  if (!CompanyCalculator.Metrics.Contains(query.Metric) && query.Metric != "all") throw new ArgumentException("Indicador inválido.");
  var result = rows.Where(p => CompanyCalculator.Matches(p, query.Metric == "all" ? "pair-all" : query.Metric))
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
  var first = await db.MatchPlayers.AsNoTracking().Where(p => p.UserId == userId).GroupBy(p => p.Match!.GameId)
   .Select(g => new { GameId = g.Key, Date = g.Min(p => p.Match!.MatchDate) }).ToDictionaryAsync(x => x.GameId, x => x.Date, ct);
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
  var libraries = await db.UserGameLibraries.AsNoTracking().Where(l => l.UserId == userId && l.AddedAt >= start && l.AddedAt < end
   && (!query.GameId.HasValue || l.GameId == query.GameId))
   .Select(l => new { l.Id, l.GameId, l.Game!.Name, l.Game.ImageUrl, l.Status, l.PricePaid, l.AddedAt }).ToListAsync(ct);
  var modeGames = query.Mode == null ? null : (await db.MatchPlayers.Where(p => p.UserId == userId && (query.Mode == "UNKNOWN" ? p.Match!.GameMode == null || !StatisticsCalculator.Modes.Take(3).Contains(p.Match.GameMode) : p.Match!.GameMode == query.Mode)).Select(p => p.Match!.GameId).Distinct().ToListAsync(ct)).ToHashSet();
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
