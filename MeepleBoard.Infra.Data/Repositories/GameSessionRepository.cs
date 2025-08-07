using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class GameSessionRepository : IGameSessionRepository
    {
        private readonly MeepleBoardDbContext _context;

        public GameSessionRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        public async Task<GameSession?> GetByIdAsync(Guid id)
        {
            return await _context.GameSessions
                .Include(s => s.Players)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<IEnumerable<GameSession>> GetAllAsync()
        {
            return await _context.GameSessions
                .Include(s => s.Players)
                .ToListAsync();
        }

        public async Task AddAsync(GameSession session)
        {
            await _context.GameSessions.AddAsync(session);
        }

        public Task UpdateAsync(GameSession session)
        {
            _context.GameSessions.Update(session);
            return Task.CompletedTask; // não salva aqui
        }

        public async Task DeleteAsync(Guid id)
        {
            var session = await GetByIdAsync(id);
            if (session != null)
            {
                _context.GameSessions.Remove(session);
            }
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
