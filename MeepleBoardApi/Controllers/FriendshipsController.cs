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
        => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FriendLiteDto>>> GetMyFriends(CancellationToken ct)
        => Ok(await _service.GetMyFriendsAsync(CurrentUserId(), ct));

    [HttpGet("requests/incoming")]
    public async Task<ActionResult<IReadOnlyList<FriendRequestDto>>> GetIncoming(CancellationToken ct)
        => Ok(await _service.GetIncomingAsync(CurrentUserId(), ct));

    [HttpPost("request/{toUserId:guid}")]
    public async Task<IActionResult> SendRequest(Guid toUserId, CancellationToken ct)
    {
        await _service.RequestFriendshipAsync(CurrentUserId(), toUserId, ct);
        return NoContent();
    }

    [HttpPost("accept/{requestId:guid}")]
    public async Task<IActionResult> Accept(Guid requestId, CancellationToken ct)
    {
        await _service.AcceptAsync(CurrentUserId(), requestId, ct);
        return NoContent();
    }

    [HttpPost("reject/{requestId:guid}")]
    public async Task<IActionResult> Reject(Guid requestId, CancellationToken ct)
    {
        await _service.RejectAsync(CurrentUserId(), requestId, ct);
        return NoContent();
    }
}
