namespace MeepleBoard.Application.DTOs
{
    public class CreateGameSessionDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Location { get; set; }
    }
}
