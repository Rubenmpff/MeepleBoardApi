using MeepleBoard.Domain.Entities;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Services.Experiences
{
    public sealed record PublicationDto(bool Rated, double? Rating, bool ShowRating, string? Comment, bool CommentPublished, bool Edited,
        DateTime? PublishedAt, bool Hidden, string? HiddenReason);
    public sealed record SetPublicationDto(bool ShowRating, string? Comment, bool PublishComment);
    public sealed record ExperienceDto(Guid Id, string AuthorName, bool IsMine, double? Rating, string? Comment, string Mode, int Players,
        string Month, bool Edited, bool CanReport);
    public sealed record ExperiencePageDto(IReadOnlyList<ExperienceDto> Items, int Total);
    public sealed record ReportDto(string Reason, string? Detail);
    public sealed record ReportedExperienceDto(Guid Id, string AuthorName, string GameName, double? Rating, string? Comment,
        Dictionary<string, int> Reasons, bool Hidden, string? HiddenReason);

    /// <summary>
    /// Publishing an experience: the participant's own choices for one diary entry (rating with the name, a comment,
    /// or both), the public list on the game page, reports and moderation. Publishing never creates or changes a
    /// rating and never touches averages. Public responses carry only author name, the published rating and/or
    /// comment, mode, number of players and month — never the match, other participants, place, notes or the author id.
    /// </summary>
    public sealed class ExperienceService(MeepleBoardDbContext db)
    {
        /// <summary>Publications that may start per person in 24 hours.</summary>
        public const int DailyLimit = 20;

        async Task<MatchJournalEntry> OwnEntry(Guid matchId, Guid userId, CancellationToken ct)
        {
            if (userId == Guid.Empty) throw new UnauthorizedAccessException();
            var isPlayer = await db.MatchPlayers.AnyAsync(p => p.MatchId == matchId && p.UserId == userId, ct);
            if (!await db.Matches.AnyAsync(m => m.Id == matchId, ct)) throw new KeyNotFoundException("Partida não encontrada.");
            if (!isPlayer) throw new UnauthorizedAccessException();
            return await db.MatchJournalEntries.FirstOrDefaultAsync(e => e.MatchId == matchId && e.UserId == userId, ct)
                ?? throw new InvalidOperationException("Avalia a partida antes de publicar.");
        }

        static PublicationDto Map(MatchJournalEntry e, MatchExperiencePublication? p) => new(e.PersonalRating.HasValue, e.PersonalRating,
            p?.RatingPublishedAt != null, p?.Comment, p?.CommentPublishedAt != null, p?.CommentEditedAt != null,
            p == null ? null : new[] { p.RatingPublishedAt, p.CommentPublishedAt }.Max(), p?.HiddenAt != null, p?.HiddenReason);

        public async Task<PublicationDto> GetMineAsync(Guid matchId, Guid userId, CancellationToken ct = default)
        {
            if (userId == Guid.Empty) throw new UnauthorizedAccessException();
            if (!await db.MatchPlayers.AnyAsync(p => p.MatchId == matchId && p.UserId == userId, ct)) throw new UnauthorizedAccessException();
            var entry = await db.MatchJournalEntries.AsNoTracking().FirstOrDefaultAsync(e => e.MatchId == matchId && e.UserId == userId, ct);
            if (entry == null) return new PublicationDto(false, null, false, null, false, false, null, false, null);
            return Map(entry, await db.MatchExperiencePublications.AsNoTracking().FirstOrDefaultAsync(p => p.JournalEntryId == entry.Id, ct));
        }

        public async Task<PublicationDto> SetAsync(Guid matchId, Guid userId, SetPublicationDto dto, CancellationToken ct = default)
        {
            var entry = await OwnEntry(matchId, userId, ct);
            if (!entry.PersonalRating.HasValue) throw new InvalidOperationException("Avalia a partida antes de publicar.");
            var pub = await db.MatchExperiencePublications.FirstOrDefaultAsync(p => p.JournalEntryId == entry.Id, ct);
            var startsSomething = (dto.ShowRating && pub?.RatingPublishedAt == null) || (dto.PublishComment && pub?.CommentPublishedAt == null);
            if (startsSomething)
            {
                var since = DateTime.UtcNow.AddHours(-24);
                var recent = await db.MatchExperiencePublications.CountAsync(p => p.JournalEntry!.UserId == userId
                    && (p.RatingPublishedAt >= since || p.CommentPublishedAt >= since), ct);
                if (recent >= DailyLimit) throw new InvalidOperationException("Chegaste ao limite de publicações de hoje.");
            }
            if (pub == null) { pub = new MatchExperiencePublication(entry.Id); db.MatchExperiencePublications.Add(pub); }
            pub.Set(dto.ShowRating, dto.Comment, dto.PublishComment);
            if (pub.IsEmpty) db.MatchExperiencePublications.Remove(pub);
            await db.SaveChangesAsync(ct);
            return Map(entry, pub.IsEmpty ? null : pub);
        }

        public async Task<PublicationDto> ChangeAsync(Guid matchId, Guid userId, string action, CancellationToken ct = default)
        {
            var entry = await OwnEntry(matchId, userId, ct);
            var pub = await db.MatchExperiencePublications.FirstOrDefaultAsync(p => p.JournalEntryId == entry.Id, ct)
                ?? throw new KeyNotFoundException("Nada publicado nesta partida.");
            if (action == "withdraw-rating") pub.WithdrawRating();
            else if (action == "withdraw-comment") pub.WithdrawComment();
            else if (action == "delete-comment") pub.DeleteComment();
            else throw new ArgumentException("Ação inválida.");
            if (pub.IsEmpty) db.MatchExperiencePublications.Remove(pub);
            await db.SaveChangesAsync(ct);
            return Map(entry, pub.IsEmpty ? null : pub);
        }

        public async Task<ExperiencePageDto> ListForGameAsync(Guid gameId, Guid viewerId, int offset, int limit, CancellationToken ct = default)
        {
            if (viewerId == Guid.Empty) throw new UnauthorizedAccessException();
            limit = Math.Clamp(limit, 1, 50); offset = Math.Max(0, offset);
            var query = db.MatchExperiencePublications.AsNoTracking()
                .Where(p => p.HiddenAt == null && (p.RatingPublishedAt != null || p.CommentPublishedAt != null)
                    && p.JournalEntry!.Match!.GameId == gameId && p.JournalEntry.PersonalRating != null
                    && p.JournalEntry.Match.MatchPlayers.Any(mp => mp.UserId == p.JournalEntry.UserId)
                    && !p.Reports.Any(r => r.ReporterId == viewerId && r.ResolvedAt == null));
            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt).ThenBy(p => p.Id)
                .Skip(offset).Take(limit)
                .Select(p => new
                {
                    p.Id, Author = p.JournalEntry!.User!.UserName, Mine = p.JournalEntry.UserId == viewerId,
                    Rating = p.RatingPublishedAt != null ? p.JournalEntry.PersonalRating : null,
                    Comment = p.CommentPublishedAt != null ? p.Comment : null, Edited = p.CommentPublishedAt != null && p.CommentEditedAt != null,
                    p.JournalEntry.Match!.GameMode, p.JournalEntry.Match.IsSoloGame, Players = p.JournalEntry.Match.MatchPlayers.Count, p.JournalEntry.Match.MatchDate
                }).ToListAsync(ct);
            return new ExperiencePageDto(rows.Select(r => new ExperienceDto(r.Id, r.Author ?? "—", r.Mine, r.Rating, r.Comment,
                r.GameMode ?? (r.IsSoloGame ? "SOLO" : "UNKNOWN"), r.Players, r.MatchDate.ToString("yyyy-MM"), r.Edited, r.Comment != null && !r.Mine)).ToList(), total);
        }

        public async Task ReportAsync(Guid publicationId, Guid reporterId, ReportDto dto, CancellationToken ct = default)
        {
            if (reporterId == Guid.Empty) throw new UnauthorizedAccessException();
            var pub = await db.MatchExperiencePublications.Include(p => p.JournalEntry).Include(p => p.Reports)
                .FirstOrDefaultAsync(p => p.Id == publicationId, ct);
            if (pub == null || pub.CommentPublishedAt == null || pub.HiddenAt != null) throw new KeyNotFoundException("Comentário não encontrado.");
            if (pub.JournalEntry!.UserId == reporterId) throw new InvalidOperationException("Não podes reportar o teu comentário.");
            if (pub.Reports.Any(r => r.ReporterId == reporterId && r.IsOpen)) throw new InvalidOperationException("Já reportaste este comentário.");
            // Added as new explicitly: its key is set in the constructor, so the collection alone would be read as an update.
            var report = new ExperienceReport(pub.Id, reporterId, dto.Reason, dto.Detail);
            db.ExperienceReports.Add(report);
            pub.Reports.Add(report);
            // Distinct people with an open report hide it until an administrator reviews it.
            if (pub.Reports.Where(r => r.IsOpen).Select(r => r.ReporterId).Distinct().Count() >= ExperienceReport.AutoHideThreshold)
                pub.Hide(ExperienceReport.AutoHidden, null);
            await db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<ReportedExperienceDto>> ListReportedAsync(CancellationToken ct = default)
        {
            var rows = await db.MatchExperiencePublications.AsNoTracking()
                .Where(p => p.Reports.Any(r => r.ResolvedAt == null) || p.HiddenReason == ExperienceReport.AutoHidden)
                .Select(p => new { p.Id, Author = p.JournalEntry!.User!.UserName, Game = p.JournalEntry.Match!.Game!.Name,
                    Rating = p.RatingPublishedAt != null ? p.JournalEntry.PersonalRating : null, p.Comment, p.HiddenAt, p.HiddenReason,
                    Reasons = p.Reports.Where(r => r.ResolvedAt == null).Select(r => r.Reason).ToList() })
                .ToListAsync(ct);
            return rows.Select(r => new ReportedExperienceDto(r.Id, r.Author ?? "—", r.Game, r.Rating, r.Comment,
                r.Reasons.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()), r.HiddenAt != null, r.HiddenReason)).ToList();
        }

        public async Task ModerateAsync(Guid publicationId, Guid administratorId, string action, string? reason, CancellationToken ct = default)
        {
            var pub = await db.MatchExperiencePublications.Include(p => p.Reports).FirstOrDefaultAsync(p => p.Id == publicationId, ct)
                ?? throw new KeyNotFoundException("Comentário não encontrado.");
            if (action == "keep") { if (pub.HiddenReason == ExperienceReport.AutoHidden) pub.Restore(); }
            else if (action == "hide") pub.Hide(reason ?? throw new ArgumentException("Indica o motivo."), administratorId);
            else if (action == "restore") pub.Restore();
            else throw new ArgumentException("Ação inválida.");
            foreach (var report in pub.Reports.Where(r => r.IsOpen)) report.Resolve(administratorId, action);
            await db.SaveChangesAsync(ct);
        }
    }
}
