using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    /// <summary>
    /// DTO que representa os dados completos de um jogo na MeepleBoard.
    /// Inclui dados locais e dados importados do BGG.
    /// </summary>
    public class GameDto
    {
        // 📌 Dados Locais

        /// <summary> Identificador interno (GUID). </summary>
        public Guid Id { get; set; }

        /// <summary> Nome principal do jogo. </summary>
        [Required(ErrorMessage = "O nome do jogo é obrigatório.")]
        [MaxLength(150, ErrorMessage = "O nome do jogo deve ter no máximo 150 caracteres.")]
        public string Name { get; set; } = string.Empty;

        /// <summary> Descrição do jogo. </summary>
        [Required(ErrorMessage = "A descrição do jogo é obrigatória.")]
        [MaxLength(1000, ErrorMessage = "A descrição deve ter no máximo 1000 caracteres.")]
        public string Description { get; set; } = string.Empty;

        /// <summary> URL da imagem de capa. </summary>
        [Required(ErrorMessage = "A imagem é obrigatória.")]
        [Url(ErrorMessage = "A URL da imagem não é válida.")]
        public string ImageUrl { get; set; } = string.Empty;

        /// <summary> Indica se o jogo foi aprovado por admin. </summary>
        public bool IsApproved { get; set; }

        /// <summary> Indica se o jogo possui modo solo. </summary>
        public bool SupportsSoloMode { get; set; }

        /// <summary> Número total de partidas jogadas localmente. </summary>
        [Range(0, int.MaxValue, ErrorMessage = "O total de partidas não pode ser negativo.")]
        public int TotalMatches { get; set; }

        /// <summary> Score calculado pela MeepleBoard (0–100). </summary>
        [Range(0, 100, ErrorMessage = "O score deve estar entre 0 e 100.")]
        public int? MeepleBoardScore { get; set; }

        /// <summary> Lista de partidas registradas com esse jogo. </summary>
        public List<MatchDto> Matches { get; set; } = new();

        /// <summary> Lista de expansões relacionadas a este jogo. </summary>
        public List<GameDto> Expansions { get; set; } = new();

        /// <summary> Indica se o jogo é uma expansão. </summary>
        public bool IsExpansion { get; set; }

        /// <summary> ID do jogo base (local) caso seja expansão. </summary>
        public Guid? BaseGameId { get; set; }

        // 🌍 Dados importados do BoardGameGeek (BGG)

        /// <summary> ID do jogo no BoardGameGeek (opcional). </summary>
        [Range(1, int.MaxValue, ErrorMessage = "O ID do BGG deve ser um número positivo.")]
        public int? BggId { get; set; }

        /// <summary> Ranking atual no BGG. </summary>
        [Range(1, int.MaxValue, ErrorMessage = "O ranking deve ser positivo.")]
        public int? BggRanking { get; set; }

        /// <summary> Média de avaliação no BGG. </summary>
        [Range(0, 10, ErrorMessage = "A nota deve estar entre 0 e 10.")]
        public double? AverageRating { get; set; }

        /// <summary> Ano de publicação original no BGG. </summary>
        [Range(1000, 2100, ErrorMessage = "Ano de publicação inválido.")]
        public int? YearPublished { get; set; }

        /// <summary> ID do jogo base no BGG, caso seja expansão. </summary>
        public int? BaseGameBggId { get; set; }

        // 🆕 Novos dados opcionais importados do BGG
        public int? MinPlayers { get; set; }

        public int? MaxPlayers { get; set; }
        public double? AverageWeight { get; set; }

        /// <summary> Lista de categorias do jogo (ex: "Economic", "Animals", etc). </summary>
        public List<string> Categories { get; set; } = new();
    }
}