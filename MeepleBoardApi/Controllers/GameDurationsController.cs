using MeepleBoard.Services.GameDurations;
using MeepleBoard.CrossCutting.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    /// <summary>
    /// Duration statistics of a game: the caller's own matches and the community aggregate of matches that every
    /// participant shares. Returns aggregates only — never match identifiers, names, dates or single durations.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("MeepleBoard/game/{gameId:guid}/durations")]
    public class GameDurationsController(GameDurationService durations) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<GameDurationsDto>> Get(Guid gameId, CancellationToken cancellationToken)
            => Ok(await durations.GetAsync(gameId, User.GetUserId(), cancellationToken));
    }
}
