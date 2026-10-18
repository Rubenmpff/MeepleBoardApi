using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// A report about one photo the reporter can see (a diary photo of a match they played), identified by its diary entry
    /// and stored reference. Moderation decides; nothing is removed automatically.
    /// </summary>
    public class PhotoReport
    {
        public static readonly string[] Reasons = ["Inappropriate", "PersonalData", "Harassment", "Spam", "Other"];
        private PhotoReport() { }

        public PhotoReport(Guid reporterId, Guid journalEntryId, string photoUrl, string reason, string? detail)
        {
            if (reporterId == Guid.Empty || journalEntryId == Guid.Empty || string.IsNullOrWhiteSpace(photoUrl)) throw new ArgumentException("Pedido inválido.");
            if (!Reasons.Contains(reason)) throw new ArgumentException("Motivo inválido.");
            var text = detail?.Trim();
            if (text?.Length > 300) throw new ArgumentException("Usa no máximo 300 caracteres.");
            Id = Guid.NewGuid(); ReporterId = reporterId; JournalEntryId = journalEntryId; PhotoUrl = photoUrl; Reason = reason;
            Detail = string.IsNullOrEmpty(text) ? null : text; CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }
        public Guid ReporterId { get; private set; }
        public virtual User? Reporter { get; private set; }
        public Guid JournalEntryId { get; private set; }
        public virtual MatchJournalEntry? JournalEntry { get; private set; }
        [MaxLength(500)] public string PhotoUrl { get; private set; } = string.Empty;
        [MaxLength(16)] public string Reason { get; private set; } = string.Empty;
        [MaxLength(300)] public string? Detail { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? ResolvedAt { get; private set; }
        public Guid? ResolvedById { get; private set; }
        [MaxLength(16)] public string? Outcome { get; private set; }
        public bool IsOpen => ResolvedAt == null;

        public void Resolve(Guid administratorId, string outcome)
        {
            if (ResolvedAt != null) return;
            ResolvedAt = DateTime.UtcNow; ResolvedById = administratorId; Outcome = outcome;
            if (Reporter?.DeletedAt != null) Detail = null;
        }
    }
}
