namespace MeepleBoard.Application.DTOs
{
    public class GameSessionPlayerDto
    {
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public bool IsOrganizer { get; set; }
        public DateTime JoinedAt { get; set; }
        public DateTime? LeftAt { get; set; }
    }
}
