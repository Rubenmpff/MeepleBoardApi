namespace MeepleBoard.Services.DTOs
{
    public sealed record PublicationDto(bool Rated, double? Rating, bool ShowRating, string? Comment, bool CommentPublished, bool Edited,
        DateTime? PublishedAt, bool Hidden, string? HiddenReason);
    public sealed record SetPublicationDto(bool ShowRating, string? Comment, bool PublishComment);
    /// <summary>OwnMatchId is set only on the reader's own experience, so they can manage it; never for anyone else.</summary>
    public sealed record ExperienceDto(Guid Id, string AuthorName, bool IsMine, double? Rating, string? Comment, string Mode, int Players,
        string Month, bool Edited, bool CanReport, Guid? OwnMatchId);
    public sealed record ExperiencePageDto(IReadOnlyList<ExperienceDto> Items, int Total);
    public sealed record ReportDto(string Reason, string? Detail);
    public sealed record ReportedExperienceDto(Guid Id, string AuthorName, string GameName, double? Rating, string? Comment,
        Dictionary<string, int> Reasons, bool Hidden, string? HiddenReason);
}
