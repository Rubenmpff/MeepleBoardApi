using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    /// <summary>Experience publications of diary entries and the reports about them.</summary>
    public interface IExperienceRepository
    {
        Task<MatchExperiencePublication?> GetByEntryForUpdateAsync(Guid journalEntryId, CancellationToken ct = default);
        /// <summary>With its diary entry and every report, tracked.</summary>
        Task<MatchExperiencePublication?> GetWithReportsForUpdateAsync(Guid publicationId, CancellationToken ct = default);
        /// <summary>Publications of this person with a rating or comment published since the given moment.</summary>
        Task<int> CountPublishedSinceAsync(Guid userId, DateTime since, CancellationToken ct = default);
        /// <summary>Visible experiences of a game, newest first; leaves out what the viewer has reported and not yet seen resolved.</summary>
        Task<(IReadOnlyList<ExperienceListProjection> Items, int Total)> ListVisibleForGameAsync(Guid gameId, Guid viewerId, int offset, int limit, CancellationToken ct = default);
        /// <summary>Publications with open reports, or hidden automatically by reports.</summary>
        Task<IReadOnlyList<ReportedExperienceProjection>> ListReportedAsync(CancellationToken ct = default);
        Task AddAsync(MatchExperiencePublication publication, CancellationToken ct = default);
        Task AddReportAsync(ExperienceReport report, CancellationToken ct = default);
        void Remove(MatchExperiencePublication publication);
        Task SaveChangesAsync(CancellationToken ct = default);
    }

    public sealed record ExperienceListProjection(Guid Id, string? AuthorName, bool IsMine, double? Rating, string? Comment, bool Edited,
        Guid MatchId, string? GameMode, bool IsSoloGame, int Players, DateTime MatchDate);

    public sealed record ReportedExperienceProjection(Guid Id, string? AuthorName, string GameName, double? Rating, string? Comment,
        DateTime? HiddenAt, string? HiddenReason, IReadOnlyList<string> OpenReasons);
}
