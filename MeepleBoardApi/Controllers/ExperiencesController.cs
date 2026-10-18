using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    /// <summary>
    /// Publishing experiences: the participant's own publication for a match, the public list on the game page,
    /// reports and moderation. Nothing here creates or changes a rating.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("MeepleBoard")]
    public class ExperiencesController(IExperienceService experiences, IPhotoStorageService photos) : ControllerBase
    {
        async Task<ActionResult> Run(Func<Task<ActionResult>> action)
        {
            try { return await action(); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("campaigns/matches/{matchId:guid}/journal/publication")]
        public Task<ActionResult> GetMine(Guid matchId, CancellationToken ct) => Run(async () => Ok(await experiences.GetMineAsync(matchId, User.GetUserId(), ct)));

        /// <summary>Explicit choices: show the rating with your name; keep or publish a comment.</summary>
        [HttpPut("campaigns/matches/{matchId:guid}/journal/publication")]
        public Task<ActionResult> Set(Guid matchId, [FromBody] SetPublicationDto dto, CancellationToken ct)
            => Run(async () => Ok(await experiences.SetAsync(matchId, User.GetUserId(), dto ?? throw new ArgumentException("Os dados são obrigatórios."), ct)));

        [HttpPost("campaigns/matches/{matchId:guid}/journal/publication/{change:regex(^(withdraw-rating|withdraw-comment|delete-comment|withdraw-all)$)}")]
        public Task<ActionResult> Change(Guid matchId, string change, CancellationToken ct) => Run(async () => Ok(await experiences.ChangeAsync(matchId, User.GetUserId(), change, ct)));

        /// <summary>Withdraws one photo from the community; it stays in the diary, shared with the players.</summary>
        [HttpDelete("campaigns/matches/{matchId:guid}/journal/publication/photos/{photoId:guid}")]
        public Task<ActionResult> WithdrawPhoto(Guid matchId, Guid photoId, CancellationToken ct) => Run(async () => Ok(await experiences.WithdrawPhotoAsync(matchId, User.GetUserId(), photoId, ct)));

        /// <summary>
        /// A published photo, for any signed-in person who may see it now (blocks respected). The path names only the publication
        /// and the photo; after it is withdrawn or hidden, new requests get 404. Never cached; copies already taken are out of reach.
        /// </summary>
        [HttpGet("experiences/{id:guid}/photos/{photoId:guid}")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public Task<ActionResult> Photo(Guid id, Guid photoId, CancellationToken ct) => Run(async () =>
        {
            Response.Headers.CacheControl = "no-store, private";
            Response.Headers.Pragma = "no-cache";
            var url = await experiences.PublicPhotoUrlAsync(id, photoId, User.GetUserId(), ct);
            if (url == null) return NotFound();
            var image = await photos.ReadProtectedAsync(url, ct);
            return File(image.Content, image.ContentType);
        });

        [HttpGet("game/{gameId:guid}/experiences")]
        public Task<ActionResult> List(Guid gameId, [FromQuery] int offset = 0, [FromQuery] int limit = 20, CancellationToken ct = default)
            => Run(async () => Ok(await experiences.ListForGameAsync(gameId, User.GetUserId(), offset, limit, ct)));

        [HttpPost("experiences/{id:guid}/reports")]
        public Task<ActionResult> Report(Guid id, [FromBody] ReportDto dto, CancellationToken ct)
            => Run(async () => { await experiences.ReportAsync(id, User.GetUserId(), dto ?? throw new ArgumentException("Os dados são obrigatórios."), ct); return NoContent(); });

        [HttpGet("admin/experiences/reported")]
        [Authorize(Roles = "Admin")]
        public Task<ActionResult> Reported(CancellationToken ct) => Run(async () => Ok(await experiences.ListReportedAsync(ct)));

        public sealed record ModerationDto(string? Reason);

        [HttpPost("admin/experiences/{id:guid}/{decision:regex(^(keep|hide|restore)$)}")]
        [Authorize(Roles = "Admin")]
        public Task<ActionResult> Moderate(Guid id, string decision, [FromBody] ModerationDto? dto, CancellationToken ct)
            => Run(async () => { await experiences.ModerateAsync(id, User.GetUserId(), decision, dto?.Reason, ct); return NoContent(); });
    }
}
