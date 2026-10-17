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

        public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken = default)
            => _context.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.DeletedAt == null, cancellationToken);

        public Task<bool> ContributesDurationStatsAsync(Guid userId, CancellationToken cancellationToken = default)
            => _context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.ContributesDurationStats).FirstOrDefaultAsync(cancellationToken);

        public async Task<AccountDeletionResult> AnonymizeAsync(Guid userId, string deletedUserName, IReadOnlyDictionary<Guid, Guid>? campaignTransfers = null, CancellationToken cancellationToken = default)
        {
            campaignTransfers ??= new Dictionary<Guid, Guid>();
            // Retries (EnableRetryOnFailure) replay the whole unit, so nothing is ever saved half-way.
            var strategy = _context.Database.CreateExecutionStrategy();
            // Should a simultaneous request still slip past the lock, its changes roll back and it reports the account gone.
            try { return await AnonymizeOnceAsync(); }
            catch (DbUpdateConcurrencyException) { _context.ChangeTracker.Clear(); throw new KeyNotFoundException("Utilizador não encontrado."); }

            async Task<AccountDeletionResult> AnonymizeOnceAsync() => await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                // The account row is locked for the whole unit: a simultaneous request waits, then finds it deleted.
                var user = await _context.Users.FromSqlInterpolated($"SELECT * FROM AspNetUsers WITH (UPDLOCK, ROWLOCK) WHERE Id = {userId}")
                    .SingleOrDefaultAsync(cancellationToken) ?? throw new KeyNotFoundException("Utilizador não encontrado.");
                if (user.DeletedAt.HasValue) throw new KeyNotFoundException("Utilizador não encontrado.");
                var now = DateTime.UtcNow;

                // New managers chosen by the person: only for campaigns they created, among other active members.
                var created = await _context.Campaigns.Where(c => c.CreatorId == userId).Select(c => c.Id).ToListAsync(cancellationToken);
                var activeMembers = await _context.CampaignMembers
                    .Where(m => created.Contains(m.CampaignId) && m.UserId != userId && m.Status == CampaignMemberStatus.Accepted && m.User!.DeletedAt == null)
                    .Select(m => new { m.CampaignId, m.UserId }).ToListAsync(cancellationToken);
                foreach (var (campaignId, managerId) in campaignTransfers)
                    if (!activeMembers.Any(m => m.CampaignId == campaignId && m.UserId == managerId))
                        throw new ArgumentException("O novo gestor tem de ser um membro ativo de uma campanha que criaste.");

                // The person's diary: notes, photos, ratings and publications (their reports go with the publications).
                var entries = await _context.MatchJournalEntries.Where(e => e.UserId == userId).ToListAsync(cancellationToken);
                var photos = entries.SelectMany(e => e.PhotoUrls).ToList();
                var games = await _context.MatchJournalEntries.Where(e => e.UserId == userId && e.PersonalRating != null)
                    .Select(e => e.Match!.GameId)
                    // Legacy ratings kept on the match itself count as the creator's.
                    .Union(_context.Matches.Where(m => m.CreatorId == userId && m.PersonalRating != null).Select(m => m.GameId))
                    .Distinct().ToListAsync(cancellationToken);
                _context.MatchJournalEntries.RemoveRange(entries);
                await _context.SaveChangesAsync(cancellationToken);

                // Matches nobody else active took part in belonged only to the person: they go, with their diaries and links.
                var onlyTheirs = await _context.MatchPlayers.Where(p => p.UserId == userId).Select(p => p.MatchId)
                    .Where(id => !_context.MatchPlayers.Any(o => o.MatchId == id && o.UserId != userId && o.User!.DeletedAt == null))
                    .Distinct().ToListAsync(cancellationToken);
                if (onlyTheirs.Count > 0)
                {
                    games = games.Union(await _context.Matches.Where(m => onlyTheirs.Contains(m.Id)).Select(m => m.GameId).ToListAsync(cancellationToken)).Distinct().ToList();
                    photos.AddRange((await _context.MatchJournalEntries.Where(e => onlyTheirs.Contains(e.MatchId)).ToListAsync(cancellationToken)).SelectMany(e => e.PhotoUrls));
                    await _context.CampaignMatches.Where(c => onlyTheirs.Contains(c.MatchId)).ExecuteDeleteAsync(cancellationToken);
                    // Diaries, publications, their reports and players go with the match (cascade).
                    await _context.Matches.Where(m => onlyTheirs.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
                }

                // Shared matches the person created: their structured data stays for the others; their free text goes.
                await _context.Matches.Where(m => m.CreatorId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.PersonalRating, (double?)null).SetProperty(m => m.Notes, (string?)null)
                        .SetProperty(m => m.Location, (string?)null).SetProperty(m => m.ScoreSummary, (string?)null).SetProperty(m => m.Tags, (string?)null), cancellationToken);
                await _context.CampaignMatches.Where(c => c.Match!.CreatorId == userId && c.SessionTitle != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.SessionTitle, (string?)null), cancellationToken);

                // Sessions the person organizes: scheduled ones are cancelled, a running one ends now (the same conditional
                // close as «Terminar sessão», so the automatic close never mixes with it); all get a neutral title, no place.
                var organized = await _context.GameSessions.AsNoTracking().Include(s => s.Players).ThenInclude(p => p.User)
                    .Where(s => s.OrganizerId == userId).ToListAsync(cancellationToken);
                var notify = new List<string>();
                foreach (var session in organized)
                {
                    if (session.EndDate == null && !session.IsCancelled)
                    {
                        var due = session.AcceptedGuestCount >= 1 && now >= session.AutoCloseAt;
                        if (session.Status == "Active" || due)
                            await _context.GameSessions.Where(s => s.Id == session.Id && s.EndDate == null && !s.IsCancelled)
                                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndDate, due ? session.AutoCloseAt : now).SetProperty(x => x.ClosedAutomatically, due), cancellationToken);
                        else if (await _context.GameSessions.Where(s => s.Id == session.Id && s.EndDate == null && !s.IsCancelled)
                                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsCancelled, true).SetProperty(x => x.EndDate, now), cancellationToken) == 1)
                            notify.AddRange(session.Players.Where(p => !p.IsOrganizer && p.UserId != userId && p.Status != GameSessionInviteStatus.Declined
                                && p.User != null && p.User.DeletedAt == null && !string.IsNullOrWhiteSpace(p.User.ExpoPushToken)).Select(p => p.User!.ExpoPushToken!));
                    }
                    var title = User.DeletedSessionName(session.ScheduledStartDate);
                    await _context.GameSessions.Where(s => s.Id == session.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, title).SetProperty(x => x.Location, (string?)null), cancellationToken);
                }

                // Campaigns the person created: with no other active member they go (their matches follow the rules above);
                // otherwise they stay with the members — under the chosen new manager or with management read-only — and a
                // neutral title. Notes stay: any member may have written them.
                var withMembers = activeMembers.Select(m => m.CampaignId).ToHashSet();
                var alone = created.Where(id => !withMembers.Contains(id)).ToList();
                if (alone.Count > 0) await _context.Campaigns.Where(c => alone.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
                foreach (var (campaignId, managerId) in campaignTransfers)
                {
                    await _context.Campaigns.Where(c => c.Id == campaignId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatorId, managerId), cancellationToken);
                    await _context.CampaignMembers.Where(m => m.CampaignId == campaignId && (m.UserId == userId || m.UserId == managerId))
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsCreator, m => m.UserId == managerId), cancellationToken);
                }
                var kept = created.Where(withMembers.Contains).ToList();
                if (kept.Count > 0) await _context.Campaigns.Where(c => kept.Contains(c.Id)).ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, User.DeletedCampaignName), cancellationToken);

                // Public resource links stay in the catalogue, no longer tied to the person.
                await _context.GameResources.Where(r => r.SuggestedById == userId).ExecuteUpdateAsync(s => s.SetProperty(r => r.SuggestedById, (Guid?)null), cancellationToken);
                // Reports the person made: kept for moderation (administrators only); free text goes once resolved.
                await _context.ExperienceReports.Where(r => r.ReporterId == userId && r.ResolvedAt != null && r.Detail != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Detail, (string?)null), cancellationToken);
                await _context.GameResourceReports.Where(r => r.UserId == userId && r.ResolvedAt != null && r.Comment != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Comment, (string?)null), cancellationToken);

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

                // A profile picture of ours goes from storage too (one from a sign-in provider is not ours to delete).
                if (user.ProfilePictureUrl is { } picture && picture.Contains("res.cloudinary.com/", StringComparison.OrdinalIgnoreCase)) photos.Add(picture);
                user.Anonymize(deletedUserName);
                await _context.SaveChangesAsync(cancellationToken);
                foreach (var gameId in games) await GameRepository.RecomputeMeepleBoardRating(_context, gameId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new AccountDeletionResult(games, photos.Distinct().ToList(), notify.Distinct().ToList());
            });
        }

        public async Task<AccountDeletionSummary?> GetDeletionSummaryAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (!await IsActiveAsync(userId, cancellationToken)) return null;
            var organized = await _context.GameSessions.AsNoTracking().Include(s => s.Players)
                .Where(s => s.OrganizerId == userId && s.EndDate == null && !s.IsCancelled).ToListAsync(cancellationToken);
            var campaigns = await _context.Campaigns.AsNoTracking().Where(c => c.CreatorId == userId)
                .Select(c => new
                {
                    c.Id, c.Name, GameName = c.Game!.Name, HasNotes = c.Notes != null && c.Notes != "",
                    Members = c.Members.Where(m => m.UserId != userId && m.Status == CampaignMemberStatus.Accepted && m.User!.DeletedAt == null)
                        .OrderBy(m => m.RespondedAt).Select(m => new AccountDeletionMember(m.UserId, m.User!.UserName!)).ToList()
                }).ToListAsync(cancellationToken);
            return new AccountDeletionSummary(
                organized.Count(s => s.Status == "Upcoming"),
                organized.Count(s => s.Status is "Active" or "Closed"),
                campaigns.Where(c => c.Members.Count > 0).Select(c => new AccountDeletionCampaign(c.Id, c.Name, c.GameName, c.HasNotes, c.Members)).ToList(),
                campaigns.Count(c => c.Members.Count == 0));
        }

        public async Task<int> ClearDeletedReporterTextAsync(DateTime deletedBefore, IReadOnlyCollection<Guid>? reporters = null, CancellationToken cancellationToken = default)
        {
            var experience = _context.ExperienceReports.Where(r => r.Detail != null && r.Reporter!.DeletedAt != null && r.Reporter.DeletedAt <= deletedBefore);
            var resource = _context.GameResourceReports.Where(r => r.Comment != null && r.User.DeletedAt != null && r.User.DeletedAt <= deletedBefore);
            if (reporters != null) { experience = experience.Where(r => reporters.Contains(r.ReporterId)); resource = resource.Where(r => reporters.Contains(r.UserId)); }
            return await experience.ExecuteUpdateAsync(s => s.SetProperty(r => r.Detail, (string?)null), cancellationToken)
                + await resource.ExecuteUpdateAsync(s => s.SetProperty(r => r.Comment, (string?)null), cancellationToken);
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
