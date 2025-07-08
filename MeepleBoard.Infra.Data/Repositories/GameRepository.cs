using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class GameRepository : IGameRepository
    {
        private readonly MeepleBoardDbContext _context;

        public GameRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        #region ✅ Consultas de Existência

        public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .AsNoTracking()
                .AnyAsync(g => g.Name.ToLower() == name.ToLower(), cancellationToken);
        }

        public async Task<bool> ExistsByBggIdAsync(int bggId, CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .AsNoTracking()
                .AnyAsync(g => g.BGGId == bggId, cancellationToken);
        }

        #endregion ✅ Consultas de Existência

        #region 🔍 Leitura de Dados

        public async Task<Game?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .Include(g => g.Expansions)
                .AsSplitQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        }

        public async Task<List<Game>> SearchByNameAsync(string query, int offset, int limit, CancellationToken cancellationToken)
{
    return await _context.Games
        .AsNoTracking()
        .Where(g => g.Name.ToLower().Contains(query.ToLower()))
        .OrderBy(g => g.Name)
        .Skip(offset)
        .Take(limit)
        .ToListAsync(cancellationToken);
}


        public async Task<IReadOnlyList<Game>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            IQueryable<Game> query = _context.Games
    .Include(g => g.Expansions)
    .AsSplitQuery()
    .AsNoTracking()
    .OrderBy(g => g.Name);

            if (pageIndex >= 0 && pageSize > 0)
                query = query.Skip(pageIndex * pageSize).Take(pageSize);

            return await query.ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Game>> GetPendingApprovalAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .AsNoTracking()
                .Where(g => !g.IsApproved)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Game>> GetApprovedAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .AsNoTracking()
                .Where(g => g.IsApproved)
                .ToListAsync(cancellationToken);
        }

        public async Task<Game?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Name.ToLower() == name.ToLower(), cancellationToken);
        }

        public async Task<Game?> GetGameByBggIdAsync(int bggId, CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.BGGId == bggId, cancellationToken);
        }

        public async Task<List<Game>> GetExpansionsForBaseGameAsync(Guid baseGameId, CancellationToken cancellationToken)
        {
            return await _context.Games
                .Where(g => g.BaseGameId == baseGameId)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<Game>> GetExpansionsWithBaseGameBggIdAsync(int baseGameBggId, CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .Where(g =>
                    g.BaseGameId == null &&                // Não associadas localmente
                    g.BaseGameBggId == baseGameBggId)     // Mas têm BGG ID do jogo base
                .ToListAsync(cancellationToken);
        }

        public async Task<List<Game>> SearchBaseGamesByNameAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .AsNoTracking()
                .Where(g =>
                    g.BaseGameId == null &&
                    g.BaseGameBggId == null &&
                    g.Name.ToLower().Contains(query.ToLower()))
                .OrderBy(g => g.Name)
                .Skip(offset)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<Game>> SearchExpansionsByNameAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default)
        {
            return await _context.Games
    .AsNoTracking()
    .Where(g =>
        (g.BaseGameId != null || g.BaseGameBggId != null) &&
        g.Name.ToLower().Contains(query.ToLower()))
    .OrderBy(g => g.Name)
    .Skip(offset)
    .Take(limit)
    .ToListAsync(cancellationToken);

        }



        #endregion 🔍 Leitura de Dados

        #region ✍️ Escrita de Dados

        public async Task AddAsync(Game game, CancellationToken cancellationToken = default)
        {
            if (game.BaseGame != null)
            {
                _context.Entry(game.BaseGame).State = EntityState.Unchanged;
            }

            await _context.Games.AddAsync(game, cancellationToken);
        }

        public async Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .AsNoTracking()
                .AnyAsync(g => g.Id == id, cancellationToken);
        }

        public async Task UpdateAsync(Game game, CancellationToken cancellationToken = default)
        {
            _context.Games.Update(game);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(Game game, CancellationToken cancellationToken = default)
        {
            _context.Games.Remove(game);
            await Task.CompletedTask; // Espera não necessária aqui, mas mantida por padrão de interface
        }

        public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }

        #endregion ✍️ Escrita de Dados

        #region 🔥 Funções Especiais para Jobs (Ranking, Atualizações, etc.)

        public async Task<IReadOnlyList<Game>> GetRecentlyPlayedAsync(int limit, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .OrderByDescending(m => m.MatchDate)
                .Where(m => m.Game != null && m.Game.BGGId.HasValue)
                .Select(m => m.Game!)
                .Distinct()
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Game>> GetMostSearchedAsync(int limit, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m => m.Game != null && m.Game.BGGId.HasValue)
                .GroupBy(m => m.GameId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.First().Game!)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        #endregion 🔥 Funções Especiais para Jobs (Ranking, Atualizações, etc.)
    }
}