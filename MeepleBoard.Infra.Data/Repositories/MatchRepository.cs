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

        // ── GetById — inclui tudo para detalhe/avaliação ──────────────────────
        public async ValueTask<Match?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Include(m => m.Game)
                .Include(m => m.MatchPlayers).ThenInclude(mp => mp.User)
                .Include(m => m.Winner)
                .Include(m => m.JournalEntries)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        }

        // ── Projecção: última partida do utilizador ───────────────────────────
        // Devolve tuplo simples — repositório não conhece DTOs (arquitectura limpa)
        public async Task<(string Name, string Date, string Winner, string? ImageUrl)?> GetLastMatchProjectionForUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            var result = await _context.Matches
                .Where(m => m.MatchPlayers.Any(mp => mp.UserId == userId))
                .OrderByDescending(m => m.MatchDate)
                .Select(m => new
                {
                    Name = m.Game != null ? m.Game.Name : "Desconhecido",
                    Date = m.MatchDate.ToString("yyyy-MM-dd"),
                    Winner = m.MatchPlayers
                                  .Where(mp => mp.IsWinner)
                                  .Select(mp => mp.User != null ? mp.User.UserName : "Desconhecido")
                                  .FirstOrDefault() ?? "Desconhecido",
                    ImageUrl = m.Game != null ? m.Game.ImageUrl : null,
                })
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null) return null;
            return (result.Name, result.Date, result.Winner, result.ImageUrl);
        }

        // ── Projecção: partidas pendentes de avaliação ────────────────────────
        public async Task<IReadOnlyList<Match>> GetPendingJournalMatchesForUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m =>
                    m.JournalStatus == MatchJournalStatus.Open &&
                    m.MatchPlayers.Any(mp => mp.UserId == userId) &&
                    !m.JournalEntries.Any(e => e.UserId == userId && e.PersonalRating != null))
                .Include(m => m.Game)
                .Include(m => m.MatchPlayers).ThenInclude(mp => mp.User)
                .Include(m => m.JournalEntries)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .OrderByDescending(m => m.MatchDate)
                .ToListAsync(cancellationToken);
        }

        // ── Projecção: historial por jogo/utilizador ──────────────────────────
        public async Task<IReadOnlyList<Match>> GetMatchHistoryByGameForUserAsync(
            Guid gameId, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m =>
                    m.GameId == gameId &&
                    m.MatchPlayers.Any(mp => mp.UserId == userId))
                .Include(m => m.Game)
                .Include(m => m.MatchPlayers).ThenInclude(mp => mp.User)
                .Include(m => m.Winner)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .OrderByDescending(m => m.MatchDate)
                .ToListAsync(cancellationToken);
        }

        // ── Projecção: ratings por jogo/utilizador ───────────────────────────
        // ✅ Retorna lista de doubles — double? não funciona como tipo genérico EF Core
        //
        // Une duas fontes (mesma lógica do GameRepository.GetAveragePersonalRatingAsync):
        //   1. MatchJournalEntry — a fonte normal, a avaliação do utilizador no diário
        //   2. Match.PersonalRating (legado) — só para partidas sem NENHUMA entrada
        //      de diário ainda, para não perder avaliações antigas órfãs
        public async Task<IReadOnlyList<double>> GetRatingsForGameByUserAsync(
            Guid gameId, Guid userId, CancellationToken cancellationToken = default)
        {
            var journalRatings = _context.MatchJournalEntries
                .Where(e =>
                    e.UserId == userId &&
                    e.PersonalRating.HasValue &&
                    e.Match!.GameId == gameId &&
                    e.Match!.IsOfficialMode)
                .Select(e => (double)e.PersonalRating!.Value);

            var legacyMatchRatings = _context.Matches
                .Where(m =>
                    m.GameId == gameId &&
                    m.IsOfficialMode &&
                    m.PersonalRating != null &&
                    !m.JournalEntries.Any() &&
                    m.MatchPlayers.Any(mp => mp.UserId == userId))
                .Select(m => m.PersonalRating!.Value);

            return await journalRatings.Concat(legacyMatchRatings).ToListAsync(cancellationToken);
        }

        // ── "Já joguei" automático ───────────────────────────────────────────

        public async Task<IReadOnlyDictionary<Guid, (int Count, DateTime LastPlayed)>> GetPlayCountsByGameForUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            var stats = await _context.MatchPlayers
                .AsNoTracking()
                .Where(mp => mp.UserId == userId)
                .GroupBy(mp => mp.Match!.GameId)
                .Select(g => new
                {
                    GameId = g.Key,
                    Count = g.Count(),
                    LastPlayed = g.Max(mp => mp.Match!.MatchDate),
                })
                .ToListAsync(cancellationToken);

            return stats.ToDictionary(x => x.GameId, x => (x.Count, x.LastPlayed));
        }

        // ── Queries standard ──────────────────────────────────────────────────

        public async Task<IReadOnlyList<Match>> GetAllAsync(
            int pageIndex = 0, int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            if (pageIndex < 0) pageIndex = 0;
            if (pageSize <= 0) pageSize = 10;

            return await _context.Matches
                .Include(m => m.Game)
                .Include(m => m.MatchPlayers).ThenInclude(mp => mp.User)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .OrderByDescending(m => m.MatchDate)
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetByUserIdAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m => m.MatchPlayers.Any(mp => mp.UserId == userId))
                .Include(m => m.Game)
                .Include(m => m.MatchPlayers).ThenInclude(mp => mp.User)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .OrderByDescending(m => m.MatchDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetByGameIdAsync(
            Guid gameId, CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m => m.GameId == gameId)
                .Include(m => m.Game)
                .Include(m => m.MatchPlayers).ThenInclude(mp => mp.User)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .OrderByDescending(m => m.MatchDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetMatchesByPeriodAsync(
            DateTime startDate, DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m => m.MatchDate >= startDate && m.MatchDate <= endDate)
                .Include(m => m.Game)
                .AsNoTrackingWithIdentityResolution()
                .OrderByDescending(m => m.MatchDate)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetOpenJournalMatchesAsync(
            CancellationToken cancellationToken = default)
        {
            return await _context.Matches
                .Where(m => m.JournalStatus == MatchJournalStatus.Open)
                .Include(m => m.Game)
                .Include(m => m.MatchPlayers)
                .Include(m => m.JournalEntries)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Match>> GetRecentMatchesSinceAsync(
            int count = 10, DateTime? startDate = null,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Matches.AsQueryable();
            if (startDate.HasValue)
                query = query.Where(m => m.MatchDate >= startDate.Value);

            return await query
                .Include(m => m.Game)
                .OrderByDescending(m => m.MatchDate)
                .Take(count)
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync(cancellationToken);
        }

        public Task<int> GetTotalMatchesByGameAsync(
            Guid gameId, CancellationToken cancellationToken = default)
            => _context.Matches.AsNoTracking().CountAsync(m => m.GameId == gameId, cancellationToken);

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
            => _context.Matches.AnyAsync(m => m.Id == id, cancellationToken);

        // ── Escrita ───────────────────────────────────────────────────────────

        public async Task AddAsync(Match match, CancellationToken cancellationToken = default)
            => await _context.Matches.AddAsync(match, cancellationToken);

        public async Task UpdateAsync(Match match, CancellationToken cancellationToken = default)
        {
            _context.Matches.Update(match);
            await SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(
            Guid id, Guid? gameId = null,
            CancellationToken cancellationToken = default)
        {
            if (gameId.HasValue)
            {
                var matches = await _context.Matches
                    .Where(m => m.GameId == gameId.Value)
                    .ToListAsync(cancellationToken);
                if (matches.Any())
                    _context.Matches.RemoveRange(matches);
            }
            else
            {
                var match = await _context.Matches.FindAsync(new object[] { id }, cancellationToken);
                if (match == null) throw new KeyNotFoundException("A partida não foi encontrada.");
                _context.Matches.Remove(match);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _context.SaveChangesAsync(cancellationToken);
    }
}