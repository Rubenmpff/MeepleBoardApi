using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IMatchPlayerRepository
    {
        Task<bool> ExistsAsync(Guid userId, Guid matchId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<MatchPlayer>> GetByUserIdAsync(Guid userId, bool includeMatch = false, CancellationToken cancellationToken = default);

        Task<int> GetTotalMatchesByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<int> GetTotalWinsByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<double> GetWinRateByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<int> GetTotalMatchesByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        Task<int> GetTotalWinsByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        Task<IReadOnlyDictionary<string, int>> GetMostPlayedGamesByUserAsync(Guid userId, int topN, CancellationToken cancellationToken = default);

        Task<MatchPlayer?> GetByMatchAndPlayerAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken = default);

        Task AddAsync(MatchPlayer matchPlayer, CancellationToken cancellationToken = default);

        Task DeleteAsync(MatchPlayer matchPlayer, CancellationToken cancellationToken = default);

        Task<int> CommitAsync(CancellationToken cancellationToken = default);
    }
}