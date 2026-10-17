using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// A user's report of a broken, wrong or incompatible resource link.
    /// Stays open until an administrator resolves it; one open report per user and resource.
    /// </summary>
    public class GameResourceReport
    {
        public static readonly string[] Reasons = ["BrokenLink", "WrongGame", "WrongLanguage", "Incompatible", "Other"];

        [Key]
        public Guid Id { get; private set; }
        public Guid ResourceId { get; private set; }
        public virtual GameResource Resource { get; private set; } = null!;
        public Guid UserId { get; private set; }
        public virtual User User { get; private set; } = null!;
        [Required, MaxLength(16)] public string Reason { get; private set; } = string.Empty;
        [MaxLength(500)] public string? Comment { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? ResolvedAt { get; private set; }
        public Guid? ResolvedById { get; private set; }

        private GameResourceReport() { }

        public GameResourceReport(Guid resourceId, Guid userId, string reason, string? comment)
        {
            if (resourceId == Guid.Empty || userId == Guid.Empty) throw new ArgumentException("Recurso ou utilizador inválido.");
            if (!Reasons.Contains(reason)) throw new ArgumentException("Motivo inválido.");
            var text = comment?.Trim();
            if (text?.Length > 500) throw new ArgumentException("Usa no máximo 500 caracteres.");
            if (reason == "Other" && string.IsNullOrEmpty(text)) throw new ArgumentException("Descreve o problema.");
            Id = Guid.NewGuid(); ResourceId = resourceId; UserId = userId; Reason = reason;
            Comment = string.IsNullOrEmpty(text) ? null : text; CreatedAt = DateTime.UtcNow;
        }

        public bool IsOpen => ResolvedAt == null;

        public void Resolve(Guid administratorId)
        {
            if (ResolvedAt != null) return;
            ResolvedAt = DateTime.UtcNow; ResolvedById = administratorId;
            // A report by a deleted account keeps its free text only while open (docs/proposta-eliminacao-conta.md, D6).
            if (User?.DeletedAt != null) Comment = null;
        }
    }
}
