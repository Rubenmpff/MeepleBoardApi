using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>A report about a person, for moderation only (the person never sees who). Never acts by itself.</summary>
    public class UserReport
    {
        public static readonly string[] Reasons = ["Abuse", "Harassment", "OffensiveName", "Spam", "Other"];
        private UserReport() { }

        public UserReport(Guid reporterId, Guid reportedUserId, string reason, string? detail)
        {
            if (reporterId == Guid.Empty || reportedUserId == Guid.Empty || reporterId == reportedUserId) throw new ArgumentException("Pedido inválido.");
            if (!Reasons.Contains(reason)) throw new ArgumentException("Motivo inválido.");
            var text = detail?.Trim();
            if (text?.Length > 300) throw new ArgumentException("Usa no máximo 300 caracteres.");
            Id = Guid.NewGuid(); ReporterId = reporterId; ReportedUserId = reportedUserId; Reason = reason;
            Detail = string.IsNullOrEmpty(text) ? null : text; CreatedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }
        public Guid ReporterId { get; private set; }
        public virtual User? Reporter { get; private set; }
        public Guid ReportedUserId { get; private set; }
        public virtual User? ReportedUser { get; private set; }
        [MaxLength(16)] public string Reason { get; private set; } = string.Empty;
        [MaxLength(300)] public string? Detail { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? ResolvedAt { get; private set; }
        public Guid? ResolvedById { get; private set; }
        [MaxLength(16)] public string? Outcome { get; private set; }
        public bool IsOpen => ResolvedAt == null;

        // A report by a deleted account keeps its free text only while open (as for publications).
        public void Resolve(Guid administratorId, string outcome)
        {
            if (ResolvedAt != null) return;
            ResolvedAt = DateTime.UtcNow; ResolvedById = administratorId; Outcome = outcome;
            if (Reporter?.DeletedAt != null) Detail = null;
        }
    }
}
