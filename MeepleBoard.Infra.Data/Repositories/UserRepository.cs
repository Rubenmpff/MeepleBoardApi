using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly MeepleBoardDbContext _context;

        public UserRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        // 🔹 Verifica se um e-mail já está em uso
        public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;

            return await _context.Users
                .AsNoTracking()
                .AnyAsync(
                    u => u.Email != null &&
                         u.Email.ToLower().Trim() == email.ToLower().Trim(),
                    cancellationToken);
        }

        // 🔹 Verifica se um nome de usuário já está em uso
        public async Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            return await _context.Users
                .AsNoTracking()
                .AnyAsync(
                    u => u.UserName != null &&
                         u.UserName.ToLower().Trim() == username.ToLower().Trim(),
                    cancellationToken);
        }

        // 🔹 Obtém um usuário pelo ID (inclui partidas do usuário)
        public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Users
                .Include(u => u.Matches)
                    .ThenInclude(m => m.Match)
                .AsSplitQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        }

        // 🔹 Obtém um usuário pelo e-mail
        public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;

            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    user => user.Email != null &&
                            user.Email.ToLower().Trim() == email.ToLower().Trim(),
                    cancellationToken);
        }

        // 🔹 Obtém um usuário pelo nome de usuário
        public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    u => u.UserName != null &&
                         u.UserName.ToLower().Trim() == username.ToLower().Trim(),
                    cancellationToken);
        }

        // 🔹 Retorna todos os usuários registrados
        public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Users
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        // 🔹 Retorna os usuários com mais vitórias
        public async Task<IReadOnlyList<User>> GetUsersWithMostWinsAsync(
            int count,
            DateTime? startDate = null,
            CancellationToken cancellationToken = default)
        {
            var query = _context.MatchPlayers
                .Where(mp =>
                    mp.Outcome == "Win" && mp.Match!.IsOfficialMode &&
                    (!startDate.HasValue || mp.Match!.MatchDate >= startDate.Value))
                .GroupBy(mp => mp.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    TotalWins = g.Count()
                })
                .OrderByDescending(u => u.TotalWins)
                .Take(count)
                .Join(
                    _context.Users,
                    stat => stat.UserId,
                    u => u.Id,
                    (stat, u) => new
                    {
                        u,
                        stat.TotalWins
                    })
                .OrderByDescending(u => u.TotalWins)
                .Select(u => u.u)
                .AsNoTracking();

            return await query.ToListAsync(cancellationToken);
        }

        // 🔹 Adiciona um novo usuário (sem SaveChanges)
        public async Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            await _context.Users.AddAsync(user, cancellationToken);
        }

        // 🔹 Atualiza os dados de um usuário
        public async Task<int> UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            var existingUser =
                await _context.Users.FindAsync(
                    new object[] { user.Id },
                    cancellationToken);

            if (existingUser == null) return 0;

            _context.Entry(existingUser)
                .CurrentValues
                .SetValues(user);

            return await _context.SaveChangesAsync(cancellationToken);
        }

        public Task<bool> ContributesDurationStatsAsync(Guid userId, CancellationToken cancellationToken = default)
            => _context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.ContributesDurationStats).FirstOrDefaultAsync(cancellationToken);

        public async Task<AccountDeletionResult> AnonymizeAsync(Guid userId, string deletedUserName, CancellationToken cancellationToken = default)
        {
            // Retries (EnableRetryOnFailure) replay the whole unit, so nothing is ever saved half-way.
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
                    ?? throw new KeyNotFoundException("Utilizador não encontrado.");

                // The person's diary: notes, photos, ratings and publications (their reports go with the publications).
                var entries = await _context.MatchJournalEntries.Where(e => e.UserId == userId).ToListAsync(cancellationToken);
                var photos = entries.SelectMany(e => e.PhotoUrls).Distinct().ToList();
                var games = await _context.MatchJournalEntries.Where(e => e.UserId == userId && e.PersonalRating != null)
                    .Select(e => e.Match!.GameId)
                    // Legacy ratings kept on the match itself count as the creator's.
                    .Union(_context.Matches.Where(m => m.CreatorId == userId && m.PersonalRating != null).Select(m => m.GameId))
                    .Distinct().ToListAsync(cancellationToken);
                _context.MatchJournalEntries.RemoveRange(entries);
                await _context.Matches.Where(m => m.CreatorId == userId && (m.PersonalRating != null || m.Notes != null))
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.PersonalRating, (double?)null).SetProperty(m => m.Notes, (string?)null), cancellationToken);

                // Personal links that only made sense with the person.
                await _context.UserGameLibraries.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await _context.Friendships.Where(f => f.UserAId == userId || f.UserBId == userId || f.InitiatorId == userId).ExecuteDeleteAsync(cancellationToken);
                await _context.GameSessionPlayers.Where(p => p.UserId == userId && p.Status != GameSessionInviteStatus.Accepted).ExecuteDeleteAsync(cancellationToken);

                // Sign-in closed for good.
                await _context.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await _context.EmailResendLogs.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await _context.UserLogins.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await _context.UserTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await _context.UserClaims.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                await _context.UserRoles.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);

                user.Anonymize(deletedUserName);
                await _context.SaveChangesAsync(cancellationToken);
                foreach (var gameId in games) await GameRepository.RecomputeMeepleBoardRating(_context, gameId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new AccountDeletionResult(games, photos);
            });
        }

        // 🔹 Salva as mudanças no banco
        public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }

        // 🔹 Atualiza a última atividade do utilizador (indicador "online" nos Amigos).
        //
        // Usa ExecuteUpdateAsync para não carregar a entidade inteira e só altera
        // LastActiveAt quando o valor está desatualizado há mais de 1 minuto.
        //
        // Quando o request HTTP é abortado, o SqlClient pode devolver o cancelamento
        // como SqlException ("Operation cancelled by user") em vez de
        // OperationCanceledException. Nessa situação convertemos para
        // OperationCanceledException para que o middleware trate corretamente o
        // cancelamento como comportamento normal e não o registe como warning.
        public async Task TouchLastActiveAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var staleThreshold = now.AddMinutes(-1);

            try
            {
                await _context.Users
                    .Where(u =>
                        u.Id == userId &&
                        (u.LastActiveAt == null ||
                         u.LastActiveAt < staleThreshold))
                    .ExecuteUpdateAsync(
                        setters =>
                            setters.SetProperty(
                                u => u.LastActiveAt,
                                now),
                        cancellationToken);
            }
            catch (SqlException ex)
                when (
                    cancellationToken.IsCancellationRequested &&
                    ex.Message.Contains(
                        "Operation cancelled by user",
                        StringComparison.OrdinalIgnoreCase))
            {
                throw new OperationCanceledException(
                    "A atualização de LastActiveAt foi cancelada porque o pedido HTTP deixou de ser atual.",
                    ex,
                    cancellationToken);
            }
        }
    }
}
