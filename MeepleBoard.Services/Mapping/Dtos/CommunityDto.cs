namespace MeepleBoard.Services.DTOs
{
    public sealed record BlockedPersonDto(Guid Id, string UserName, DateTime BlockedAt);
    /// <summary>Administrators only. Reasons are counted; details are the optional texts of the open reports.</summary>
    public sealed record ModerationPersonDto(Guid UserId, string UserName, Dictionary<string, int> Reasons, IReadOnlyList<string> Details,
        DateTime? LastReportAt, bool Suspended, DateTime? SuspendedAt, DateTime? SuspendedUntil, string? SuspensionReason);
    /// <summary>Administrators only. ImagePath opens the photo while a report is open; Present is false once its owner removed it.</summary>
    public sealed record ModerationPhotoDto(Guid JournalEntryId, Guid MatchId, string PhotoKey, string ImagePath, string OwnerName,
        Dictionary<string, int> Reasons, IReadOnlyList<string> Details, DateTime LastReportAt, bool Present, bool Published = false, bool AutoHidden = false);
    /// <summary>Days: 7 or 30; null suspends until review.</summary>
    public sealed record SuspendDto(int? Days, string Reason);
}
