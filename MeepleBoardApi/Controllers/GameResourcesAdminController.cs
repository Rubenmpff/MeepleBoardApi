using System.Security.Claims;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    /// <summary>
    /// Resource administration: review suggestions, add, edit, remove and resolve problem reports. Administrators only.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "Admin")]
    [Route("MeepleBoard/admin/game-resources")]
    public class GameResourcesAdminController : ControllerBase
    {
        private readonly IGameResourceService _resources;

        public GameResourcesAdminController(IGameResourceService resources) => _resources = resources;

        /// <summary>status: Pending, Approved or Rejected (all when omitted); optionally one game.</summary>
        [HttpGet]
        public Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? gameId, CancellationToken ct = default) =>
            Run(async () => Ok(await _resources.ReviewListAsync(status, gameId, ct)));

        [HttpPost("~/MeepleBoard/admin/game/{gameId:guid}/resources")]
        public Task<IActionResult> Create(Guid gameId, [FromBody] GameResourceRequest request, CancellationToken ct = default) =>
            Run(async () => StatusCode(StatusCodes.Status201Created, await _resources.CreateAsync(gameId, AdminId(), request, ct)));

        [HttpPut("{id:guid}")]
        public Task<IActionResult> Update(Guid id, [FromBody] GameResourceRequest request, CancellationToken ct = default) =>
            Run(async () => Ok(await _resources.UpdateAsync(id, request, ct)));

        [HttpPost("{id:guid}/approve")]
        public Task<IActionResult> Approve(Guid id, CancellationToken ct = default) =>
            Run(async () => Ok(await _resources.ApproveAsync(id, AdminId(), ct)));

        [HttpPost("{id:guid}/reject")]
        public Task<IActionResult> Reject(Guid id, [FromBody] GameResourceReviewRequest? request, CancellationToken ct = default) =>
            Run(async () => Ok(await _resources.RejectAsync(id, AdminId(), request?.Note, ct)));

        [HttpDelete("{id:guid}")]
        public Task<IActionResult> Delete(Guid id, CancellationToken ct = default) =>
            Run(async () => { await _resources.DeleteAsync(id, ct); return NoContent(); });

        [HttpGet("reports")]
        public Task<IActionResult> Reports(CancellationToken ct = default) =>
            Run(async () => Ok(await _resources.OpenReportsAsync(ct)));

        [HttpPost("reports/{reportId:guid}/resolve")]
        public Task<IActionResult> Resolve(Guid reportId, CancellationToken ct = default) =>
            Run(async () => { await _resources.ResolveReportAsync(reportId, AdminId(), ct); return NoContent(); });

        private Guid AdminId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        private Task<IActionResult> Run(Func<Task<IActionResult>> action) => GameResourcesController.Handle(this, action);
    }
}
