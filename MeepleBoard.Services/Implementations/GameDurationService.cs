using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Calculators;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Reads, from the current data, what the duration statistics of a game need: one row per match, whether the
    /// viewer took part and whether every participant shares durations. Nothing is cached, so a corrected
    /// duration, a deleted match or a changed preference applies on the next read. Only aggregates leave here.
    /// </summary>
    public sealed class GameDurationService(IMatchRepository matches, IUserRepository users) : IGameDurationService
    {
        public async Task<GameDurationsDto> GetAsync(Guid gameId, Guid viewerId, CancellationToken ct = default)
        {
            if (viewerId == Guid.Empty) throw new UnauthorizedAccessException();
            var rows = await matches.GetDurationRowsForGameAsync(gameId, viewerId, ct);
            var contributes = await users.ContributesDurationStatsAsync(viewerId, ct);
            return GameDurationCalculator.Calculate(rows) with { ViewerContributes = contributes };
        }
    }
}
