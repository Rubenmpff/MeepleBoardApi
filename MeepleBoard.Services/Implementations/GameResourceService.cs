using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Catalogue of external links per game. Only approved resources are public;
    /// suggestions wait for an administrator. Authorization (administrator role) is enforced by the controllers.
    /// </summary>
    public class GameResourceService : IGameResourceService
    {
        private readonly IGameResourceRepository _resources;
        private readonly IGameRepository _games;

        public GameResourceService(IGameResourceRepository resources, IGameRepository games)
        {
            _resources = resources;
            _games = games;
        }

        public async Task<GameResourceListDto> ListAsync(Guid gameId, string language, CancellationToken ct = default)
        {
            var code = Normalize(language);
            var approved = await _resources.GetApprovedByGameAsync(gameId, ct);
            var items = approved.Where(r => code == "all" || r.Language == null || r.Language == code)
                // Base game first, then each expansion; within them, the publisher's own first.
                .OrderBy(r => r.Expansion != null).ThenBy(r => r.Expansion)
                .ThenByDescending(r => r.IsOfficial).ThenBy(r => Array.IndexOf(GameResource.Kinds, r.Kind))
                .ThenBy(r => r.Language == code ? 0 : r.Language == null ? 1 : 2).ThenBy(r => r.Title)
                .Select(ToDto).ToList();
            var languages = approved.GroupBy(r => r.Language).OrderBy(g => g.Key == null).ThenByDescending(g => g.Count())
                .Select(g => new GameResourceLanguageDto(g.Key, g.Count(), g.GroupBy(r => r.Kind).ToDictionary(k => k.Key, k => k.Count())))
                .ToList();
            return new GameResourceListDto(code, items, languages, approved.Count);
        }

        public async Task<GameResourceDto> SuggestAsync(Guid gameId, Guid userId, GameResourceRequest request, CancellationToken ct = default)
        {
            var resource = await NewAsync(gameId, userId, request, byAdministrator: false, ct);
            return ToDto(resource);
        }

        public async Task ReportAsync(Guid resourceId, Guid userId, GameResourceReportRequest request, CancellationToken ct = default)
        {
            var resource = await _resources.GetForUpdateAsync(resourceId, ct);
            if (resource == null || resource.Status != GameResource.Approved) throw new KeyNotFoundException("Recurso não encontrado.");
            if (await _resources.HasOpenReportAsync(resourceId, userId, ct)) throw new InvalidOperationException("Já reportaste um problema neste recurso; está a ser analisado.");
            await _resources.AddReportAsync(new GameResourceReport(resourceId, userId, request.Reason, request.Comment), ct);
            await _resources.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<GameResourceAdminDto>> ReviewListAsync(string? status, Guid? gameId, CancellationToken ct = default)
        {
            if (status != null && status is not (GameResource.Pending or GameResource.Approved or GameResource.Rejected)) throw new ArgumentException("Estado inválido.");
            var list = await _resources.GetForReviewAsync(status, gameId, ct);
            return list.Select(r => new GameResourceAdminDto(ToDto(r), r.Game.Name, r.SuggestedBy?.UserName, r.CreatedAt, r.ReviewedAt, r.ReviewNote,
                r.Reports.Count(x => x.ResolvedAt == null))).ToList();
        }

        public async Task<GameResourceDto> CreateAsync(Guid gameId, Guid administratorId, GameResourceRequest request, CancellationToken ct = default) =>
            ToDto(await NewAsync(gameId, administratorId, request, byAdministrator: true, ct));

        public async Task<GameResourceDto> UpdateAsync(Guid id, GameResourceRequest request, CancellationToken ct = default)
        {
            var resource = await Find(id, ct);
            var details = Details(request);
            if (await _resources.UrlExistsAsync(resource.GameId, GameResource.ValidUrl(details.Url), id, ct)) throw new InvalidOperationException("Este endereço já existe para este jogo.");
            resource.Update(details);
            await _resources.SaveChangesAsync(ct);
            return ToDto(resource);
        }

        public async Task<GameResourceDto> ApproveAsync(Guid id, Guid administratorId, CancellationToken ct = default)
        {
            var resource = await Find(id, ct);
            resource.Approve(administratorId);
            await _resources.SaveChangesAsync(ct);
            return ToDto(resource);
        }

        public async Task<GameResourceDto> RejectAsync(Guid id, Guid administratorId, string? note, CancellationToken ct = default)
        {
            var resource = await Find(id, ct);
            resource.Reject(administratorId, note);
            await _resources.SaveChangesAsync(ct);
            return ToDto(resource);
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _resources.Remove(await Find(id, ct));
            await _resources.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<GameResourceReportDto>> OpenReportsAsync(CancellationToken ct = default) =>
            (await _resources.GetOpenReportsAsync(ct)).Select(r => new GameResourceReportDto(r.Id, r.ResourceId, r.Resource.Title, r.Resource.Game.Name,
                r.Resource.GameId, r.Resource.Url, r.Reason, r.Comment, r.User.UserName ?? "", r.CreatedAt)).ToList();

        public async Task ResolveReportAsync(Guid reportId, Guid administratorId, CancellationToken ct = default)
        {
            var report = await _resources.GetReportForUpdateAsync(reportId, ct) ?? throw new KeyNotFoundException("Relato não encontrado.");
            report.Resolve(administratorId);
            await _resources.SaveChangesAsync(ct);
        }

        private async Task<GameResource> NewAsync(Guid gameId, Guid authorId, GameResourceRequest request, bool byAdministrator, CancellationToken ct)
        {
            if (await _games.GetByIdAsync(gameId, ct) == null) throw new KeyNotFoundException("Jogo não encontrado.");
            var resource = new GameResource(gameId, Details(request), authorId, byAdministrator);
            if (await _resources.UrlExistsAsync(gameId, resource.Url, null, ct)) throw new InvalidOperationException("Este endereço já existe ou está a aguardar aprovação para este jogo.");
            await _resources.AddAsync(resource, ct);
            await _resources.SaveChangesAsync(ct);
            return resource;
        }

        private async Task<GameResource> Find(Guid id, CancellationToken ct) =>
            await _resources.GetForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Recurso não encontrado.");

        private static GameResourceDetails Details(GameResourceRequest r) =>
            new(r.Kind, r.PrintCategory, r.Language, r.Title, r.Author, r.Platform, r.Url, r.Compatibility, r.IsOfficial, r.IsFree, r.Expansion, r.ThumbnailUrl);

        private static string Normalize(string? language)
        {
            var code = string.IsNullOrWhiteSpace(language) ? "all" : language.Trim().ToLowerInvariant();
            if (code != "all" && (code.Length != 2 || !code.All(char.IsAsciiLetterLower))) throw new ArgumentException("Idioma inválido.");
            return code;
        }

        private static GameResourceDto ToDto(GameResource r) => new(r.Id, r.GameId, r.Kind, r.PrintCategory, r.Language, r.Title, r.Author,
            r.Platform, r.Url, r.Compatibility, r.IsOfficial, r.IsFree, r.Status, r.Expansion, r.ThumbnailUrl);
    }
}
