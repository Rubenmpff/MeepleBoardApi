using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    public class MatchDto
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        [Required(ErrorMessage = "A data da partida é obrigatória.")]
        [DataType(DataType.DateTime)]
        [PastDate(ErrorMessage = "A data da partida não pode estar no futuro.")]
        public DateTime MatchDate { get; init; }

        [Required(ErrorMessage = "O ID do jogo é obrigatório.")]
        [NotEqualToEmptyGuid(ErrorMessage = "O ID do jogo deve ser válido.")]
        public Guid GameId { get; init; }

        [Required(ErrorMessage = "O nome do jogo é obrigatório.")]
        [MaxLength(150, ErrorMessage = "O nome do jogo deve ter no máximo 150 caracteres.")]
        public string GameName { get; init; } = string.Empty;

        /// <summary>URL da imagem do jogo — para mostrar na app.</summary>
        public string? GameImageUrl { get; init; }

        [WinnerRequiredIfNotSolo(ErrorMessage = "O vencedor é obrigatório para partidas multiplayer.")]
        [NotEqualToEmptyGuid(ErrorMessage = "O ID do vencedor deve ser válido.")]
        public Guid? WinnerId { get; init; }

        [MaxLength(100, ErrorMessage = "O nome do vencedor deve ter no máximo 100 caracteres.")]
        public string? WinnerName { get; init; }

        public bool IsSoloGame { get; init; } = false;

        [Range(0, int.MaxValue, ErrorMessage = "A duração da partida não pode ser negativa.")]
        public int? DurationInMinutes { get; init; }

        [MaxLength(200, ErrorMessage = "O local da partida deve ter no máximo 200 caracteres.")]
        public string? Location { get; init; }

        [MaxLength(500, ErrorMessage = "O resumo do placar deve ter no máximo 500 caracteres.")]
        public string? ScoreSummary { get; init; }

        [MinLength(1, ErrorMessage = "A partida deve ter pelo menos um jogador.")]
        public List<MatchPlayerDto> Players { get; init; } = new();

        // ── Diário de partida ────────────────────────────────────────────────

        [Range(0, 10, ErrorMessage = "O rating deve ser entre 0 e 10.")]
        public double? PersonalRating { get; init; }

        [MaxLength(2000)]
        public string? Notes { get; init; }

        [MaxLength(500)]
        public string? Tags { get; init; }

        // ── Estado do diário ─────────────────────────────────────────────────

        public string JournalStatus { get; init; } = "Open";
        public DateTime? ClosedAt { get; init; }

        // ── Modo oficial / não oficial ───────────────────────────────────────

        public bool IsOfficialMode { get; init; } = true;

        [MaxLength(500)]
        public string? UnofficialModeJustification { get; init; }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class NotEqualToEmptyGuidAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value is Guid guidValue) return guidValue != Guid.Empty;
            return true;
        }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class PastDateAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value == null) return true;
            // ✅ Margem de 1 minuto para latência de rede e fusos horários
            if (value is DateTime dateValue) return dateValue <= DateTime.UtcNow.AddMinutes(1);
            return false;
        }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class WinnerRequiredIfNotSoloAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (validationContext?.ObjectInstance is not MatchDto matchDto)
                return ValidationResult.Success;

            if (!matchDto.IsSoloGame && (value == null || (value is Guid guidValue && guidValue == Guid.Empty)))
                return new ValidationResult(ErrorMessage);

            return ValidationResult.Success;
        }
    }
}