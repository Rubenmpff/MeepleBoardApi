using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class ExperienceRepository(MeepleBoardDbContext context) : IExperienceRepository
    {
        public Task<MatchExperiencePublication?> GetByEntryForUpdateAsync(Guid journalEntryId, CancellationToken ct = default)
            => context.MatchExperiencePublications.Include(p => p.Photos).FirstOrDefaultAsync(p => p.JournalEntryId == journalEntryId, ct);

        public Task<MatchExperiencePublication?> GetWithReportsForUpdateAsync(Guid publicationId, CancellationToken ct = default)
            => context.MatchExperiencePublications.Include(p => p.JournalEntry).Include(p => p.Photos).Include(p => p.Reports).ThenInclude(r => r.Reporter)
                .FirstOrDefaultAsync(p => p.Id == publicationId, ct);

        public Task<int> CountPublishedSinceAsync(Guid userId, DateTime since, CancellationToken ct = default)
            => context.MatchExperiencePublications.CountAsync(p => p.JournalEntry!.UserId == userId
                && (p.RatingPublishedAt >= since || p.CommentPublishedAt >= since || p.Photos.Any(f => f.PublishedAt >= since)), ct);

        public async Task<(IReadOnlyList<ExperienceListProjection> Items, int Total)> ListVisibleForGameAsync(
            Guid gameId, Guid viewerId, int offset, int limit, CancellationToken ct = default)
        {
            // Only what is published and visible, of a rated entry whose author is still a player of the match.
            var query = context.MatchExperiencePublications.AsNoTracking()
                .Where(p => p.HiddenAt == null && (p.RatingPublishedAt != null || p.CommentPublishedAt != null
                        || p.Photos.Any(f => f.HiddenAt == null && !context.PhotoReports.Any(r => r.JournalEntryId == p.JournalEntryId && r.PhotoUrl == f.PhotoUrl && r.ReporterId == viewerId && r.ResolvedAt == null)))
                    && p.JournalEntry!.Match!.GameId == gameId && p.JournalEntry.PersonalRating != null
                    && p.JournalEntry.Match.MatchPlayers.Any(mp => mp.UserId == p.JournalEntry.UserId)
                    && !p.Reports.Any(r => r.ReporterId == viewerId && r.ResolvedAt == null)
                    // A block either way: neither sees the other's publications (list and count).
                    && !context.UserBlocks.Any(b => (b.BlockerId == viewerId && b.BlockedId == p.JournalEntry.UserId)
                        || (b.BlockerId == p.JournalEntry.UserId && b.BlockedId == viewerId)));
            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt).ThenBy(p => p.Id)
                .Skip(offset).Take(limit)
                .Select(p => new ExperienceListProjection(
                    p.Id, p.JournalEntry!.User!.UserName, p.JournalEntry.UserId == viewerId,
                    p.RatingPublishedAt != null ? p.JournalEntry.PersonalRating : null,
                    p.CommentPublishedAt != null ? p.Comment : null, p.CommentPublishedAt != null && p.CommentEditedAt != null,
                    p.JournalEntry.MatchId, p.JournalEntry.Match!.GameMode, p.JournalEntry.Match.IsSoloGame,
                    p.JournalEntry.Match.MatchPlayers.Count, p.JournalEntry.Match.MatchDate,
                    // Photos the viewer reported (open) are left out for them, as reported comments are.
                    p.Photos.Where(f => f.HiddenAt == null && !context.PhotoReports.Any(r => r.JournalEntryId == p.JournalEntryId && r.PhotoUrl == f.PhotoUrl && r.ReporterId == viewerId && r.ResolvedAt == null))
                        .OrderBy(f => f.Position).Select(f => f.Id).ToList()))
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

        public async Task AddPhotoAsync(PublicationPhoto photo, CancellationToken ct = default) => await context.PublicationPhotos.AddAsync(photo, ct);

        public Task<PublicPhotoProjection?> GetPublicPhotoAsync(Guid publicationId, Guid photoId, Guid viewerId, CancellationToken ct = default, bool evenIfReportedByViewer = false)
            => context.PublicationPhotos.AsNoTracking()
                .Where(f => f.Id == photoId && f.PublicationId == publicationId && f.HiddenAt == null && f.Publication!.HiddenAt == null
                    && f.Publication.JournalEntry!.PersonalRating != null
                    && f.Publication.JournalEntry.Match!.MatchPlayers.Any(mp => mp.UserId == f.Publication.JournalEntry.UserId)
                    && (evenIfReportedByViewer || !context.PhotoReports.Any(r => r.JournalEntryId == f.Publication.JournalEntryId && r.PhotoUrl == f.PhotoUrl && r.ReporterId == viewerId && r.ResolvedAt == null))
                    && !context.UserBlocks.Any(b => (b.BlockerId == viewerId && b.BlockedId == f.Publication.JournalEntry.UserId)
                        || (b.BlockerId == f.Publication.JournalEntry.UserId && b.BlockedId == viewerId)))
                .Select(f => new PublicPhotoProjection(f.Id, f.Publication!.JournalEntryId, f.Publication.JournalEntry!.UserId, f.PhotoUrl))
                .FirstOrDefaultAsync(ct);

        public async Task<IReadOnlyList<PublicationPhoto>> PhotosOfDiaryPhotoAsync(Guid journalEntryId, string photoUrl, CancellationToken ct = default)
            => await context.PublicationPhotos.Where(f => f.Publication!.JournalEntryId == journalEntryId && f.PhotoUrl == photoUrl).ToListAsync(ct);

        public Task<int> OpenPhotoReportersAsync(Guid journalEntryId, string photoUrl, CancellationToken ct = default)
            => context.PhotoReports.Where(r => r.JournalEntryId == journalEntryId && r.PhotoUrl == photoUrl && r.ResolvedAt == null)
                .Select(r => r.ReporterId).Distinct().CountAsync(ct);

        public void Remove(MatchExperiencePublication publication) => context.MatchExperiencePublications.Remove(publication);

        public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
    }
}
