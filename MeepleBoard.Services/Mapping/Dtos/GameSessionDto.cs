using MeepleBoard.Application.DTOs;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Services.DTOs;

public class GameSessionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public Guid OrganizerId { get; set; }
    public string OrganizerUserName { get; set; } = string.Empty;

    /// <summary>Data/hora planeada para iniciar (UTC).</summary>
    public DateTime ScheduledStartDate { get; set; }

    /// <summary>
    /// Data limite para os convidados responderem (UTC).
    /// Se null, usa ScheduledStartDate como limite.
    /// </summary>
    public DateTime? ResponseDeadline { get; set; }

    /// <summary>Data/hora real de criação (UTC).</summary>
    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string? Location { get; set; }

    /// <summary>Upcoming | Active | Closed | Cancelled</summary>
    public string Status { get; set; } = "Upcoming";

    /// <summary>Compatibilidade (true apenas quando Status == Active).</summary>
    public bool IsActive => Status == "Active";

    /// <summary>True se a sessão foi cancelada.</summary>
    public bool IsCancelled => Status == "Cancelled";

    public List<GameSessionPlayerDto> Players { get; set; } = new();
    public List<MatchDto> Matches { get; set; } = new();

    public int PlayerCount => Players?.Count ?? 0;
    public int MatchCount => Matches?.Count ?? 0;

    /// <summary>
    /// Número de aceitações além do organizer.
    /// Usado no frontend para mostrar progresso de confirmações.
    /// </summary>
    public int AcceptedGuestCount => Players?
        .Count(p => !p.IsOrganizer && p.Status == GameSessionInviteStatus.Accepted) ?? 0;

    /// <summary>
    /// Data efectiva de deadline para mostrar no frontend.
    /// ResponseDeadline se definida, senão ScheduledStartDate.
    /// </summary>
    public DateTime EffectiveDeadline => ResponseDeadline ?? ScheduledStartDate;
}