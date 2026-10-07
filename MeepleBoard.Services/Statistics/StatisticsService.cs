using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
namespace MeepleBoard.Services.Statistics;

public sealed class StatisticsService(MeepleBoardDbContext db)
{
    private async Task<(List<StatisticsRow> Rows, TimeZoneInfo Zone)> Load(Guid userId, StatisticsQuery query, CancellationToken ct)
    {
        if (userId == Guid.Empty) throw new UnauthorizedAccessException();
        var (zone, start, end) = StatisticsCalculator.Validate(query);
        // SQL selects only this participant and this person's diary; no friend IDs, names, notes or prices.
        var rows = await db.MatchPlayers.AsNoTracking().Where(p => p.UserId == userId && p.Match!.MatchDate >= start && p.Match.MatchDate < end)
            .Select(p => new StatisticsRow(p.MatchId, p.Match!.GameId, p.Match.Game!.Name, p.Match.Game.ImageUrl, p.Match.MatchDate,
                p.Match.GameMode, p.Match.Result, p.Outcome, p.Match.DurationInMinutes, p.Score,
                p.Match.JournalEntries.Where(e => e.UserId == userId).Select(e => e.PersonalRating).FirstOrDefault()))
            .ToListAsync(ct);
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
        StatisticsCalculator.MatchesMetric(new(Guid.Empty, Guid.Empty, "", null, DateTime.UtcNow, null, null, null, null, null, null), query.Metric);
        var filter = query.ToQuery(); var (rows, zone) = await Load(userId, filter, ct);
        var result = rows.Where(r => !query.GameId.HasValue || r.GameId == query.GameId)
            .Where(r => StatisticsCalculator.MatchesMetric(r, query.Metric))
            .Where(r => query.Bucket == null || StatisticsCalculator.Bucket(r, filter, zone) == query.Bucket)
            .OrderByDescending(r => r.MatchDate).ThenBy(r => r.MatchId).ToList();
        return new(result.Count, query.Offset, query.Limit, result.Skip(query.Offset).Take(query.Limit).Select(r => new StatisticsMatch(r.MatchId, r.GameId, r.GameName,
            r.GameImageUrl, DateTime.SpecifyKind(r.MatchDate, DateTimeKind.Utc), StatisticsCalculator.Mode(r), StatisticsCalculator.Outcome(r),
            r.Result == null ? "Legacy" : "Explicit", r.DurationInMinutes, r.Score, r.PersonalRating)).ToList());
    }
}
