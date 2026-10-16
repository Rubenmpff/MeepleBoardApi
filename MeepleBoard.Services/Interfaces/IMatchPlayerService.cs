using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    public interface IMatchPlayerService
    {
        Task<IEnumerable<MatchPlayerDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<int> GetTotalMatchesByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<int> GetTotalWinsByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<double?> GetWinRateByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<int> GetTotalMatchesByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        Task<int> GetTotalWinsByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        /// <summary>Only the match creator may change who played; anyone else gets UnauthorizedAccessException.</summary>
        Task RemovePlayerFromMatchAsync(Guid matchId, Guid playerId, Guid actorId, CancellationToken cancellationToken = default);

        /// <summary>Only the match creator may change who played; anyone else gets UnauthorizedAccessException.</summary>
        Task AddPlayerToMatchAsync(Guid matchId, Guid playerId, Guid actorId, CancellationToken cancellationToken = default);
    }
}