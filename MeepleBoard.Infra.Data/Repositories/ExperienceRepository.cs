using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class ExperienceRepository(MeepleBoardDbContext context) : IExperienceRepository
    {
        public Task<MatchExperiencePublication?> GetByEntryForUpdateAsync(Guid journalEntryId, CancellationToken ct = default)
            => context.MatchExperiencePublications.FirstOrDefaultAsync(p => p.JournalEntryId == journalEntryId, ct);

        public Task<MatchExperiencePublication?> GetWithReportsForUpdateAsync(Guid publicationId, CancellationToken ct = default)
            => context.MatchExperiencePublications.Include(p => p.JournalEntry).Include(p => p.Reports)
                .FirstOrDefaultAsync(p => p.Id == publicationId, ct);

        public Task<int> CountPublishedSinceAsync(Guid userId, DateTime since, CancellationToken ct = default)
            => context.MatchExperiencePublications.CountAsync(p => p.JournalEntry!.UserId == userId
                && (p.RatingPublishedAt >= since || p.CommentPublishedAt >= since), ct);

        public async Task<(IReadOnlyList<ExperienceListProjection> Items, int Total)> ListVisibleForGameAsync(
            Guid gameId, Guid viewerId, int offset, int limit, CancellationToken ct = default)
        {
            // Only what is published and visible, of a rated entry whose author is still a player of the match.
            var query = context.MatchExperiencePublications.AsNoTracking()
                .Where(p => p.HiddenAt == null && (p.RatingPublishedAt != null || p.CommentPublishedAt != null)
                    && p.JournalEntry!.Match!.GameId == gameId && p.JournalEntry.PersonalRating != null
                    && p.JournalEntry.Match.MatchPlayers.Any(mp => mp.UserId == p.JournalEntry.UserId)
                    && !p.Reports.Any(r => r.ReporterId == viewerId && r.ResolvedAt == null));
            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt).ThenBy(p => p.Id)
                .Skip(offset).Take(limit)
                .Select(p => new ExperienceListProjection(
                    p.Id, p.JournalEntry!.User!.UserName, p.JournalEntry.UserId == viewerId,
                    p.RatingPublishedAt != null ? p.JournalEntry.PersonalRating : null,
                    p.CommentPublishedAt != null ? p.Comment : null, p.CommentPublishedAt != null && p.CommentEditedAt != null,
                    p.JournalEntry.MatchId, p.JournalEntry.Match!.GameMode, p.JournalEntry.Match.IsSoloGame,
                    p.JournalEntry.Match.MatchPlayers.Count, p.JournalEntry.Match.MatchDate))
                .ToListAsync(ct);
            return (items, total);
        }

        public async Task<IReadOnlyList<ReportedExperienceProjection>> ListReportedAsync(CancellationToken ct = default)
            => await context.MatchExperiencePublications.AsNoTracking()
                .Where(p => p.Reports.Any(r => r.ResolvedAt == null) || p.HiddenReason == ExperienceReport.AutoHidden)
                .Select(p => new ReportedExperienceProjection(p.Id, p.JournalEntry!.User!.UserName, p.JournalEntry.Match!.Game!.Name,
                    p.RatingPublishedAt != null ? p.JournalEntry.PersonalRating : null, p.Comment, p.HiddenAt, p.HiddenReason,
                    p.Reports.Where(r => r.ResolvedAt == null).Select(r => r.Reason).ToList()))
                .ToListAsync(ct);

        public async Task AddAsync(MatchExperiencePublication publication, CancellationToken ct = default)
            => await context.MatchExperiencePublications.AddAsync(publication, ct);

        // Added as new explicitly: its key is set in the constructor, so the collection alone would be read as an update.
        public async Task AddReportAsync(ExperienceReport report, CancellationToken ct = default)
            => await context.ExperienceReports.AddAsync(report, ct);

        public void Remove(MatchExperiencePublication publication) => context.MatchExperiencePublications.Remove(publication);

        public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
    }
}
