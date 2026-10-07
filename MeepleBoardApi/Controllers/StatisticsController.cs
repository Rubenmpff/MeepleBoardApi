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
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
    [HttpGet("matches")]
    public async Task<IActionResult> Matches([FromQuery] StatisticsMatchesQuery query, CancellationToken ct)
    {
        try { return Ok(await service.Matches(User.GetUserId(), query, ct)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
