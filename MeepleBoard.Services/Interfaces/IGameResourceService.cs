using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    public interface IGameResourceService
    {
        /// <summary>Approved resources; language "all", or a code (language-independent resources are always included).</summary>
        Task<GameResourceListDto> ListAsync(Guid gameId, string language, CancellationToken ct = default);
        Task<GameResourceDto> SuggestAsync(Guid gameId, Guid userId, GameResourceRequest request, CancellationToken ct = default);
        Task ReportAsync(Guid resourceId, Guid userId, GameResourceReportRequest request, CancellationToken ct = default);

        Task<IReadOnlyList<GameResourceAdminDto>> ReviewListAsync(string? status, Guid? gameId, CancellationToken ct = default);
        Task<GameResourceDto> CreateAsync(Guid gameId, Guid administratorId, GameResourceRequest request, CancellationToken ct = default);
        Task<GameResourceDto> UpdateAsync(Guid id, GameResourceRequest request, CancellationToken ct = default);
        Task<GameResourceDto> ApproveAsync(Guid id, Guid administratorId, CancellationToken ct = default);
        Task<GameResourceDto> RejectAsync(Guid id, Guid administratorId, string? note, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<GameResourceReportDto>> OpenReportsAsync(CancellationToken ct = default);
        Task ResolveReportAsync(Guid reportId, Guid administratorId, CancellationToken ct = default);
    }
}
