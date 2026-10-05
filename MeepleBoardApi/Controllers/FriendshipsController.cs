using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("MeepleBoard/friendships")]
[Authorize]
public class FriendshipsController : ControllerBase
{
    private readonly IFriendshipService _service;

    public FriendshipsController(IFriendshipService service)
    {
        _service = service;
    }

    private Guid CurrentUserId()
        => Guid.Parse(
            User.FindFirstValue(
                ClaimTypes.NameIdentifier
            )!
        );

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FriendLiteDto>>> GetMyFriends(
        CancellationToken ct
    )
        => Ok(
            await _service.GetMyFriendsAsync(
                CurrentUserId(),
                ct
            )
        );

    [HttpGet("requests/incoming")]
    public async Task<ActionResult<IReadOnlyList<FriendRequestDto>>> GetIncoming(
        CancellationToken ct
    )
        => Ok(
            await _service.GetIncomingAsync(
                CurrentUserId(),
                ct
            )
        );

    [HttpGet("requests/outgoing")]
    public async Task<ActionResult<IReadOnlyList<OutgoingFriendRequestDto>>> GetOutgoing(
        CancellationToken ct
    )
        => Ok(
            await _service.GetOutgoingAsync(
                CurrentUserId(),
                ct
            )
        );

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<UserSearchResultDto>>> Search(
        [FromQuery] string query,
        CancellationToken ct
    )
        => Ok(
            await _service.SearchUsersAsync(
                CurrentUserId(),
                query,
                ct
            )
        );

    [HttpGet("profile/{userId:guid}")]
    public async Task<ActionResult<UserProfileDto>> GetProfile(
        Guid userId,
        CancellationToken ct
    )
    {
        var profile =
            await _service.GetProfileAsync(
                CurrentUserId(),
                userId,
                ct
            );

        return profile is null
            ? NotFound()
            : Ok(profile);
    }

    [HttpGet("{userId:guid}/matches")]
    public async Task<ActionResult<SharedMatchesPageDto>> GetSharedMatches(
        Guid userId,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default
    )
    {
        try
        {
            var result =
                await _service.GetSharedMatchesAsync(
                    CurrentUserId(),
                    userId,
                    page,
                    pageSize,
                    ct
                );

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    message = ex.Message,
                }
            );
        }
    }

    [HttpGet("{userId:guid}/matches/game/{gameId:guid}")]
    public async Task<ActionResult<IReadOnlyList<SharedMatchDetailDto>>> GetSharedMatchesForGame(
        Guid userId,
        Guid gameId,
        CancellationToken ct
    )
    {
        try
        {
            var result =
                await _service.GetSharedMatchesForGameAsync(
                    CurrentUserId(),
                    userId,
                    gameId,
                    ct
                );

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    message = ex.Message,
                }
            );
        }
    }

    [HttpPost("request/{toUserId:guid}")]
    public async Task<IActionResult> SendRequest(
        Guid toUserId,
        CancellationToken ct
    )
    {
        try
        {
            await _service.RequestFriendshipAsync(
                CurrentUserId(),
                toUserId,
                ct
            );

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(
                new
                {
                    message = ex.Message,
                }
            );
        }
    }

    [HttpPost("accept/{requestId:guid}")]
    public async Task<IActionResult> Accept(
        Guid requestId,
        CancellationToken ct
    )
    {
        try
        {
            await _service.AcceptAsync(
                CurrentUserId(),
                requestId,
                ct
            );

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    message = ex.Message,
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(
                new
                {
                    message = ex.Message,
                }
            );
        }
    }

    [HttpPost("reject/{requestId:guid}")]
    public async Task<IActionResult> Reject(
        Guid requestId,
        CancellationToken ct
    )
    {
        try
        {
            await _service.RejectAsync(
                CurrentUserId(),
                requestId,
                ct
            );

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    message = ex.Message,
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(
                new
                {
                    message = ex.Message,
                }
            );
        }
    }

    [HttpDelete("request/{requestId:guid}")]
    public async Task<IActionResult> Cancel(
        Guid requestId,
        CancellationToken ct
    )
    {
        try
        {
            await _service.CancelAsync(
                CurrentUserId(),
                requestId,
                ct
            );

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    message = ex.Message,
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(
                new
                {
                    message = ex.Message,
                }
            );
        }
    }

    [HttpDelete("{friendUserId:guid}")]
    public async Task<IActionResult> Remove(
        Guid friendUserId,
        CancellationToken ct
    )
    {
        try
        {
            await _service.RemoveAsync(
                CurrentUserId(),
                friendUserId,
                ct
            );

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    message = ex.Message,
                }
            );
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(
                new
                {
                    message = ex.Message,
                }
            );
        }
    }
}