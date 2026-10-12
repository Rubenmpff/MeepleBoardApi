using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class GameResourceRepository : IGameResourceRepository
    {
        private readonly MeepleBoardDbContext _context;

        public GameResourceRepository(MeepleBoardDbContext context) => _context = context;

        public async Task<IReadOnlyList<GameResource>> GetApprovedByGameAsync(Guid gameId, CancellationToken ct = default) =>
            await _context.GameResources.AsNoTracking()
                .Where(r => r.GameId == gameId && r.Status == GameResource.Approved)
                .ToListAsync(ct);

        public async Task<IReadOnlyList<GameResource>> GetForReviewAsync(string? status, Guid? gameId, CancellationToken ct = default) =>
            await _context.GameResources.AsNoTracking()
                .Include(r => r.Game).Include(r => r.SuggestedBy).Include(r => r.Reports)
                .Where(r => (status == null || r.Status == status) && (gameId == null || r.GameId == gameId))
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);

        public Task<GameResource?> GetForUpdateAsync(Guid id, CancellationToken ct = default) =>
            _context.GameResources.Include(r => r.Game).Include(r => r.SuggestedBy).Include(r => r.Reports).FirstOrDefaultAsync(r => r.Id == id, ct);

        public Task<bool> UrlExistsAsync(Guid gameId, string url, Guid? exceptId = null, CancellationToken ct = default) =>
            _context.GameResources.AnyAsync(r => r.GameId == gameId && r.Url == url && r.Status != GameResource.Rejected && r.Id != exceptId, ct);

        public Task<bool> HasOpenReportAsync(Guid resourceId, Guid userId, CancellationToken ct = default) =>
            _context.GameResourceReports.AnyAsync(r => r.ResourceId == resourceId && r.UserId == userId && r.ResolvedAt == null, ct);

        public async Task<IReadOnlyList<GameResourceReport>> GetOpenReportsAsync(CancellationToken ct = default) =>
            await _context.GameResourceReports.AsNoTracking()
                .Include(r => r.Resource).ThenInclude(r => r.Game).Include(r => r.User)
                .Where(r => r.ResolvedAt == null)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct);

        public Task<GameResourceReport?> GetReportForUpdateAsync(Guid id, CancellationToken ct = default) =>
            _context.GameResourceReports.FirstOrDefaultAsync(r => r.Id == id, ct);

        public async Task AddAsync(GameResource resource, CancellationToken ct = default) => await _context.GameResources.AddAsync(resource, ct);

        public async Task AddReportAsync(GameResourceReport report, CancellationToken ct = default) => await _context.GameResourceReports.AddAsync(report, ct);

        public void Remove(GameResource resource) => _context.GameResources.Remove(resource);

        public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
    }
}
