namespace MeepleBoard.Services.Mapping.Dtos
{
    /// <summary>A public (approved) resource as the game page shows it.</summary>
    public sealed record GameResourceDto(Guid Id, Guid GameId, string Kind, string? PrintCategory, string? Language, string Title,
        string? Author, string Platform, string Url, string? Compatibility, bool IsOfficial, bool? IsFree, string Status,
        string? Expansion, string? ThumbnailUrl);

    /// <summary>Resources for one language filter, plus what exists in other languages so the app can offer alternatives.</summary>
    public sealed record GameResourceListDto(string Language, IReadOnlyList<GameResourceDto> Items, IReadOnlyList<GameResourceLanguageDto> Languages, int Total);

    /// <summary>Approved resources per language code (null: language-independent, e.g. 3D models) and kind.</summary>
    public sealed record GameResourceLanguageDto(string? Code, int Count, IReadOnlyDictionary<string, int> Kinds);

    /// <summary>Fields a user suggests or an administrator edits.</summary>
    public sealed record GameResourceRequest(string Kind, string? PrintCategory, string? Language, string Title, string? Author,
        string Platform, string Url, string? Compatibility, bool IsOfficial, bool? IsFree, string? Expansion = null, string? ThumbnailUrl = null);

    public sealed record GameResourceReportRequest(string Reason, string? Comment);

    public sealed record GameResourceReviewRequest(string? Note);

    /// <summary>Administration view: review state, origin and open reports.</summary>
    public sealed record GameResourceAdminDto(GameResourceDto Resource, string GameName, string? SuggestedBy, DateTime CreatedAt,
        DateTime? ReviewedAt, string? ReviewNote, int OpenReports);

    public sealed record GameResourceReportDto(Guid Id, Guid ResourceId, string ResourceTitle, string GameName, Guid GameId, string Url,
        string Reason, string? Comment, string ReportedBy, DateTime CreatedAt);
}
