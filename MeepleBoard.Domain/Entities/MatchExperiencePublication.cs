using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// What a participant chose to publish about one experience (one diary entry): their rating with their name,
    /// a comment, or both — each chosen explicitly and withdrawn independently. Publishing never creates or changes a
    /// rating; the rating shown is always the diary entry's (closed). Private notes never pass through here.
    /// </summary>
    public class MatchExperiencePublication
    {
        public const int MaxComment = 500;
        private MatchExperiencePublication() { }

        public MatchExperiencePublication(Guid journalEntryId)
        {
            if (journalEntryId == Guid.Empty) throw new ArgumentException("Entrada inválida.");
            Id = Guid.NewGuid(); JournalEntryId = journalEntryId; CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }
        public Guid JournalEntryId { get; private set; }
        public virtual MatchJournalEntry? JournalEntry { get; private set; }
        public DateTime? RatingPublishedAt { get; private set; }
        [MaxLength(MaxComment)] public string? Comment { get; private set; }
        public DateTime? CommentPublishedAt { get; private set; }
        public DateTime? CommentEditedAt { get; private set; }
        public DateTime? HiddenAt { get; private set; }
        [MaxLength(32)] public string? HiddenReason { get; private set; }
        public Guid? HiddenById { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }
        public virtual ICollection<ExperienceReport> Reports { get; private set; } = new List<ExperienceReport>();

        public bool IsVisible => HiddenAt == null && (RatingPublishedAt != null || CommentPublishedAt != null);
        public bool IsEmpty => RatingPublishedAt == null && Comment == null;

        /// <summary>Explicit choices: show the rating with the name; keep a comment, published or not.</summary>
        public void Set(bool showRating, string? comment, bool publishComment)
        {
            var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            if (text?.Length > MaxComment) throw new ArgumentException($"Usa no máximo {MaxComment} caracteres.");
            if (publishComment && text == null) throw new ArgumentException("Escreve o comentário antes de o publicar.");
            var now = DateTime.UtcNow;
            RatingPublishedAt = showRating ? RatingPublishedAt ?? now : null;
            if (CommentPublishedAt != null && publishComment && text != Comment) CommentEditedAt = now;
            Comment = text;
            CommentPublishedAt = publishComment ? CommentPublishedAt ?? now : null;
            if (!publishComment) CommentEditedAt = null;
            UpdatedAt = now;
        }

        public void WithdrawRating() { RatingPublishedAt = null; UpdatedAt = DateTime.UtcNow; }
        public void WithdrawComment() { CommentPublishedAt = null; CommentEditedAt = null; UpdatedAt = DateTime.UtcNow; }
        public void DeleteComment() { Comment = null; CommentPublishedAt = null; CommentEditedAt = null; UpdatedAt = DateTime.UtcNow; }

        public void Hide(string reason, Guid? byId)
        {
            if (!ExperienceReport.Reasons.Contains(reason) && reason != ExperienceReport.AutoHidden) throw new ArgumentException("Motivo inválido.");
            HiddenAt = DateTime.UtcNow; HiddenReason = reason; HiddenById = byId; UpdatedAt = HiddenAt;
        }

        public void Restore() { HiddenAt = null; HiddenReason = null; HiddenById = null; UpdatedAt = DateTime.UtcNow; }
    }

    /// <summary>A report about a published comment. One open report per person and comment; the author never sees who.</summary>
    public class ExperienceReport
    {
        public static readonly string[] Reasons = ["Abusive", "Spam", "PersonalData", "OffTopic", "Other"];
        public const string AutoHidden = "AutoReports";
        /// <summary>Distinct people with an open report that hide a comment until it is reviewed.</summary>
        public const int AutoHideThreshold = 3;
        private ExperienceReport() { }

        public ExperienceReport(Guid publicationId, Guid reporterId, string reason, string? detail)
        {
            if (publicationId == Guid.Empty || reporterId == Guid.Empty) throw new ArgumentException("Pedido inválido.");
            if (!Reasons.Contains(reason)) throw new ArgumentException("Motivo inválido.");
            var text = detail?.Trim();
            if (text?.Length > 300) throw new ArgumentException("Usa no máximo 300 caracteres.");
            Id = Guid.NewGuid(); PublicationId = publicationId; ReporterId = reporterId; Reason = reason;
            Detail = string.IsNullOrEmpty(text) ? null : text; CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }
        public Guid PublicationId { get; private set; }
        public virtual MatchExperiencePublication? Publication { get; private set; }
        public Guid ReporterId { get; private set; }
        public virtual User? Reporter { get; private set; }
        [MaxLength(16)] public string Reason { get; private set; } = string.Empty;
        [MaxLength(300)] public string? Detail { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? ResolvedAt { get; private set; }
        public Guid? ResolvedById { get; private set; }
        [MaxLength(16)] public string? Outcome { get; private set; }
        public bool IsOpen => ResolvedAt == null;

        public void Resolve(Guid? administratorId, string outcome) { ResolvedAt = DateTime.UtcNow; ResolvedById = administratorId; Outcome = outcome; }
    }
}
