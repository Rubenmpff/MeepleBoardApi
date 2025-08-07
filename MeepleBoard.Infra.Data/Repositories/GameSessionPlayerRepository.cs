using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class GameSessionPlayerRepository : IGameSessionPlayerRepository
    {
        private readonly MeepleBoardDbContext _context;

        public GameSessionPlayerRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<GameSessionPlayer>> GetBySessionIdAsync(Guid sessionId)
        {
            return await _context.GameSessionPlayers
                .Include(p => p.User)
                .Where(p => p.SessionId == sessionId)
                .ToListAsync();
        }

        public async Task<GameSessionPlayer?> GetByIdAsync(Guid id)
        {
            return await _context.GameSessionPlayers
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<GameSessionPlayer?> GetBySessionAndUserAsync(Guid sessionId, Guid userId)
        {
            return await _context.GameSessionPlayers
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.UserId == userId);
        }

        public async Task AddAsync(GameSessionPlayer player)
        {
            await _context.GameSessionPlayers.AddAsync(player);
        }

        public async Task RemoveAsync(Guid id)
        {
            var player = await GetByIdAsync(id);
            if (player != null)
            {
                _context.GameSessionPlayers.Remove(player);
            }
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
