using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    /// <summary>
    /// DTO para representar uma partida no MeepleBoard.
    /// </summary>
    public class MatchDto
    {
        /// <summary>
        /// Identificador único da partida.
        /// </summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>
        /// Data da partida (Obrigatória e não pode estar no futuro).
        /// </summary>
        [Required(ErrorMessage = "A data da partida é obrigatória.")]
        [DataType(DataType.DateTime)]
        [PastDate(ErrorMessage = "A data da partida não pode estar no futuro.")]
        public DateTime MatchDate { get; init; }

        /// <summary>
        /// Identificação do jogo (Obrigatório).
        /// </summary>
        [Required(ErrorMessage = "O ID do jogo é obrigatório.")]
        [NotEqualToEmptyGuid(ErrorMessage = "O ID do jogo deve ser válido.")]
        public Guid GameId { get; init; }

        /// <summary>
        /// Nome do jogo (Obrigatório).
        /// </summary>
        [Required(ErrorMessage = "O nome do jogo é obrigatório.")]
        [MaxLength(150, ErrorMessage = "O nome do jogo deve ter no máximo 150 caracteres.")]
        public string GameName { get; init; } = string.Empty;

        /// <summary>
        /// Identificação do vencedor (Obrigatório se não for jogo solo).
        /// </summary>
        [WinnerRequiredIfNotSolo(ErrorMessage = "O vencedor é obrigatório para partidas multiplayer.")]
        [NotEqualToEmptyGuid(ErrorMessage = "O ID do vencedor deve ser válido.")]
        public Guid? WinnerId { get; init; }

        /// <summary>
        /// Nome do vencedor (Opcional).
        /// </summary>
        [MaxLength(100, ErrorMessage = "O nome do vencedor deve ter no máximo 100 caracteres.")]
        public string? WinnerName { get; init; }

        /// <summary>
        /// Indica se a partida foi jogada solo.
        /// </summary>
        public bool IsSoloGame { get; init; } = false;

        /// <summary>
        /// Duração da partida em minutos (Opcional).
        /// </summary>
        [Range(0, int.MaxValue, ErrorMessage = "A duração da partida não pode ser negativa.")]
        public int? DurationInMinutes { get; init; }

        /// <summary>
        /// Local onde a partida foi realizada (Opcional).
        /// </summary>
        [MaxLength(200, ErrorMessage = "O local da partida deve ter no máximo 200 caracteres.")]
        public string? Location { get; init; }

        /// <summary>
        /// Resumo do placar (Opcional).
        /// </summary>
        [MaxLength(500, ErrorMessage = "O resumo do placar deve ter no máximo 500 caracteres.")]
        public string? ScoreSummary { get; init; }

        /// <summary>
        /// Lista de jogadores (Opcional, deve ter pelo menos um jogador se fornecida).
        /// </summary>
        [MinLength(1, ErrorMessage = "A partida deve ter pelo menos um jogador.")]
        public List<MatchPlayerDto> Players { get; init; } = new List<MatchPlayerDto>();
    }

    /// <summary>
    /// Validação para garantir que um GUID não seja vazio.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class NotEqualToEmptyGuidAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value is Guid guidValue)
            {
                return guidValue != Guid.Empty;
            }
            return true; // Permite valores nulos
        }
    }

    /// <summary>
    /// Validação para garantir que a data da partida não seja futura.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class PastDateAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value == null) return true; // Permite valores nulos

            if (value is DateTime dateValue)
            {
                return dateValue <= DateTime.UtcNow;
            }

            return false; // Datas inválidas não são aceitas
        }
    }

    /// <summary>
    /// Validação para garantir que o vencedor seja obrigatório se a partida não for solo.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class WinnerRequiredIfNotSoloAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            // Verifica se o contexto e a instância são válidos
            if (validationContext?.ObjectInstance is not MatchDto matchDto)
            {
                return ValidationResult.Success; // Se não puder validar, não falha
            }

            // Se não for jogo solo e não houver vencedor, retorna erro
            if (!matchDto.IsSoloGame && (value == null || (value is Guid guidValue && guidValue == Guid.Empty)))
            {
                return new ValidationResult(ErrorMessage);
            }

            return ValidationResult.Success;
        }
    }
}