// MeepleBoard.Application/DTOs/GameSessionDto.cs
using MeepleBoard.Application.DTOs;
using MeepleBoard.Services.DTOs;

public class GameSessionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public Guid OrganizerId { get; set; }
    public string OrganizerUserName { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Location { get; set; }

    public List<GameSessionPlayerDto> Players { get; set; } = new();
    public List<MatchDto> Matches { get; set; } = new();

    public int PlayerCount => Players?.Count ?? 0;
    public int MatchCount => Matches?.Count ?? 0;
}
