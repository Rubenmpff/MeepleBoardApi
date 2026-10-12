using System.Security.Claims;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    /// <summary>
    /// Game resources for signed-in users: approved links, suggestions (pending until approved) and problem reports.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("MeepleBoard/game/{gameId:guid}/resources")]
    public class GameResourcesController : ControllerBase
    {
        private readonly IGameResourceService _resources;

        public GameResourcesController(IGameResourceService resources) => _resources = resources;

        /// <summary>Approved resources. language: "all" or a two-letter code; language-independent resources are always included.</summary>
        [HttpGet]
        public Task<IActionResult> List(Guid gameId, [FromQuery] string language = "all", CancellationToken ct = default) =>
            Run(async () => Ok(await _resources.ListAsync(gameId, language, ct)));

        /// <summary>Suggest a resource; it stays pending until an administrator approves it.</summary>
        [HttpPost("suggestions")]
        public Task<IActionResult> Suggest(Guid gameId, [FromBody] GameResourceRequest request, CancellationToken ct = default) =>
            Run(async () => StatusCode(StatusCodes.Status201Created, await _resources.SuggestAsync(gameId, UserId(), request, ct)));

        /// <summary>Report a broken, wrong or incompatible link.</summary>
        [HttpPost("~/MeepleBoard/game-resources/{resourceId:guid}/reports")]
        public Task<IActionResult> Report(Guid resourceId, [FromBody] GameResourceReportRequest request, CancellationToken ct = default) =>
            Run(async () => { await _resources.ReportAsync(resourceId, UserId(), request, ct); return StatusCode(StatusCodes.Status201Created); });

        /// <summary>Whether the signed-in user can manage resources, so the app can show the administration entry.</summary>
        [HttpGet("~/MeepleBoard/game-resources/permissions")]
        public IActionResult Permissions() => Ok(new { canManage = User.IsInRole("Admin") });

        private Guid UserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());

        internal static async Task<IActionResult> Handle(ControllerBase controller, Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (KeyNotFoundException ex) { return controller.NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return controller.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return controller.Conflict(new { message = ex.Message }); }
        }

        private Task<IActionResult> Run(Func<Task<IActionResult>> action) => Handle(this, action);
    }
}
