using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    /// <summary>
    /// Implementação do repositório de sessões de jogo.
    /// Controla persistência e carregamento de relacionamentos.
    /// </summary>
    public class GameSessionRepository : IGameSessionRepository
    {
        private readonly MeepleBoardDbContext _context;

        public GameSessionRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        public async Task<GameSession?> GetByIdAsync(Guid id, bool includeRelations = true)
        {
            IQueryable<GameSession> query = _context.GameSessions.AsNoTracking();

            if (includeRelations)
            {
                query = query
                    .Include(s => s.Players)
                        .ThenInclude(p => p.User)
                    .Include(s => s.Matches);
            }

            return await query.FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<IEnumerable<GameSession>> GetAllAsync(bool includeRelations = true)
        {
            IQueryable<GameSession> query = _context.GameSessions.AsNoTracking();

            if (includeRelations)
            {
                query = query
                    .Include(s => s.Players)
                        .ThenInclude(p => p.User)
                    .Include(s => s.Matches);
            }

            return await query.OrderByDescending(s => s.StartDate).ToListAsync();
        }

        public async Task AddAsync(GameSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            await _context.GameSessions.AddAsync(session);
        }

        public Task UpdateAsync(GameSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            _context.GameSessions.Update(session);
            return Task.CompletedTask; // Espera que SaveChangesAsync seja chamado externamente
        }

        public async Task DeleteAsync(Guid id)
        {
            var session = await _context.GameSessions.FindAsync(id);
            if (session != null)
                _context.GameSessions.Remove(session);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
