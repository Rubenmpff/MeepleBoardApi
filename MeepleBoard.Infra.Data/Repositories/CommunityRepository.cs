using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class CommunityRepository(MeepleBoardDbContext context) : ICommunityRepository
    {
        public Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken ct = default) =>
            context.UserBlocks.AnyAsync(x => (x.BlockerId == a && x.BlockedId == b) || (x.BlockerId == b && x.BlockedId == a), ct);

        public Task<bool> AnyBlockWithAsync(Guid user, IEnumerable<Guid> others, CancellationToken ct = default)
        {
            var ids = others.Where(o => o != user).Distinct().ToList();
            return ids.Count == 0 ? Task.FromResult(false)
                : context.UserBlocks.AnyAsync(x => (x.BlockerId == user && ids.Contains(x.BlockedId)) || (x.BlockedId == user && ids.Contains(x.BlockerId)), ct);
        }

        public Task<bool> AnyBlockAmongAsync(IEnumerable<Guid> users, CancellationToken ct = default)
        {
            var ids = users.Distinct().ToList();
            return ids.Count < 2 ? Task.FromResult(false) : context.UserBlocks.AnyAsync(x => ids.Contains(x.BlockerId) && ids.Contains(x.BlockedId), ct);
        }

        public async Task<IReadOnlyList<Guid>> BlockedEitherWayAsync(Guid user, CancellationToken ct = default) =>
            await context.UserBlocks.Where(x => x.BlockerId == user || x.BlockedId == user)
                .Select(x => x.BlockerId == user ? x.BlockedId : x.BlockerId).Distinct().ToListAsync(ct);

        public async Task<IReadOnlyList<BlockedPerson>> MyBlocksAsync(Guid blocker, CancellationToken ct = default) =>
            await context.UserBlocks.AsNoTracking().Where(x => x.BlockerId == blocker).OrderByDescending(x => x.CreatedAt)
                .Select(x => new BlockedPerson(x.BlockedId, x.Blocked!.UserName!, x.CreatedAt)).ToListAsync(ct);

        public async Task<bool> BlockAsync(Guid blocker, Guid blocked, CancellationToken ct = default)
        {
            if (await context.UserBlocks.AnyAsync(x => x.BlockerId == blocker && x.BlockedId == blocked, ct)) return false;
            // A friendship or a pending request between the two ends with the block (the row holds both directions).
            await context.Friendships.Where(f => (f.UserAId == blocker && f.UserBId == blocked) || (f.UserAId == blocked && f.UserBId == blocker)).ExecuteDeleteAsync(ct);
            context.UserBlocks.Add(new UserBlock(blocker, blocked));
            await context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> UnblockAsync(Guid blocker, Guid blocked, CancellationToken ct = default) =>
            await context.UserBlocks.Where(x => x.BlockerId == blocker && x.BlockedId == blocked).ExecuteDeleteAsync(ct) > 0;

        public async Task AddUserReportAsync(UserReport report, CancellationToken ct = default) => await context.UserReports.AddAsync(report, ct);
        public async Task AddPhotoReportAsync(PhotoReport report, CancellationToken ct = default) => await context.PhotoReports.AddAsync(report, ct);

        public Task<bool> HasOpenUserReportAsync(Guid reporterId, Guid reportedUserId, CancellationToken ct = default) =>
            context.UserReports.AnyAsync(r => r.ReporterId == reporterId && r.ReportedUserId == reportedUserId && r.ResolvedAt == null, ct);
        public Task<bool> HasOpenPhotoReportAsync(Guid reporterId, Guid journalEntryId, string photoUrl, CancellationToken ct = default) =>
            context.PhotoReports.AnyAsync(r => r.ReporterId == reporterId && r.JournalEntryId == journalEntryId && r.PhotoUrl == photoUrl && r.ResolvedAt == null, ct);
        public Task<bool> PhotoHasOpenReportAsync(Guid journalEntryId, string photoUrl, CancellationToken ct = default) =>
            context.PhotoReports.AnyAsync(r => r.JournalEntryId == journalEntryId && r.PhotoUrl == photoUrl && r.ResolvedAt == null, ct);

        public async Task<IReadOnlyList<ReportedPerson>> ReportedPeopleAsync(CancellationToken ct = default)
        {
            var open = await context.UserReports.AsNoTracking().Where(r => r.ResolvedAt == null)
                .Select(r => new { r.ReportedUserId, Name = r.ReportedUser!.UserName, r.Reason, r.Detail, r.CreatedAt,
                    r.ReportedUser.SuspendedAt, r.ReportedUser.SuspendedUntil, r.ReportedUser.SuspensionReason }).ToListAsync(ct);
            return open.GroupBy(r => r.ReportedUserId).Select(g => new ReportedPerson(g.Key, g.First().Name ?? "", g.GroupBy(r => r.Reason).ToDictionary(x => x.Key, x => x.Count()),
                g.Where(r => r.Detail != null).Select(r => r.Detail!).ToList(), g.Max(r => r.CreatedAt), g.First().SuspendedAt, g.First().SuspendedUntil, g.First().SuspensionReason))
                .OrderByDescending(p => p.LastReportAt).ToList();
        }

        public async Task<IReadOnlyList<ReportedPhoto>> ReportedPhotosAsync(CancellationToken ct = default)
        {
            var open = await context.PhotoReports.AsNoTracking().Where(r => r.ResolvedAt == null)
                .Select(r => new { r.JournalEntryId, r.JournalEntry!.MatchId, Owner = r.JournalEntry.UserId, OwnerName = r.JournalEntry.User!.UserName, r.PhotoUrl, r.Reason, r.Detail, r.CreatedAt }).ToListAsync(ct);
            return open.GroupBy(r => (r.JournalEntryId, r.PhotoUrl)).Select(g => new ReportedPhoto(g.Key.JournalEntryId, g.First().MatchId, g.First().Owner, g.First().OwnerName ?? "", g.Key.PhotoUrl,
                g.GroupBy(r => r.Reason).ToDictionary(x => x.Key, x => x.Count()), g.Where(r => r.Detail != null).Select(r => r.Detail!).ToList(), g.Max(r => r.CreatedAt)))
                .OrderByDescending(p => p.LastReportAt).ToList();
        }

        public async Task<IReadOnlyList<SuspendedPerson>> SuspendedPeopleAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            return await context.Users.AsNoTracking().Where(u => u.DeletedAt == null && u.SuspendedAt != null && (u.SuspendedUntil == null || u.SuspendedUntil > now))
                .OrderByDescending(u => u.SuspendedAt)
                .Select(u => new SuspendedPerson(u.Id, u.UserName!, u.SuspendedAt!.Value, u.SuspendedUntil, u.SuspensionReason)).ToListAsync(ct);
        }

        public async Task<int> ResolveUserReportsAsync(Guid reportedUserId, Guid administratorId, string outcome, CancellationToken ct = default)
        {
            var reports = await context.UserReports.Include(r => r.Reporter).Where(r => r.ReportedUserId == reportedUserId && r.ResolvedAt == null).ToListAsync(ct);
            foreach (var r in reports) r.Resolve(administratorId, outcome);
            await context.SaveChangesAsync(ct);
            return reports.Count;
        }

        public async Task<int> ResolvePhotoReportsAsync(Guid journalEntryId, string photoUrl, Guid administratorId, string outcome, CancellationToken ct = default)
        {
            var reports = await context.PhotoReports.Include(r => r.Reporter).Where(r => r.JournalEntryId == journalEntryId && r.PhotoUrl == photoUrl && r.ResolvedAt == null).ToListAsync(ct);
            foreach (var r in reports) r.Resolve(administratorId, outcome);
            await context.SaveChangesAsync(ct);
            return reports.Count;
        }

        public Task<MatchJournalEntry?> GetJournalEntryAsync(Guid journalEntryId, CancellationToken ct = default) =>
            context.MatchJournalEntries.Include(e => e.Match).ThenInclude(m => m!.MatchPlayers).FirstOrDefaultAsync(e => e.Id == journalEntryId, ct);

        public async Task<bool> SuspendAsync(Guid userId, DateTime? untilUtc, string reason, Guid administratorId, CancellationToken ct = default)
        {
            var strategy = context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(ct);
                var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
                if (user == null) return false;
                user.Suspend(untilUtc, reason, administratorId, DateTime.UtcNow);
                await context.SaveChangesAsync(ct);
                // Every session ends with the suspension; a new sign-in is needed once it is over.
                await context.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            });
        }

        public async Task<bool> LiftSuspensionAsync(Guid userId, Guid administratorId, CancellationToken ct = default)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
            if (user?.SuspendedAt == null) return false;
            user.LiftSuspension(administratorId, DateTime.UtcNow);
            await context.SaveChangesAsync(ct);
            return true;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
    }
}
