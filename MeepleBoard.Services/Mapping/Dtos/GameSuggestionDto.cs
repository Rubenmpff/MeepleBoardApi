namespace MeepleBoard.Services.Mapping.Dtos
{
    /// <summary>
    /// Sugestão de jogo (pode vir do BGG ou da base local)
    /// </summary>
    public class GameSuggestionDto
    {
        /// <summary>
        /// Id interno (GUID) se existir na base local; caso contrário, null
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Id do jogo no BoardGameGeek (sempre presente)
        /// </summary>
        public int BggId { get; set; }

        /// <summary>
        /// Nome do jogo
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Ano de publicação (opcional)
        /// </summary>
        public int? YearPublished { get; set; }

        /// <summary>
        /// Imagem do jogo (pode ser null)
        /// </summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// Indica se é uma expansão
        /// </summary>
        public bool IsExpansion { get; set; }
    }
}
