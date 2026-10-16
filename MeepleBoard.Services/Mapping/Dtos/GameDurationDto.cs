namespace MeepleBoard.Services.DTOs
{
    /// <summary>
    /// Indicators of one group. Community groups below the minimum protection carry only Available = false.
    /// Community values are rounded to 5 minutes and very distant values are only counted.
    /// </summary>
    public sealed record DurationStatsDto(
        bool Available, int Count = 0, int Measured = 0, int Estimated = 0, int NotStated = 0,
        int? Mean = null, int? Median = null, int? Q1 = null, int? Q3 = null, int Distant = 0, List<int>? DistantValues = null,
        int? ExcludedUnknown = null, int? ExcludedMissing = null, int? ExcludedZero = null)
    {
        public static readonly DurationStatsDto Insufficient = new(false);
    }

    public sealed record DurationBucketDto(int Players, DurationStatsDto Stats);
    public sealed record DurationSideDto(DurationStatsDto Solo, DurationStatsDto Multiplayer, List<DurationBucketDto> ByPlayers);
    public sealed record GameDurationsDto(DurationSideDto Personal, DurationSideDto Community)
    {
        /// <summary>The viewer's own preference, shown in «Ver detalhes».</summary>
        public bool ViewerContributes { get; init; }
    }
}
