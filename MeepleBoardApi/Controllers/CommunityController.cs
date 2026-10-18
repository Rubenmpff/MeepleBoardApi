using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    /// <summary>
    /// Blocks, reports of people and diary photos, and their moderation (docs/proposta-bloqueio-comunidade.md in the app).
    /// The other person is never told about a block or a report.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("MeepleBoard")]
    public class CommunityController(ICommunityService community, IPhotoStorageService photos) : ControllerBase
    {
        async Task<ActionResult> Run(Func<Task<ActionResult>> action)
        {
            try { return await action(); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("community/blocks")]
        public Task<ActionResult> Blocks(CancellationToken ct) => Run(async () => Ok(await community.MyBlocksAsync(User.GetUserId(), ct)));

        [HttpPost("community/blocks/{userId:guid}")]
        public Task<ActionResult> Block(Guid userId, CancellationToken ct) => Run(async () => { await community.BlockAsync(User.GetUserId(), userId, ct); return NoContent(); });

        [HttpPost("community/blocks/experiences/{publicationId:guid}")]
        public Task<ActionResult> BlockAuthor(Guid publicationId, CancellationToken ct) => Run(async () => { await community.BlockPublicationAuthorAsync(User.GetUserId(), publicationId, ct); return NoContent(); });

        [HttpDelete("community/blocks/{userId:guid}")]
        public Task<ActionResult> Unblock(Guid userId, CancellationToken ct) => Run(async () => { await community.UnblockAsync(User.GetUserId(), userId, ct); return NoContent(); });

        [HttpPost("community/people/{userId:guid}/reports")]
        public Task<ActionResult> ReportPerson(Guid userId, [FromBody] ReportDto dto, CancellationToken ct)
            => Run(async () => { await community.ReportPersonAsync(User.GetUserId(), userId, dto ?? throw new ArgumentException("Os dados são obrigatórios."), ct); return NoContent(); });

        [HttpPost("campaigns/matches/{matchId:guid}/journal/photos/{entryId:guid}/{photoKey}/reports")]
        public Task<ActionResult> ReportPhoto(Guid matchId, Guid entryId, string photoKey, [FromBody] ReportDto dto, CancellationToken ct)
            => Run(async () => { await community.ReportPhotoAsync(User.GetUserId(), matchId, entryId, photoKey, dto ?? throw new ArgumentException("Os dados são obrigatórios."), ct); return NoContent(); });

        [HttpGet("admin/moderation/people")]
        [Authorize(Roles = "Admin")]
        public Task<ActionResult> People(CancellationToken ct) => Run(async () => Ok(await community.ModerationPeopleAsync(ct)));

        [HttpPost("admin/moderation/people/{userId:guid}/archive")]
        [Authorize(Roles = "Admin")]
        public Task<ActionResult> Archive(Guid userId, CancellationToken ct) => Run(async () => { await community.ArchivePersonReportsAsync(User.GetUserId(), userId, ct); return NoContent(); });

        [HttpPost("admin/moderation/people/{userId:guid}/suspend")]
        [Authorize(Roles = "Admin")]
        public Task<ActionResult> Suspend(Guid userId, [FromBody] SuspendDto dto, CancellationToken ct)
            => Run(async () => { await community.SuspendAsync(User.GetUserId(), userId, dto ?? throw new ArgumentException("Os dados são obrigatórios."), ct); return NoContent(); });

        [HttpPost("admin/moderation/people/{userId:guid}/lift-suspension")]
        [Authorize(Roles = "Admin")]
        public Task<ActionResult> Lift(Guid userId, CancellationToken ct) => Run(async () => { await community.LiftSuspensionAsync(User.GetUserId(), userId, ct); return NoContent(); });

        [HttpGet("admin/moderation/photos")]
        [Authorize(Roles = "Admin")]
        public Task<ActionResult> Photos(CancellationToken ct) => Run(async () => Ok(await community.ModerationPhotosAsync(ct)));

        /// <summary>An administrator sees a diary photo only while it has an open report.</summary>
        [HttpGet("admin/moderation/photos/{entryId:guid}/{photoKey}")]
        [Authorize(Roles = "Admin")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public Task<ActionResult> Photo(Guid entryId, string photoKey, CancellationToken ct) => Run(async () =>
        {
            Response.Headers.CacheControl = "no-store, private";
            Response.Headers.Pragma = "no-cache";
            var url = await community.ReportedPhotoUrlAsync(entryId, photoKey, ct);
            if (url == null) return NotFound();
            var image = await photos.ReadProtectedAsync(url, ct);
            return File(image.Content, image.ContentType);
        });

        public sealed record PhotoDecisionDto(string? Reason);

        /// <summary>keep · withdraw (from the community) · remove (from the match); the last two with a reason the author sees.</summary>
        [HttpPost("admin/moderation/photos/{entryId:guid}/{photoKey}/{decision:regex(^(keep|withdraw|remove)$)}")]
        [Authorize(Roles = "Admin")]
        public Task<ActionResult> ModeratePhoto(Guid entryId, string photoKey, string decision, [FromBody] PhotoDecisionDto? dto, CancellationToken ct)
            => Run(async () => { await community.ModeratePhotoAsync(User.GetUserId(), entryId, photoKey, decision, ct, dto?.Reason); return NoContent(); });

        /// <summary>A photo published in the community: anyone who sees it can report it (not its author).</summary>
        [HttpPost("experiences/{publicationId:guid}/photos/{photoId:guid}/reports")]
        public Task<ActionResult> ReportPublicPhoto(Guid publicationId, Guid photoId, [FromBody] ReportDto dto, CancellationToken ct)
            => Run(async () => { await community.ReportPublicPhotoAsync(User.GetUserId(), publicationId, photoId, dto ?? throw new ArgumentException("Os dados são obrigatórios."), ct); return NoContent(); });
    }
}
