using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace MeepleBoardApi.Controllers;

[ApiController, Authorize, Route("MeepleBoard/statistics/me")]
public sealed class StatisticsController(StatisticsService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Summary([FromQuery] StatisticsQuery query, CancellationToken ct)
    {
        try { return Ok(await service.Summary(User.GetUserId(), query, ct)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
    [HttpGet("matches")]
    public async Task<IActionResult> Matches([FromQuery] StatisticsMatchesQuery query, CancellationToken ct)
    {
        try { return Ok(await service.Matches(User.GetUserId(), query, ct)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
    [HttpGet("company")]
    public async Task<IActionResult> Company([FromQuery] StatisticsQuery query, [FromQuery] Guid? friendId, CancellationToken ct) {
        try { return Ok(await service.Company(User.GetUserId(), query, friendId, ct)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
    [HttpGet("explore")]
    public async Task<IActionResult> Explore([FromQuery] StatisticsQuery query, CancellationToken ct) {
        try { return Ok(await service.Explore(User.GetUserId(), query, ct)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
    [HttpGet("year")]
    public async Task<IActionResult> Year([FromQuery] int year, [FromQuery] string timeZone = "UTC", [FromQuery] Guid? gameId = null, [FromQuery] string? mode = null, CancellationToken ct = default) {
        try { return Ok(await service.Year(User.GetUserId(), year, timeZone, gameId, mode, ct)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
