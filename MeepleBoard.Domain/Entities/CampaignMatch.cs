using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Liga uma partida a uma campanha.
    /// Uma partida pode estar associada a uma campanha (opcional).
    /// O número de sessão é opcional — serve para ordenar/identificar
    /// (ex: "Sessão 3", "Cenário 5").
    /// </summary>
    public class CampaignMatch
    {
        private CampaignMatch() { }

        public CampaignMatch(Guid campaignId, Guid matchId, int? sessionNumber = null, string? sessionTitle = null)
        {
            if (campaignId == Guid.Empty)
                throw new ArgumentException("ID da campanha inválido.");
            if (matchId == Guid.Empty)
                throw new ArgumentException("ID da partida inválido.");

            Id = Guid.NewGuid();
            CampaignId = campaignId;
            MatchId = matchId;
            SessionNumber = sessionNumber;
            SessionTitle = sessionTitle?.Trim();
            AddedAt = DateTime.UtcNow;
        }

        [Key]
        public Guid Id { get; private set; }

        [Required]
        public Guid CampaignId { get; private set; }

        [ForeignKey("CampaignId")]
        public virtual Campaign? Campaign { get; private set; }

        [Required]
        public Guid MatchId { get; private set; }

        [ForeignKey("MatchId")]
        public virtual Match? Match { get; private set; }

        /// <summary>
        /// Número da sessão dentro da campanha (opcional).
        /// Ex: 1, 2, 3... ou número do cenário no Gloomhaven.
        /// </summary>
        public int? SessionNumber { get; private set; }

        /// <summary>
        /// Título da sessão (opcional).
        /// Ex: "Cenário 3 — A Cripta", "Janeiro", "Ato 1".
        /// </summary>
        [MaxLength(200)]
        public string? SessionTitle { get; private set; }

        public DateTime AddedAt { get; private set; }

        // ── Métodos ───────────────────────────────────────────────────────────

        public void UpdateSession(int? sessionNumber, string? sessionTitle)
        {
            SessionNumber = sessionNumber;
            SessionTitle = sessionTitle?.Trim();
        }
    }
}