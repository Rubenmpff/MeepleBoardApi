namespace MeepleBoard.Application.DTOs
{
    public class GameSessionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Organizer { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Location { get; set; }

        public List<GameSessionPlayerDto> Players { get; set; } = new();
    }
}
