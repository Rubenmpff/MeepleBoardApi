using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    /// <summary>
    /// Blocks, reports of people and photos, and their moderation (docs/proposta-bloqueio-comunidade.md in the app).
    /// Nothing here notifies the other person, and no report acts by itself.
    /// </summary>
    public interface ICommunityService
    {
        Task<IReadOnlyList<BlockedPersonDto>> MyBlocksAsync(Guid userId, CancellationToken ct = default);
        Task BlockAsync(Guid userId, Guid targetId, CancellationToken ct = default);
        /// <summary>Blocks the author of a visible publication on a game page (the list never carries the author's id).</summary>
        Task BlockPublicationAuthorAsync(Guid userId, Guid publicationId, CancellationToken ct = default);
        Task UnblockAsync(Guid userId, Guid targetId, CancellationToken ct = default);
        Task ReportPersonAsync(Guid reporterId, Guid targetId, ReportDto dto, CancellationToken ct = default);
        Task ReportPhotoAsync(Guid reporterId, Guid matchId, Guid entryId, string photoKey, ReportDto dto, CancellationToken ct = default);

        Task<IReadOnlyList<ModerationPersonDto>> ModerationPeopleAsync(CancellationToken ct = default);
        Task<IReadOnlyList<ModerationPhotoDto>> ModerationPhotosAsync(CancellationToken ct = default);
        Task ArchivePersonReportsAsync(Guid administratorId, Guid userId, CancellationToken ct = default);
        Task SuspendAsync(Guid administratorId, Guid userId, SuspendDto dto, CancellationToken ct = default);
        Task LiftSuspensionAsync(Guid administratorId, Guid userId, CancellationToken ct = default);
        /// <summary>The stored reference of a photo with an open report (administrators may then read it), or null.</summary>
        Task<string?> ReportedPhotoUrlAsync(Guid entryId, string photoKey, CancellationToken ct = default);
        /// <summary>«keep» resolves the reports; «remove» takes the photo out of the diary entry (a manual decision) and resolves them.</summary>
        Task ModeratePhotoAsync(Guid administratorId, Guid entryId, string photoKey, string decision, CancellationToken ct = default);
    }
}
