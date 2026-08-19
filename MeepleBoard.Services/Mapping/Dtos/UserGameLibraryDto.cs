using MeepleBoard.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    public class UserGameLibraryDto
    {
        public Guid Id { get; init; }

        [Required(ErrorMessage = "O ID do jogo é obrigatório.")]
        public Guid GameId { get; init; }

        public int? BggId { get; set; }

        public double? AverageRating { get; set; }
        public int? MinPlayers { get; set; }
        public int? MaxPlayers { get; set; }
        public bool IsExpansion { get; set; }
        public bool IsCooperative { get; set; }
        public bool SupportsSoloMode { get; set; }

        [Required(ErrorMessage = "O nome do jogo é obrigatório.")]
        [MaxLength(150, ErrorMessage = "O nome do jogo deve ter no máximo 150 caracteres.")]
        public string GameName { get; init; } = "Nome Desconhecido";

        [Url(ErrorMessage = "A URL da imagem do jogo não é válida.")]
        public string? GameImageUrl { get; init; }

        public GameLibraryStatus Status { get; init; }

        [Range(0, double.MaxValue, ErrorMessage = "O preço pago deve ser um valor positivo.")]
        // `set` (não `init`) — o serviço precisa de o poder anular quando quem pede
        // a biblioteca não é o dono (o preço pago nunca é devolvido a terceiros).
        public decimal? PricePaid { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "O número de partidas jogadas não pode ser negativo.")]
        public int TotalTimesPlayed { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "O total de horas jogadas não pode ser negativo.")]
        public int TotalHoursPlayed { get; init; }

        public DateTime AddedAt { get; init; } = DateTime.UtcNow;

        private DateTime? _lastPlayedAt;

        public DateTime? LastPlayedAt
        {
            get => _lastPlayedAt;
            set
            {
                if (value > DateTime.UtcNow)
                    throw new ArgumentException("A data da última jogada não pode ser no futuro.");
                _lastPlayedAt = value;
            }
        }
    }

    /// <summary>DTO usado para atualizar (PATCH) uma entrada já existente da biblioteca.</summary>
    public class UpdateUserGameLibraryDto
    {
        public GameLibraryStatus Status { get; init; }

        [Range(0, double.MaxValue, ErrorMessage = "O preço pago deve ser um valor positivo.")]
        public decimal? PricePaid { get; init; }
    }

    /// <summary>
    /// Um jogo que o utilizador já jogou (tem pelo menos 1 partida registada),
    /// esteja ou não atualmente na biblioteca.
    /// </summary>
    public class PlayedGameDto
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string? GameImageUrl { get; set; }
        public double? AverageRating { get; set; }
        public int? MinPlayers { get; set; }
        public int? MaxPlayers { get; set; }
        public bool IsExpansion { get; set; }
        public bool IsCooperative { get; set; }
        public bool SupportsSoloMode { get; set; }
        public int TimesPlayed { get; set; }
        public DateTime? LastPlayedAt { get; set; }

        public bool InLibrary { get; set; }
        public GameLibraryStatus? Status { get; set; }
        public decimal? PricePaid { get; set; }
    }
}