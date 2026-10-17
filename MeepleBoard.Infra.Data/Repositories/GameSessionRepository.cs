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
                .Include(s => s.Organizer).Include(s => s.Players) // membership required for mine filtering
                .Include(s => s.PlannedGame)
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
                .Include(s => s.PlannedGame)
                .Include(s => s.Players)
                    .ThenInclude(p => p.User)
                .Include(s => s.Matches).ThenInclude(m => m.Game)
                .Include(s => s.Matches).ThenInclude(m => m.Winner)
                .Include(s => s.Matches).ThenInclude(m => m.MatchPlayers).ThenInclude(mp => mp.User)
                .AsSplitQuery()
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

        public async Task<GameSession?> GetByIdWithPlayersForUpdateAsync(Guid id, CancellationToken ct = default)
        {
            return await _context.GameSessions
                .Include(s => s.Players)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
        }

        public async Task<IReadOnlyList<GameSession>> GetOpenForUpdateAsync(CancellationToken ct = default)
        {
            return await _context.GameSessions
                .Include(s => s.Players)
                .Where(s => s.EndDate == null && !s.IsCancelled)
                .ToListAsync(ct);
        }

        public async Task<bool> SaveCloseAsync(GameSession session, CancellationToken ct = default)
        {
            if (session.EndDate is not { } end) throw new InvalidOperationException("A sessão não foi terminada.");
            var automatic = session.ClosedAutomatically;
            // One conditional UPDATE: the first close wins; a later one changes nothing.
            var rows = await _context.GameSessions
                .Where(s => s.Id == session.Id && s.EndDate == null && !s.IsCancelled)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndDate, end).SetProperty(s => s.ClosedAutomatically, automatic), ct);
            var entry = _context.Entry(session);
            if (rows == 1) { entry.State = EntityState.Unchanged; return true; }
            await entry.ReloadAsync(ct);
            return false;
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
