using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    /// <summary>
    /// Implementação do repositório de jogadores de sessão.
    /// Responsável por associar e remover jogadores de sessões.
    /// </summary>
    public class GameSessionPlayerRepository : IGameSessionPlayerRepository
    {
        private readonly MeepleBoardDbContext _context;

        public GameSessionPlayerRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<GameSessionPlayer>> GetBySessionIdAsync(Guid sessionId)
        {
            return await _context.GameSessionPlayers
                .AsNoTracking()
                .Include(p => p.User)
                .Where(p => p.SessionId == sessionId)
                .OrderBy(p => p.JoinedAt)
                .ToListAsync();
        }

        /// <inheritdoc />
        public async Task<GameSessionPlayer?> GetByIdAsync(Guid id)
        {
            return await _context.GameSessionPlayers
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        /// <inheritdoc />
        public async Task<GameSessionPlayer?> GetBySessionAndUserAsync(Guid sessionId, Guid userId)
        {
            return await _context.GameSessionPlayers
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.UserId == userId);
        }

        /// <inheritdoc />
        public async Task AddAsync(GameSessionPlayer player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            await _context.GameSessionPlayers.AddAsync(player);
        }

        /// <inheritdoc />
        public async Task RemoveAsync(Guid id)
        {
            var player = await _context.GameSessionPlayers.FindAsync(id);
            if (player != null)
            {
                _context.GameSessionPlayers.Remove(player);
            }
        }

        /// <inheritdoc />
        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
