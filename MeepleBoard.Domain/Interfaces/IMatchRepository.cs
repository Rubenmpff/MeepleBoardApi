using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IMatchRepository
    {
        Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

        ValueTask<Match?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Match>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Match>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Match>> GetByGameIdAsync(Guid gameId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Match>> GetMatchesByPeriodAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Match>> GetRecentMatchesSinceAsync(int count, DateTime? startDate = null, CancellationToken cancellationToken = default);

        Task<int> GetTotalMatchesByGameAsync(Guid gameId, CancellationToken cancellationToken = default);

        Task AddAsync(Match match, CancellationToken cancellationToken = default);

        Task UpdateAsync(Match match, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, Guid? gameId = null, CancellationToken cancellationToken = default);

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}