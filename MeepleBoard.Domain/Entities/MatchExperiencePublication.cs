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
}
