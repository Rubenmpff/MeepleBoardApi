using System.Security.Cryptography;
using System.Text;
namespace MeepleBoard.Services.Implementations;
public static class JournalPhotoReference
{
    public static string Key(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
    public static string Path(Guid matchId, Guid entryId, string url) => $"/MeepleBoard/campaigns/matches/{matchId}/journal/photos/{entryId}/{Key(url)}";
    public static bool IsProtected(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "res.cloudinary.com" && uri.AbsolutePath.Contains("/image/authenticated/", StringComparison.Ordinal);
}
