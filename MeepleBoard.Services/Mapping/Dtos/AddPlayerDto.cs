namespace MeepleBoard.Services.DTOs
{
    public class AddPlayerDto
    {
        public Guid UserId { get; set; }
        public bool IsOrganizer { get; set; } = false;
    }
}
