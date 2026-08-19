using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Representa uma campanha de jogo de tabuleiro.
    ///
    /// Uma campanha agrupa partidas do mesmo jogo ao longo do tempo,
    /// com notas colaborativas, fotos e avaliações de todos os membros.
    ///
    /// Exemplos: Gloomhaven, Pandemic Legacy, Arkham Horror Campaign.
    ///
    /// Ciclo de vida:
    ///   Active → Completed
    ///         → Abandoned
    /// </summary>
    public class Campaign
    {
        private Campaign()
        {
            Members = new HashSet<CampaignMember>();
            CampaignMatches = new HashSet<CampaignMatch>();
        }

        public Campaign(string name, Guid gameId, Guid creatorId)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
                throw new ArgumentException("O nome da campanha deve ter pelo menos 2 caracteres.");
            if (gameId == Guid.Empty)
                throw new ArgumentException("ID do jogo inválido.");
            if (creatorId == Guid.Empty)
                throw new ArgumentException("ID do criador inválido.");

            Id = Guid.NewGuid();
            Name = name.Trim();
            GameId = gameId;
            CreatorId = creatorId;
            Status = CampaignStatus.Active;
            CreatedAt = DateTime.UtcNow;
            Members = new HashSet<CampaignMember>();
            CampaignMatches = new HashSet<CampaignMatch>();
        }

        [Key]
        public Guid Id { get; private set; }

        [Required, MaxLength(200)]
        public string Name { get; private set; } = string.Empty;

        [Required]
        public Guid GameId { get; private set; }

        [ForeignKey("GameId")]
        public virtual Game? Game { get; private set; }

        /// <summary>Quem criou a campanha — não pode ser removido.</summary>
        [Required]
        public Guid CreatorId { get; private set; }

        [ForeignKey("CreatorId")]
        public virtual User? Creator { get; private set; }

        public CampaignStatus Status { get; private set; } = CampaignStatus.Active;

        /// <summary>Notas globais da campanha — editável por qualquer membro.</summary>
        [MaxLength(5000)]
        public string? Notes { get; private set; }

        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }
        public DateTime? CompletedAt { get; private set; }

        public virtual ICollection<CampaignMember> Members { get; private set; }
        public virtual ICollection<CampaignMatch> CampaignMatches { get; private set; }

        // ── Métodos ───────────────────────────────────────────────────────────

        public void UpdateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("O nome não pode estar vazio.");
            Name = name.Trim();
            Touch();
        }

        public void UpdateNotes(string? notes)
        {
            Notes = notes?.Trim();
            Touch();
        }

        public void Complete()
        {
            if (Status == CampaignStatus.Completed) return;
            Status = CampaignStatus.Completed;
            CompletedAt = DateTime.UtcNow;
            Touch();
        }

        public void Abandon()
        {
            if (Status == CampaignStatus.Abandoned) return;
            Status = CampaignStatus.Abandoned;
            Touch();
        }

        public void Reactivate()
        {
            Status = CampaignStatus.Active;
            Touch();
        }

        private void Touch() => UpdatedAt = DateTime.UtcNow;
    }

    public enum CampaignStatus
    {
        Active = 0,
        Completed = 1,
        Abandoned = 2
    }
}