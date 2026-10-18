using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    /// <summary>
    /// Publishing an experience: the participant's own choices for one diary entry, the public list on the game page,
    /// reports and moderation. Never creates or changes a rating and never touches averages.
    /// </summary>
    public interface IExperienceService
    {
        Task<PublicationDto> GetMineAsync(Guid matchId, Guid userId, CancellationToken ct = default);
        Task<PublicationDto> SetAsync(Guid matchId, Guid userId, SetPublicationDto dto, CancellationToken ct = default);
        Task<PublicationDto> ChangeAsync(Guid matchId, Guid userId, string action, CancellationToken ct = default);
        /// <summary>The author withdraws one photo from the community; it stays in the diary, shared with the players.</summary>
        Task<PublicationDto> WithdrawPhotoAsync(Guid matchId, Guid userId, Guid photoId, CancellationToken ct = default);
        /// <summary>The stored reference of a public photo this viewer may see right now, or null (the route answers 404).</summary>
        Task<string?> PublicPhotoUrlAsync(Guid publicationId, Guid photoId, Guid viewerId, CancellationToken ct = default);
        Task<ExperiencePageDto> ListForGameAsync(Guid gameId, Guid viewerId, int offset, int limit, CancellationToken ct = default);
        Task ReportAsync(Guid publicationId, Guid reporterId, ReportDto dto, CancellationToken ct = default);
        Task<IReadOnlyList<ReportedExperienceDto>> ListReportedAsync(CancellationToken ct = default);
        Task ModerateAsync(Guid publicationId, Guid administratorId, string action, string? reason, CancellationToken ct = default);
    }
}
