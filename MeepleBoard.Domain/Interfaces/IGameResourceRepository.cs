using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    /// <summary>Game resources (links) and their problem reports.</summary>
    public interface IGameResourceRepository
    {
        Task<IReadOnlyList<GameResource>> GetApprovedByGameAsync(Guid gameId, CancellationToken ct = default);
        Task<IReadOnlyList<GameResource>> GetForReviewAsync(string? status, Guid? gameId, CancellationToken ct = default);
        Task<GameResource?> GetForUpdateAsync(Guid id, CancellationToken ct = default);
        Task<bool> UrlExistsAsync(Guid gameId, string url, Guid? exceptId = null, CancellationToken ct = default);
        Task<bool> HasOpenReportAsync(Guid resourceId, Guid userId, CancellationToken ct = default);
        Task<IReadOnlyList<GameResourceReport>> GetOpenReportsAsync(CancellationToken ct = default);
        Task<GameResourceReport?> GetReportForUpdateAsync(Guid id, CancellationToken ct = default);
        Task AddAsync(GameResource resource, CancellationToken ct = default);
        Task AddReportAsync(GameResourceReport report, CancellationToken ct = default);
        void Remove(GameResource resource);
        Task SaveChangesAsync(CancellationToken ct = default);
    }
}
