using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    /// <summary>Blocks between people, reports of people and photos, and what moderation reads (docs/proposta-bloqueio-comunidade.md).</summary>
    public interface ICommunityRepository
    {
        /// <summary>A block either way between the two.</summary>
        Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken ct = default);
        /// <summary>A block either way between the person and any of the others.</summary>
        Task<bool> AnyBlockWithAsync(Guid user, IEnumerable<Guid> others, CancellationToken ct = default);
        /// <summary>A block either way between any two of these people.</summary>
        Task<bool> AnyBlockAmongAsync(IEnumerable<Guid> users, CancellationToken ct = default);
        /// <summary>Everyone with a block either way with the person (to leave them out of lists).</summary>
        Task<IReadOnlyList<Guid>> BlockedEitherWayAsync(Guid user, CancellationToken ct = default);
        Task<IReadOnlyList<BlockedPerson>> MyBlocksAsync(Guid blocker, CancellationToken ct = default);
        /// <summary>Adds the person's own block; the friendship and pending requests between the two go. False if it already existed.</summary>
        Task<bool> BlockAsync(Guid blocker, Guid blocked, CancellationToken ct = default);
        /// <summary>Removes only the person's own block (a block by the other person stays). False if there was none.</summary>
        Task<bool> UnblockAsync(Guid blocker, Guid blocked, CancellationToken ct = default);

        Task AddUserReportAsync(UserReport report, CancellationToken ct = default);
        Task AddPhotoReportAsync(PhotoReport report, CancellationToken ct = default);
        Task<bool> HasOpenUserReportAsync(Guid reporterId, Guid reportedUserId, CancellationToken ct = default);
        Task<bool> HasOpenPhotoReportAsync(Guid reporterId, Guid journalEntryId, string photoUrl, CancellationToken ct = default);
        Task<bool> PhotoHasOpenReportAsync(Guid journalEntryId, string photoUrl, CancellationToken ct = default);
        Task<IReadOnlyList<ReportedPerson>> ReportedPeopleAsync(CancellationToken ct = default);
        Task<IReadOnlyList<ReportedPhoto>> ReportedPhotosAsync(CancellationToken ct = default);
        /// <summary>Accounts suspended right now (so a suspension can be lifted once its reports are resolved).</summary>
        Task<IReadOnlyList<SuspendedPerson>> SuspendedPeopleAsync(CancellationToken ct = default);
        Task<int> ResolveUserReportsAsync(Guid reportedUserId, Guid administratorId, string outcome, CancellationToken ct = default);
        Task<int> ResolvePhotoReportsAsync(Guid journalEntryId, string photoUrl, Guid administratorId, string outcome, CancellationToken ct = default);
        /// <summary>A diary entry by id (tracked), with its match players, for photo reports and moderation.</summary>
        Task<MatchJournalEntry?> GetJournalEntryAsync(Guid journalEntryId, CancellationToken ct = default);
        /// <summary>Suspends and ends every session: the refresh tokens go and earlier access tokens are refused. False if the account is gone.</summary>
        Task<bool> SuspendAsync(Guid userId, DateTime? untilUtc, string reason, Guid administratorId, CancellationToken ct = default);
        /// <summary>Lifts it. False if the account is gone or not suspended.</summary>
        Task<bool> LiftSuspensionAsync(Guid userId, Guid administratorId, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);
    }

    public sealed record BlockedPerson(Guid Id, string UserName, DateTime BlockedAt);
    /// <summary>Open reports about one person, with their current suspension (administrators only).</summary>
    public sealed record ReportedPerson(Guid UserId, string UserName, IReadOnlyDictionary<string, int> Reasons, IReadOnlyList<string> Details, DateTime LastReportAt,
        DateTime? SuspendedAt, DateTime? SuspendedUntil, string? SuspensionReason);
    public sealed record SuspendedPerson(Guid UserId, string UserName, DateTime SuspendedAt, DateTime? SuspendedUntil, string? SuspensionReason);
    /// <summary>Open reports about one photo: the diary entry, its match and owner, and the stored reference.</summary>
    public sealed record ReportedPhoto(Guid JournalEntryId, Guid MatchId, Guid OwnerId, string OwnerName, string PhotoUrl, IReadOnlyDictionary<string, int> Reasons,
        IReadOnlyList<string> Details, DateTime LastReportAt);
}
