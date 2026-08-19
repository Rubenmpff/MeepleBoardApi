using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.Mapping.Dtos
{
    public class CreateMatchDto
    {
        [Required(ErrorMessage = "O ID do jogo é obrigatório.")]
        public Guid GameId { get; init; }

        [Required(ErrorMessage = "O nome do jogo é obrigatório.")]
        [MaxLength(150)]
        public string GameName { get; init; } = string.Empty;

        /// <summary>Se preenchido, este match pertence a uma sessão de jogo.</summary>
        public Guid? GameSessionId { get; init; }

        /// <summary>Se preenchido, este match pertence a uma sessão de campanha.</summary>
        public Guid? CampaignSessionId { get; init; }

        [Required(ErrorMessage = "A data da partida é obrigatória.")]
        public DateTime MatchDate { get; init; }

        public Guid? WinnerId { get; init; }

        public bool IsSoloGame { get; init; }

        public int? DurationInMinutes { get; init; }

        public string? Location { get; init; }

        public string? ScoreSummary { get; init; }

        [Required]
        [MinLength(1, ErrorMessage = "A partida deve ter pelo menos um jogador.")]
        public List<Guid> PlayerIds { get; init; } = new();

        // ── Diário de partida ────────────────────────────────────────────────

        /// <summary>
        /// Avaliação pessoal desta partida (0–10, escala BGG).
        /// A média de todas as partidas dá a avaliação pessoal do utilizador para o jogo.
        /// </summary>
        [Range(0, 10, ErrorMessage = "O rating deve estar entre 0 e 10.")]
        public double? PersonalRating { get; init; }

        /// <summary>Notas livres sobre a partida.</summary>
        [MaxLength(2000)]
        public string? Notes { get; init; }

        /// <summary>Tags separadas por vírgula.</summary>
        [MaxLength(500)]
        public string? Tags { get; init; }

        // ── Modo oficial / não oficial ───────────────────────────────────────

        /// <summary>
        /// Justificação para usar um modo não disponível no BGG.
        /// Se preenchido, a partida é marcada como não oficial
        /// e não conta para rankings nem ratings.
        /// </summary>
        [MaxLength(500)]
        public string? UnofficialModeJustification { get; init; }
    }
}