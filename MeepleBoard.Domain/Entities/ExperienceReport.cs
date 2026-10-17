using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
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

        // A report by a deleted account keeps its free text only while open (docs/proposta-eliminacao-conta.md, D6).
        public void Resolve(Guid? administratorId, string outcome) { ResolvedAt = DateTime.UtcNow; ResolvedById = administratorId; Outcome = outcome; if (Reporter?.DeletedAt != null) Detail = null; }
    }
}
