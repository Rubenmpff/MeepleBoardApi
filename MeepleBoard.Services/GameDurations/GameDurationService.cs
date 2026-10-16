using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Services.GameDurations
{
    /// <summary>
    /// Reads, from the current data, what the duration statistics of a game need: one row per match, whether the
    /// viewer took part and whether every participant shares durations. Nothing is cached, so a corrected
    /// duration, a deleted match or a changed preference applies on the next read. Only aggregates leave here.
    /// </summary>
    public sealed class GameDurationService(MeepleBoardDbContext db)
    {
        public async Task<GameDurationsDto> GetAsync(Guid gameId, Guid viewerId, CancellationToken ct = default)
        {
            if (viewerId == Guid.Empty) throw new UnauthorizedAccessException();
            // Mode: the explicit mode; matches recorded before it existed use their stored solo flag (never a catalogue guess).
            var rows = await db.Matches.AsNoTracking().Where(m => m.GameId == gameId)
                .Select(m => new DurationRow(m.Id, m.CreatorId, m.DurationInMinutes, m.DurationMethod,
                    m.GameMode != null ? m.GameMode == "SOLO" : m.IsSoloGame, m.MatchPlayers.Count,
                    m.MatchPlayers.Any(p => p.UserId == viewerId),
                    m.MatchPlayers.Any() && m.MatchPlayers.All(p => p.User!.ContributesDurationStats)))
                .ToListAsync(ct);
            var contributes = await db.Users.AsNoTracking().Where(u => u.Id == viewerId).Select(u => u.ContributesDurationStats).FirstOrDefaultAsync(ct);
            return GameDurationCalculator.Calculate(rows) with { ViewerContributes = contributes };
        }
    }
}
