namespace MeepleBoard.Services.Mapping.Dtos
{
    public class GameSuggestionDto
    {
        public int BggId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? YearPublished { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsExpansion { get; set; }
    }
}