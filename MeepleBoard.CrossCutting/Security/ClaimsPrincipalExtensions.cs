using System.Security.Claims;

namespace MeepleBoard.CrossCutting.Security
{
    public static class ClaimsPrincipalExtensions
    {
        public static Guid GetUserId(this ClaimsPrincipal user)
        {
            var id = user.FindFirst("sub")?.Value ??
                     user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(id, out var userId)
                ? userId
                : throw new UnauthorizedAccessException("UserId inválido.");
        }
    }
}