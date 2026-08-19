namespace MeepleBoard.Services.Mapping.Dtos
{
    /// <summary>
    /// Sugestão de jogo (pode vir do BGG ou da base local).
    /// Inclui dados suficientes para o frontend determinar
    /// os modos de jogo disponíveis (Solo, Multiplayer, Cooperativo).
    /// </summary>
    public class GameSuggestionDto
    {
        /// <summary>
        /// Id interno (GUID) se existir na base local; caso contrário, null.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Id do jogo no BoardGameGeek (sempre presente).
        /// </summary>
        public int BggId { get; set; }

        /// <summary>
        /// Nome do jogo.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Ano de publicação (opcional).
        /// </summary>
        public int? YearPublished { get; set; }

        /// <summary>
        /// Imagem do jogo (pode ser null).
        /// </summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// Indica se é uma expansão.
        /// </summary>
        public bool IsExpansion { get; set; }

        // ─── Modos de jogo ────────────────────────────────────────────────────

        /// <summary>
        /// Número mínimo de jogadores.
        /// </summary>
        public int? MinPlayers { get; set; }

        /// <summary>
        /// Número máximo de jogadores.
        /// </summary>
        public int? MaxPlayers { get; set; }

        /// <summary>
        /// Indica se o jogo suporta modo solo (MinPlayers == 1).
        /// </summary>
        public bool SupportsSoloMode { get; set; }

        /// <summary>
        /// Indica se o jogo é cooperativo.
        /// Determinado pela mecânica "Cooperative Game" no BGG.
        /// </summary>
        public bool IsCooperative { get; set; }

        /// <summary>Detetado no BGG (mecânica Legacy/Campaign, ou categoria "Campaign Games").</summary>
        public bool SupportsCampaign { get; set; }

        /// <summary>
        /// Nota média do BGG — já vem no mesmo pedido (stats=1), sem custo extra.
        /// </summary>
        public double? AverageRating { get; set; }

        /// <summary>
        /// Nota da comunidade MeepleBoard (0-100) — só existe se o jogo já estiver
        /// na tua base de dados local, com pelo menos 1 avaliação de um jogador.
        /// </summary>
        public int? MeepleBoardScore { get; set; }

        /// <summary>
        /// Número de pessoas que avaliaram o jogo no BGG — medida de "quão
        /// conhecido" o jogo é, melhor do que só a nota média.
        /// </summary>
        public int? RatingsCount { get; set; }
    }
}