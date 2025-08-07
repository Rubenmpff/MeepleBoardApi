namespace MeepleBoard.Application.DTOs
{
    public class CreateGameSessionDto
    {
        public string Name { get; set; } = string.Empty;
        public Guid OrganizerId { get; set; }
        public string? Location { get; set; }
    }
}
