using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class MatchPlayerRepository : IMatchPlayerRepository
    {
        private readonly MeepleBoardDbContext _context;

        public MatchPlayerRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        public async Task<bool> ExistsAsync(Guid userId, Guid matchId, CancellationToken cancellationToken = default)
        {
            return await _context.MatchPlayers
                .AsNoTracking()
                .AnyAsync(mp => mp.UserId == userId && mp.MatchId == matchId, cancellationToken);
        }

        public async Task<IReadOnlyList<MatchPlayer>> GetByUserIdAsync(Guid userId, bool includeMatch = false, CancellationToken cancellationToken = default)
        {
            var query = _context.MatchPlayers
                .Where(mp => mp.UserId == userId)
                .AsNoTrackingWithIdentityResolution();

            if (includeMatch)
            {
                query = query.Include(mp => mp.Match!)
                             .ThenInclude(m => m.Game!);
            }

            return await query.ToListAsync(cancellationToken);
        }

        public async Task<int> GetTotalMatchesByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.MatchPlayers
                .AsNoTracking()
                .CountAsync(mp => mp.UserId == userId, cancellationToken);
        }

        public async Task<int> GetTotalWinsByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.MatchPlayers
                .AsNoTracking()
                .CountAsync(mp => mp.UserId == userId && mp.IsWinner, cancellationToken);
        }

        public async Task<double> GetWinRateByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var result = await _context.MatchPlayers
                .Where(mp => mp.UserId == userId)
                .GroupBy(mp => mp.UserId)
                .Select(g => new
                {
                    TotalMatches = g.Count(),
                    TotalWins = g.Count(mp => mp.IsWinner)
                })
                .FirstOrDefaultAsync(cancellationToken);

            return (result == null || result.TotalMatches == 0) ? 0 : (double)result.TotalWins / result.TotalMatches * 100;
        }

        public async Task<int> GetTotalMatchesByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            return await _context.MatchPlayers
                .Where(mp => mp.UserId == userId && mp.Match!.MatchDate >= startDate && mp.Match.MatchDate <= endDate)
                .AsNoTracking()
                .CountAsync(cancellationToken);
        }

        public async Task<int> GetTotalWinsByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            return await _context.MatchPlayers
                .Where(mp => mp.UserId == userId && mp.IsWinner && mp.Match!.MatchDate >= startDate && mp.Match.MatchDate <= endDate)
                .AsNoTracking()
                .CountAsync(cancellationToken);
        }

        public async Task<IReadOnlyDictionary<string, int>> GetMostPlayedGamesByUserAsync(Guid userId, int topN, CancellationToken cancellationToken = default)
        {
            var result = await _context.MatchPlayers
                .Include(mp => mp.Match!)
                .ThenInclude(m => m.Game!)
                .Where(mp => mp.UserId == userId && mp.Match!.Game!.Name != null)
                .GroupBy(mp => mp.Match!.Game!.Name)
                .OrderByDescending(g => g.Count())
                .Take(topN)
                .Select(g => new { GameName = g.Key, MatchesPlayed = g.Count() })
                .ToListAsync(cancellationToken);

            return result.ToDictionary(r => r.GameName, r => r.MatchesPlayed);
        }

        public async Task<MatchPlayer?> GetByMatchAndPlayerAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken = default)
        {
            return await _context.MatchPlayers
                .FirstOrDefaultAsync(mp => mp.MatchId == matchId && mp.UserId == playerId, cancellationToken);
        }

        public async Task AddAsync(MatchPlayer matchPlayer, CancellationToken cancellationToken = default)
        {
            await _context.MatchPlayers.AddAsync(matchPlayer, cancellationToken);
        }

        public async Task DeleteAsync(MatchPlayer matchPlayer, CancellationToken cancellationToken = default)
        {
            _context.MatchPlayers.Remove(matchPlayer);
            await CommitAsync(cancellationToken); // 🔹 Garante a persistência da exclusão
        }

        public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
    }
}