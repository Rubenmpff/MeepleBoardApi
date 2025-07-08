using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    public interface IMatchPlayerService
    {
        Task<IEnumerable<MatchPlayerDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<int> GetTotalMatchesByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<int> GetTotalWinsByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<double> GetWinRateByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<int> GetTotalMatchesByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        Task<int> GetTotalWinsByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        Task RemovePlayerFromMatchAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken = default);

        Task AddPlayerToMatchAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken = default);
    }
}