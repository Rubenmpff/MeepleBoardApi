using MeepleBoard.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    public class UserGameLibraryDto
    {
        /// <summary>
        /// Identificador único do item na biblioteca.
        /// </summary>
        public Guid Id { get; init; }

        /// <summary>
        /// Identificação do jogo (Obrigatório).
        /// </summary>
        [Required(ErrorMessage = "O ID do jogo é obrigatório.")]
        public Guid GameId { get; init; }

        /// <summary>
        /// Nome do jogo (Obrigatório, máximo 150 caracteres).
        /// </summary>
        [Required(ErrorMessage = "O nome do jogo é obrigatório.")]
        [MaxLength(150, ErrorMessage = "O nome do jogo deve ter no máximo 150 caracteres.")]
        public string GameName { get; init; } = "Nome Desconhecido";

        /// <summary>
        /// URL da imagem do jogo (Opcional, validada como URL).
        /// </summary>
        [Url(ErrorMessage = "A URL da imagem do jogo não é válida.")]
        public string? GameImageUrl { get; init; }

        /// <summary>
        /// Status do jogo na biblioteca (Owned, Played, Wishlist).
        /// </summary>
        public GameLibraryStatus Status { get; init; }

        /// <summary>
        /// Valor pago pelo jogo (Opcional, mas não pode ser negativo).
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "O preço pago deve ser um valor positivo.")]
        public decimal? PricePaid { get; init; }

        /// <summary>
        /// Número total de partidas jogadas com esse jogo (Não pode ser negativo).
        /// </summary>
        [Range(0, int.MaxValue, ErrorMessage = "O número de partidas jogadas não pode ser negativo.")]
        public int TotalTimesPlayed { get; init; }

        /// <summary>
        /// Total de horas jogadas com esse jogo (Não pode ser negativo).
        /// </summary>
        [Range(0, int.MaxValue, ErrorMessage = "O total de horas jogadas não pode ser negativo.")]
        public int TotalHoursPlayed { get; init; }

        /// <summary>
        /// Data em que o jogo foi adicionado à biblioteca.
        /// </summary>
        public DateTime AddedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// Última vez que o usuário jogou este jogo (Opcional, mas não pode ser no futuro).
        /// </summary>
        private DateTime? _lastPlayedAt;

        public DateTime? LastPlayedAt
        {
            get => _lastPlayedAt;
            init
            {
                if (value > DateTime.UtcNow)
                    throw new ArgumentException("A data da última jogada não pode ser no futuro.");
                _lastPlayedAt = value;
            }
        }
    }
}