namespace MeepleBoard.Services.DTOs
{
    public sealed record PublicationDto(bool Rated, double? Rating, bool ShowRating, string? Comment, bool CommentPublished, bool Edited,
        DateTime? PublishedAt, bool Hidden, string? HiddenReason, IReadOnlyList<PublishedPhotoDto>? Photos = null);
    /// <summary>
    /// The author's view of one published photo: Key matches the diary photo (its path's last segment); DiaryPath shows it
    /// (null once moderation removed it from the match). Hidden/HiddenReason say why the community no longer sees it.
    /// </summary>
    public sealed record PublishedPhotoDto(Guid Id, string Key, string? DiaryPath, bool Hidden, string? HiddenReason, bool Removed);
    /// <summary>
    /// PhotoKeys: own diary photos of this match to publish, in order (at most 3); null leaves the photos as they are.
    /// PhotoConsent: the mandatory confirmation (others' images authorised, no personal data shown) when adding any photo.
    /// </summary>
    public sealed record SetPublicationDto(bool ShowRating, string? Comment, bool PublishComment, IReadOnlyList<string>? PhotoKeys = null, bool PhotoConsent = false);
    /// <summary>OwnMatchId is set only on the reader's own experience, so they can manage it; never for anyone else.</summary>
    /// <summary>Photos: public paths (/MeepleBoard/experiences/{Id}/photos/{photoId}), which reveal no match, entry or storage.</summary>
    public sealed record ExperienceDto(Guid Id, string AuthorName, bool IsMine, double? Rating, string? Comment, string Mode, int Players,
        string Month, bool Edited, bool CanReport, Guid? OwnMatchId, IReadOnlyList<string>? Photos = null);
    public sealed record ExperiencePageDto(IReadOnlyList<ExperienceDto> Items, int Total);
    public sealed record ReportDto(string Reason, string? Detail);
    public sealed record ReportedExperienceDto(Guid Id, string AuthorName, string GameName, double? Rating, string? Comment,
        Dictionary<string, int> Reasons, bool Hidden, string? HiddenReason);
}
