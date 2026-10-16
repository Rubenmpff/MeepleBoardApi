using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Publishing an experience: the participant's own choices for one diary entry (rating with the name, a comment,
    /// or both), the public list on the game page, reports and moderation. Publishing never creates or changes a
    /// rating and never touches averages. Public responses carry only author name, the published rating and/or
    /// comment, mode, number of players and month — never the match, other participants, place, notes or the author id.
    /// </summary>
    public sealed class ExperienceService(IExperienceRepository experiences, IMatchRepository matches,
        IMatchPlayerRepository players, ICampaignRepository journals) : IExperienceService
    {
        /// <summary>Publications that may start per person in 24 hours.</summary>
        public const int DailyLimit = 20;

        async Task<MatchJournalEntry> OwnEntry(Guid matchId, Guid userId, CancellationToken ct)
        {
            if (userId == Guid.Empty) throw new UnauthorizedAccessException();
            var isPlayer = await players.ExistsAsync(userId, matchId, ct);
            if (!await matches.ExistsAsync(matchId, ct)) throw new KeyNotFoundException("Partida não encontrada.");
            if (!isPlayer) throw new UnauthorizedAccessException();
            return await journals.GetJournalEntryAsync(matchId, userId, ct)
                ?? throw new InvalidOperationException("Avalia a partida antes de publicar.");
        }

        static PublicationDto Map(MatchJournalEntry e, MatchExperiencePublication? p) => new(e.PersonalRating.HasValue, e.PersonalRating,
            p?.RatingPublishedAt != null, p?.Comment, p?.CommentPublishedAt != null, p?.CommentEditedAt != null,
            p == null ? null : new[] { p.RatingPublishedAt, p.CommentPublishedAt }.Max(), p?.HiddenAt != null, p?.HiddenReason);

        public async Task<PublicationDto> GetMineAsync(Guid matchId, Guid userId, CancellationToken ct = default)
        {
            if (userId == Guid.Empty) throw new UnauthorizedAccessException();
            if (!await players.ExistsAsync(userId, matchId, ct)) throw new UnauthorizedAccessException();
            var entry = await journals.GetJournalEntryAsync(matchId, userId, ct);
            if (entry == null) return new PublicationDto(false, null, false, null, false, false, null, false, null);
            return Map(entry, await experiences.GetByEntryForUpdateAsync(entry.Id, ct));
        }

        public async Task<PublicationDto> SetAsync(Guid matchId, Guid userId, SetPublicationDto dto, CancellationToken ct = default)
        {
            var entry = await OwnEntry(matchId, userId, ct);
            if (!entry.PersonalRating.HasValue) throw new InvalidOperationException("Avalia a partida antes de publicar.");
            var pub = await experiences.GetByEntryForUpdateAsync(entry.Id, ct);
            var startsSomething = (dto.ShowRating && pub?.RatingPublishedAt == null) || (dto.PublishComment && pub?.CommentPublishedAt == null);
            if (startsSomething && await experiences.CountPublishedSinceAsync(userId, DateTime.UtcNow.AddHours(-24), ct) >= DailyLimit)
                throw new InvalidOperationException("Chegaste ao limite de publicações de hoje.");
            if (pub == null) { pub = new MatchExperiencePublication(entry.Id); await experiences.AddAsync(pub, ct); }
            pub.Set(dto.ShowRating, dto.Comment, dto.PublishComment);
            if (pub.IsEmpty) experiences.Remove(pub);
            await experiences.SaveChangesAsync(ct);
            return Map(entry, pub.IsEmpty ? null : pub);
        }

        public async Task<PublicationDto> ChangeAsync(Guid matchId, Guid userId, string action, CancellationToken ct = default)
        {
            var entry = await OwnEntry(matchId, userId, ct);
            var pub = await experiences.GetByEntryForUpdateAsync(entry.Id, ct)
                ?? throw new KeyNotFoundException("Nada publicado nesta partida.");
            if (action == "withdraw-rating") pub.WithdrawRating();
            else if (action == "withdraw-comment") pub.WithdrawComment();
            else if (action == "delete-comment") pub.DeleteComment();
            else throw new ArgumentException("Ação inválida.");
            if (pub.IsEmpty) experiences.Remove(pub);
            await experiences.SaveChangesAsync(ct);
            return Map(entry, pub.IsEmpty ? null : pub);
        }

        public async Task<ExperiencePageDto> ListForGameAsync(Guid gameId, Guid viewerId, int offset, int limit, CancellationToken ct = default)
        {
            if (viewerId == Guid.Empty) throw new UnauthorizedAccessException();
            limit = Math.Clamp(limit, 1, 50); offset = Math.Max(0, offset);
            var (rows, total) = await experiences.ListVisibleForGameAsync(gameId, viewerId, offset, limit, ct);
            return new ExperiencePageDto(rows.Select(r => new ExperienceDto(r.Id, r.AuthorName ?? "—", r.IsMine, r.Rating, r.Comment,
                r.GameMode ?? (r.IsSoloGame ? "SOLO" : "UNKNOWN"), r.Players, r.MatchDate.ToString("yyyy-MM"), r.Edited,
                r.Comment != null && !r.IsMine, r.IsMine ? r.MatchId : null)).ToList(), total);
        }

        public async Task ReportAsync(Guid publicationId, Guid reporterId, ReportDto dto, CancellationToken ct = default)
        {
            if (reporterId == Guid.Empty) throw new UnauthorizedAccessException();
            var pub = await experiences.GetWithReportsForUpdateAsync(publicationId, ct);
            if (pub == null || pub.CommentPublishedAt == null || pub.HiddenAt != null) throw new KeyNotFoundException("Comentário não encontrado.");
            if (pub.JournalEntry!.UserId == reporterId) throw new InvalidOperationException("Não podes reportar o teu comentário.");
            if (pub.Reports.Any(r => r.ReporterId == reporterId && r.IsOpen)) throw new InvalidOperationException("Já reportaste este comentário.");
            var report = new ExperienceReport(pub.Id, reporterId, dto.Reason, dto.Detail);
            await experiences.AddReportAsync(report, ct);
            pub.Reports.Add(report);
            // Distinct people with an open report hide it until an administrator reviews it.
            if (pub.Reports.Where(r => r.IsOpen).Select(r => r.ReporterId).Distinct().Count() >= ExperienceReport.AutoHideThreshold)
                pub.Hide(ExperienceReport.AutoHidden, null);
            await experiences.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<ReportedExperienceDto>> ListReportedAsync(CancellationToken ct = default)
            => (await experiences.ListReportedAsync(ct)).Select(r => new ReportedExperienceDto(r.Id, r.AuthorName ?? "—", r.GameName, r.Rating, r.Comment,
                r.OpenReasons.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()), r.HiddenAt != null, r.HiddenReason)).ToList();

        public async Task ModerateAsync(Guid publicationId, Guid administratorId, string action, string? reason, CancellationToken ct = default)
        {
            var pub = await experiences.GetWithReportsForUpdateAsync(publicationId, ct)
                ?? throw new KeyNotFoundException("Comentário não encontrado.");
            if (action == "keep") { if (pub.HiddenReason == ExperienceReport.AutoHidden) pub.Restore(); }
            else if (action == "hide") pub.Hide(reason ?? throw new ArgumentException("Indica o motivo."), administratorId);
            else if (action == "restore") pub.Restore();
            else throw new ArgumentException("Ação inválida.");
            foreach (var report in pub.Reports.Where(r => r.IsOpen)) report.Resolve(administratorId, action);
            await experiences.SaveChangesAsync(ct);
        }
    }
}
