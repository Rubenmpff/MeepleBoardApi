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
        public Guid Id { get; set; }
        [Required(ErrorMessage = "O nome do jogo é obrigatório.")]
        [MaxLength(150, ErrorMessage = "O nome do jogo deve ter no máximo 150 caracteres.")]
        public string Name { get; set; } = string.Empty;
        [Required(ErrorMessage = "A descrição do jogo é obrigatória.")]
        [MaxLength(1000, ErrorMessage = "A descrição deve ter no máximo 1000 caracteres.")]
        public string Description { get; set; } = string.Empty;
        [Required(ErrorMessage = "A imagem é obrigatória.")]
        [Url(ErrorMessage = "A URL da imagem não é válida.")]
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public bool SupportsSoloMode { get; set; }
        [Range(0, int.MaxValue, ErrorMessage = "O total de partidas não pode ser negativo.")]
        public int TotalMatches { get; set; }
        [Range(0, 100, ErrorMessage = "O score deve estar entre 0 e 100.")]
        public int? MeepleBoardScore { get; set; }
        /// <summary>
        /// Só preenchido na resposta do ranking PESSOAL (/game/rankings/mine) —
        /// a média das TUAS avaliações deste jogo, não misturada com a dos outros.
        /// </summary>
        public double? PersonalAverageRating { get; set; }
        public List<MatchDto> Matches { get; set; } = new();
        public List<GameDto> Expansions { get; set; } = new();
        public bool IsExpansion { get; set; }
        public Guid? BaseGameId { get; set; }
        // 🌍 Dados importados do BGG
        [Range(1, int.MaxValue, ErrorMessage = "O ID do BGG deve ser um número positivo.")]
        public int? BggId { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "O ranking deve ser positivo.")]
        public int? BggRanking { get; set; }
        [Range(0, 10, ErrorMessage = "A nota deve estar entre 0 e 10.")]
        public double? AverageRating { get; set; }
        /// <summary>
        /// Número de pessoas que avaliaram o jogo no BGG — usado como medida de
        /// "quão conhecido" o jogo é, melhor do que a nota média sozinha.
        /// </summary>
        [Range(0, int.MaxValue, ErrorMessage = "O número de avaliações não pode ser negativo.")]
        public int? UsersRatedCount { get; set; }
        [Range(1000, 2100, ErrorMessage = "Ano de publicação inválido.")]
        public int? YearPublished { get; set; }
        public int? BaseGameBggId { get; set; }
        // ─── Dados de jogadores ───────────────────────────────────────────────
        /// <summary>
        /// Número mínimo de jogadores.
        /// </summary>
        public int? MinPlayers { get; set; }
        /// <summary>
        /// Número máximo de jogadores.
        /// </summary>
        public int? MaxPlayers { get; set; }
        /// <summary>
        /// Complexidade média do jogo (weight do BGG, 1-5).
        /// </summary>
        public double? AverageWeight { get; set; }
        /// <summary>
        /// Indica se o jogo é cooperativo.
        /// Determinado pela mecânica "Cooperative Game" no BGG.
        /// </summary>
        public bool IsCooperative { get; set; }
        /// <summary>Detetado no BGG (mecânica Legacy/Campaign, ou categoria "Campaign Games").</summary>
        public bool SupportsCampaign { get; set; }
        // ─────────────────────────────────────────────────────────────────────
        public List<string> Categories { get; set; } = new();
    }
}