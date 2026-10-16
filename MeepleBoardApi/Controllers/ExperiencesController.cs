using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.Experiences;
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
    public class ExperiencesController(ExperienceService experiences) : ControllerBase
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

        [HttpPost("campaigns/matches/{matchId:guid}/journal/publication/{change:regex(^(withdraw-rating|withdraw-comment|delete-comment)$)}")]
        public Task<ActionResult> Change(Guid matchId, string change, CancellationToken ct) => Run(async () => Ok(await experiences.ChangeAsync(matchId, User.GetUserId(), change, ct)));

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
