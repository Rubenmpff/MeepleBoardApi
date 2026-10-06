using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.ExternalServices.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers;
[ApiController]
[Authorize]
[Route("MeepleBoard/campaigns/matches/{matchId:guid}/journal/photos")]
public class JournalPhotosController(IMatchRepository matches, ICampaignRepository journals, IPhotoStorageService photos) : ControllerBase
{
    [HttpGet("{entryId:guid}/{photoKey}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(Guid matchId, Guid entryId, string photoKey, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        try
        {
            var match = await matches.GetByIdAsync(matchId, ct);
            if (match == null) return NotFound();
            MatchService.RequireParticipant(match, User.GetUserId());
            var entries = await journals.GetJournalEntriesForMatchAsync(matchId, ct);
            var entry = entries.FirstOrDefault(e => e.Id == entryId);
            var url = entry?.PhotoUrls.FirstOrDefault(u => JournalPhotoReference.Key(u) == photoKey && JournalPhotoReference.IsProtected(u));
            if (url == null) return NotFound();
            var image = await photos.ReadProtectedAsync(url, ct);
            return File(image.Content, image.ContentType);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
