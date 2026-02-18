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

        /// <summary>
        /// ✅ Lista LEVE (para screen de listagem).
        /// Sem carregar Players/User e Matches em memória.
        /// </summary>
        public async Task<IReadOnlyList<GameSession>> GetListAsync(CancellationToken ct = default)
        {
            return await _context.GameSessions
                .AsNoTracking()
                .Include(s => s.Organizer) // para OrganizerUserName
                                           // ⚠️ Não usar Include(s => s.Players) / Include(s => s.Matches) aqui
                .OrderByDescending(s => s.StartDate)
                .ToListAsync(ct);
        }

        /// <summary>
        /// ✅ Detalhe COMPLETO (para /session/{id}).
        /// </summary>
        public async Task<GameSession?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        {
            return await _context.GameSessions
                .AsNoTracking()
                .Include(s => s.Organizer)
                .Include(s => s.Players)
                    .ThenInclude(p => p.User)
                .Include(s => s.Matches)
                // se precisares depois: .ThenInclude(m => m.MatchPlayers).ThenInclude(mp => mp.User)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
        }

        /// <summary>
        /// ✅ Para updates (com tracking ligado).
        /// </summary>
        public async Task<GameSession?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default)
        {
            return await _context.GameSessions
                .FirstOrDefaultAsync(s => s.Id == id, ct);
        }

        public async Task AddAsync(GameSession session, CancellationToken ct = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            await _context.GameSessions.AddAsync(session, ct);
        }

        public Task UpdateAsync(GameSession session, CancellationToken ct = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            _context.GameSessions.Update(session);
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var session = await _context.GameSessions.FindAsync(new object[] { id }, ct);
            if (session != null)
                _context.GameSessions.Remove(session);
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
            => _context.SaveChangesAsync(ct);
    }
}
