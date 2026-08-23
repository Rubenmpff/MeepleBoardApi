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

        #region  Consultas de Existência

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

        #endregion Consultas de Existência

        #region  Leitura de Dados

        public async Task<Game?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Games
                .Include(g => g.Expansions)
                .AsSplitQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        }

        public async Task<IReadOnlyList<Game>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return Array.Empty<Game>();

            return await _context.Games
                .AsNoTracking()
                .Where(g => idList.Contains(g.Id))
                .ToListAsync(cancellationToken);
        }

        public async Task<List<Game>> SearchByNameAsync(
            string query,
            int offset,
            int limit,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<Game>();

            var searchTerm = query.Trim();
            var safeOffset = Math.Max(0, offset);
            var safeLimit = Math.Clamp(limit, 1, 100);

            /*
             * IMPORTANTE:
             * Esta query serve para obter CANDIDATOS locais.
             * O ranking final é feito no GameService, juntamente com os resultados do BGG.
             *
             * Evitamos OrderBy(Name) puro porque isso fazia resultados pouco relevantes
             * aparecerem antes de jogos muito mais conhecidos.
             *
             * A ordem local agora favorece:
             * 1) nome exato
             * 2) nome começa pelo termo
             * 3) popularidade (UsersRatedCount)
             * 4) nome, apenas como desempate previsível
             */
            return await _context.Games
                .AsNoTracking()
                .Where(g =>
                    g.Name.StartsWith(searchTerm) ||
                    g.Name.Contains(searchTerm))
                .OrderByDescending(g => g.Name == searchTerm)
                .ThenByDescending(g => g.Name.StartsWith(searchTerm))
                .ThenByDescending(g => g.UsersRatedCount ?? 0)
                .ThenBy(g => g.Name)
                .Skip(safeOffset)
                .Take(safeLimit)
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

        public async Task<List<Game>> SearchBaseGamesByNameAsync(
            string query,
            int offset = 0,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<Game>();

            var searchTerm = query.Trim();
            var safeOffset = Math.Max(0, offset);
            var safeLimit = Math.Clamp(limit, 1, 100);

            return await _context.Games
                .AsNoTracking()
                .Where(g =>
                    g.BaseGameId == null &&
                    g.BaseGameBggId == null &&
                    (g.Name.StartsWith(searchTerm) ||
                     g.Name.Contains(searchTerm)))
                .OrderByDescending(g => g.Name == searchTerm)
                .ThenByDescending(g => g.Name.StartsWith(searchTerm))
                .ThenByDescending(g => g.UsersRatedCount ?? 0)
                .ThenBy(g => g.Name)
                .Skip(safeOffset)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<Game>> SearchExpansionsByNameAsync(
            string query,
            int offset = 0,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<Game>();

            var searchTerm = query.Trim();
            var safeOffset = Math.Max(0, offset);
            var safeLimit = Math.Clamp(limit, 1, 100);

            return await _context.Games
                .AsNoTracking()
                .Where(g =>
                    (g.BaseGameId != null ||
                     g.BaseGameBggId != null) &&
                    (g.Name.StartsWith(searchTerm) ||
                     g.Name.Contains(searchTerm)))
                .OrderByDescending(g => g.Name == searchTerm)
                .ThenByDescending(g => g.Name.StartsWith(searchTerm))
                .ThenByDescending(g => g.UsersRatedCount ?? 0)
                .ThenBy(g => g.Name)
                .Skip(safeOffset)
                .Take(safeLimit)
                .ToListAsync(cancellationToken);
        }

        #endregion Leitura de Dados

        #region Escrita de Dados

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

        #endregion Escrita de Dados

        #region Funções Especiais para Jobs (Ranking, Atualizações, etc.)

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

        // 🏆 Média de todas as avaliações pessoais (diário) das partidas deste jogo.
        // Devolve null se ainda não houver nenhuma avaliação.
        public async Task<double?> GetAveragePersonalRatingAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            // Fonte principal: avaliações por jogador (MatchJournalEntry) — o fluxo normal.
            var journalRatings = _context.MatchJournalEntries
                .AsNoTracking()
                .Where(e => e.PersonalRating.HasValue && e.Match!.GameId == gameId)
                .Select(e => (double)e.PersonalRating!.Value);

            // Fonte secundária (legado): partidas registadas antes de existir o espelho
            // automático para o diário, onde a avaliação ficou só em Match.PersonalRating
            // sem nenhum MatchJournalEntry associado. Assim que uma partida tiver pelo
            // menos 1 entrada no diário, deixa de contar aqui (evita duplicar a mesma nota).
            var legacyMatchRatings = _context.Matches
                .AsNoTracking()
                .Where(m => m.GameId == gameId && m.PersonalRating.HasValue && !m.JournalEntries.Any())
                .Select(m => m.PersonalRating!.Value);

            var ratings = await journalRatings.Concat(legacyMatchRatings).ToListAsync(cancellationToken);

            return ratings.Count > 0 ? ratings.Average() : (double?)null;
        }

        public async Task<(IReadOnlyList<Game> Items, int TotalCount)> GetRankedByMeepleBoardScoreAsync(
            int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            var query = _context.Games
                .AsNoTracking()
                .Where(g => g.IsApproved && g.MeepleBoardScore != null);

            var total = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(g => g.MeepleBoardScore)
                .ThenBy(g => g.Name)
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, total);
        }

        public async Task<(IReadOnlyList<Game> Items, int TotalCount)> GetRankedByBggRatingAsync(
            int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            var query = _context.Games
                .AsNoTracking()
                .Where(g => g.IsApproved && g.AverageRating != null);

            var total = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(g => g.AverageRating)
                .ThenBy(g => g.Name)
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, total);
        }

        public async Task<(IReadOnlyList<(Game Game, double Rating)> Items, int TotalCount)> GetPersonalRankingsAsync(
            Guid userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            // Fonte principal: as avaliações desta pessoa no diário
            var journalRatings = await _context.MatchJournalEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId && e.PersonalRating.HasValue)
                .Select(e => new { e.Match!.GameId, Rating = (double)e.PersonalRating!.Value })
                .ToListAsync(cancellationToken);

            // Legado: partidas desta pessoa com PersonalRating no Match mas ainda sem
            // nenhuma entrada de diário (mesma lógica do resto do ranking).
            var legacyRatings = await _context.Matches
                .AsNoTracking()
                .Where(m =>
                    m.PersonalRating != null &&
                    !m.JournalEntries.Any() &&
                    m.MatchPlayers.Any(mp => mp.UserId == userId))
                .Select(m => new { m.GameId, Rating = m.PersonalRating!.Value })
                .ToListAsync(cancellationToken);

            var grouped = journalRatings
                .Concat(legacyRatings)
                .GroupBy(x => x.GameId)
                .Select(g => new { GameId = g.Key, Avg = g.Average(x => x.Rating) })
                .OrderByDescending(x => x.Avg)
                .ToList();

            var total = grouped.Count;
            var page = grouped.Skip(pageIndex * pageSize).Take(pageSize).ToList();

            var gameIds = page.Select(p => p.GameId).ToList();
            var games = await _context.Games
                .AsNoTracking()
                .Where(g => gameIds.Contains(g.Id))
                .ToListAsync(cancellationToken);
            var gameMap = games.ToDictionary(g => g.Id);

            var items = page
                .Where(p => gameMap.ContainsKey(p.GameId))
                .Select(p => (gameMap[p.GameId], p.Avg))
                .ToList();

            return (items, total);
        }

        #endregion Funções Especiais para Jobs (Ranking, Atualizações, etc.)
    }
}