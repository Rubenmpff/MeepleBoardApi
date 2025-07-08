using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class MatchRepository : IMatchRepository
    {
        private readonly MeepleBoardDbContext _context;

        public MatchRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Matches.AnyAsync(m => m.Id == id, cancellationToken);
        }

        public async ValueTask<Match?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            if (pageIndex < 0) pageIndex = 0;
            if (pageSize <= 0) pageSize = 10;

            return await _context.Matches
                .AsNoTrackingWithIdentityResolution()
                .OrderByDescending(m => m.MatchDate)
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m => m.MatchPlayers.Any(mp => mp.UserId == userId))
                .Include(m => m.Game)
                .AsSplitQuery()
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetByGameIdAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m => m.GameId == gameId)
                .Include(m => m.MatchPlayers)
                .ThenInclude(mp => mp.User)
                .AsSplitQuery()
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetMatchesByPeriodAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m => m.MatchDate >= startDate && m.MatchDate <= endDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetRecentMatchesSinceAsync(int count = 10, DateTime? startDate = null, CancellationToken cancellationToken = default)
        {
            IQueryable<Match> query = _context.Matches;

            if (startDate.HasValue)
            {
                query = query.Where(m => m.MatchDate >= startDate.Value);
            }

            return await query
                .OrderByDescending(m => m.MatchDate)
                .Take(count)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        public async Task<int> GetTotalMatchesByGameAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .AsNoTracking()
                .CountAsync(m => m.GameId == gameId, cancellationToken);
        }

        public async Task AddAsync(Match match, CancellationToken cancellationToken = default)
        {
            await _context.Matches.AddAsync(match, cancellationToken);
        }

        public async Task UpdateAsync(Match match, CancellationToken cancellationToken = default)
        {
            _context.Matches.Update(match);
            await SaveChangesAsync(cancellationToken); // ✅ Garante que as mudanças são persistidas
        }

        public async Task DeleteAsync(Guid id, Guid? gameId = null, CancellationToken cancellationToken = default)
        {
            if (gameId.HasValue)
            {
                var matches = await _context.Matches
                    .Where(m => m.GameId == gameId.Value)
                    .ToListAsync(cancellationToken);

                if (matches.Any())
                {
                    _context.Matches.RemoveRange(matches);
                }
            }
            else
            {
                var match = await _context.Matches.FindAsync(new object[] { id }, cancellationToken);
                if (match == null)
                    throw new KeyNotFoundException("A partida não foi encontrada.");

                _context.Matches.Remove(match);
            }
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
    }
}