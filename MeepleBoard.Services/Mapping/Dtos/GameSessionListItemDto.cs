namespace MeepleBoard.Services.Mapping.Dtos
{
    public class GameSessionListItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public Guid OrganizerId { get; set; }
        public string OrganizerUserName { get; set; } = string.Empty;

        public bool IsActive { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Location { get; set; }

        public int PlayerCount { get; set; }
        public int MatchCount { get; set; }
    }

}
