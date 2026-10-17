using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    /// <summary>One person's statistics: summary, supporting matches, company, exploration and the year.</summary>
    public interface IStatisticsService
    {
        Task<StatisticsSummary> Summary(Guid userId, StatisticsQuery query, CancellationToken ct);
        Task<StatisticsMatchPage> Matches(Guid userId, StatisticsMatchesQuery query, CancellationToken ct);
        Task<CompanyReport> Company(Guid userId, StatisticsQuery query, Guid? friendId, CancellationToken ct, string groupContext = "all");
        Task<ExploreReport> Explore(Guid userId, StatisticsQuery query, CancellationToken ct);
        Task<YearReport> Year(Guid userId, int year, string timeZone, Guid? gameId, string? mode, CancellationToken ct);
    }
}
