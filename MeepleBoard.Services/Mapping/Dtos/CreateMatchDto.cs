using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeepleBoard.Services.Mapping.Dtos
{
    public class CreateMatchDto
    {
        [Required(ErrorMessage = "O ID do jogo é obrigatório.")]
        public Guid GameId { get; init; }

        [Required(ErrorMessage = "O nome do jogo é obrigatório.")]
        [MaxLength(150)]
        public string GameName { get; init; } = string.Empty;

        /// <summary>Se preenchido, este match pertence a uma sessão.</summary>
        public Guid? GameSessionId { get; init; }

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
    }
}
