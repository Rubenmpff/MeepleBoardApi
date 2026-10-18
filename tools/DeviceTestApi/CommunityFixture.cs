using System.Security.Cryptography;
using System.Text.Json;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// DeviceTests only: disposable people for verify-community.cjs (blocks, reports, suspension, filter). Each run creates
// fresh «comunidade-…» accounts (recorded in community-people.json) and removes them, with everything they made, at the
// end. Never accounts.json, never the iPhone walkthrough. Endpoints answer only on the Mac itself.
public static class CommunityFixture {
    const string PeopleFile = "community-people.json";
    public const string ResourcePrefix = "https://example.org/meepleboard-device-test/community/";

    static string Password() => "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    public static void Map(WebApplication app, string dataPath) {
        var group = app.MapGroup("/device-test/community").AddEndpointFilter(async (context, next) =>
            context.HttpContext.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) ? await next(context) : Results.NotFound());
        var path = Path.Combine(dataPath, PeopleFile);
        async Task<List<Guid>> People() => File.Exists(path) ? JsonSerializer.Deserialize<List<Guid>>(await File.ReadAllTextAsync(path))! : [];

        // A, B, C and D; accepted friendships A–B, A–C, B–C and A–D (so B–D and C–D are not friends).
        group.MapPost("/people", async (UserManager<User> users, MeepleBoardDbContext db) => {
            var ids = await People(); var created = new Dictionary<string, object>(); var byRole = new Dictionary<string, Guid>();
            var stamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
            foreach (var role in new[] { "a", "b", "c", "d" }) {
                var email = $"comunidade-{role}-{stamp}@meepleboard.test"; var password = Password();
                var user = new User($"Teste-comunidade-{role}-{stamp}", email, "Local") { EmailConfirmed = true };
                var r = await users.CreateAsync(user, password); if (!r.Succeeded) return Results.Problem(string.Join(",", r.Errors.Select(e => e.Code)));
                await users.AddToRoleAsync(user, "User");
                ids.Add(user.Id); byRole[role] = user.Id;
                created[role] = new { id = user.Id, email, password, userName = user.UserName };
            }
            foreach (var (x, y) in new[] { ("a", "b"), ("a", "c"), ("b", "c"), ("a", "d") }) {
                var (first, second) = byRole[x].CompareTo(byRole[y]) < 0 ? (byRole[x], byRole[y]) : (byRole[y], byRole[x]);
                db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserAId = first, UserBId = second, InitiatorId = byRole[x], Status = FriendshipStatus.Accepted, CreatedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(ids));
            var game = await db.Games.SingleAsync(g => g.BGGId == 990001);
            return Results.Ok(new { people = created, stamp, game = new { id = game.Id, name = game.Name } });
        });

        // What SQL holds for these people: blocks, reports, suspension, sessions (refresh tokens counted, never shown).
        group.MapPost("/state", async (List<Guid> ids, MeepleBoardDbContext db) => Results.Ok(new {
            blocks = await db.UserBlocks.AsNoTracking().Where(b => ids.Contains(b.BlockerId) || ids.Contains(b.BlockedId)).Select(b => new { b.BlockerId, b.BlockedId }).ToListAsync(),
            friendships = await db.Friendships.AsNoTracking().Where(f => ids.Contains(f.UserAId) && ids.Contains(f.UserBId)).Select(f => new { f.UserAId, f.UserBId, Status = f.Status.ToString() }).ToListAsync(),
            userReports = await db.UserReports.AsNoTracking().Where(r => ids.Contains(r.ReporterId) || ids.Contains(r.ReportedUserId))
                .Select(r => new { r.ReporterId, r.ReportedUserId, r.Reason, hasDetail = r.Detail != null, open = r.ResolvedAt == null, r.Outcome }).ToListAsync(),
            photoReports = await db.PhotoReports.AsNoTracking().Where(r => ids.Contains(r.ReporterId))
                .Select(r => new { r.ReporterId, r.JournalEntryId, r.Reason, open = r.ResolvedAt == null, r.Outcome }).ToListAsync(),
            users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.SuspendedAt, u.SuspendedUntil, u.SuspensionReason,
                u.SuspensionLiftedAt, u.TokensRevokedAt, refreshTokens = db.RefreshTokens.Count(t => t.UserId == u.Id) }).ToListAsync(),
        }));

        // A suspension's end cannot be waited for: the end moves to a minute ago, as if 7 days had passed.
        group.MapPost("/expire-suspension/{person:guid}", async (Guid person, MeepleBoardDbContext db) => {
            if (!(await People()).Contains(person)) return Results.NotFound();
            var until = DateTime.UtcNow.AddMinutes(-1);
            var changed = await db.Users.Where(u => u.Id == person && u.SuspendedAt != null && u.SuspendedUntil != null)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedUntil, until));
            return changed == 1 ? Results.NoContent() : Results.Conflict();
        });

        // Everything these fixture people made, then the accounts themselves. Only ids recorded by /people.
        group.MapDelete("/fixture", async (string people, MeepleBoardDbContext db, UserManager<User> users, IGameRepository gamesRepository) => {
            var recorded = await People();
            var everyone = people.Split(',').Select(Guid.Parse).ToList();
            if (everyone.Any(id => !recorded.Contains(id))) return Results.BadRequest(new { message = "Only fixture people." });
            var matches = await db.MatchPlayers.Where(p => everyone.Contains(p.UserId)).Select(p => p.MatchId).Distinct().ToListAsync();
            var games = await db.Matches.Where(m => matches.Contains(m.Id)).Select(m => m.GameId).Distinct().ToListAsync();
            await db.CampaignMatches.Where(c => matches.Contains(c.MatchId)).ExecuteDeleteAsync();
            await db.Matches.Where(m => matches.Contains(m.Id)).ExecuteDeleteAsync();
            await db.GameSessions.Where(s => everyone.Contains(s.OrganizerId) || s.Players.Any(p => everyone.Contains(p.UserId))).ExecuteDeleteAsync();
            await db.Campaigns.Where(c => everyone.Contains(c.CreatorId) || c.Members.Any(m => everyone.Contains(m.UserId))).ExecuteDeleteAsync();
            await db.PhotoReports.Where(r => everyone.Contains(r.ReporterId)).ExecuteDeleteAsync();
            await db.UserReports.Where(r => everyone.Contains(r.ReporterId) || everyone.Contains(r.ReportedUserId)).ExecuteDeleteAsync();
            await db.UserBlocks.Where(b => everyone.Contains(b.BlockerId) || everyone.Contains(b.BlockedId)).ExecuteDeleteAsync();
            await db.ExperienceReports.Where(r => everyone.Contains(r.ReporterId)).ExecuteDeleteAsync();
            await db.GameResources.Where(r => r.Url.StartsWith(ResourcePrefix)).ExecuteDeleteAsync();
            await db.Friendships.Where(f => everyone.Contains(f.UserAId) || everyone.Contains(f.UserBId)).ExecuteDeleteAsync();
            await db.UserGameLibraries.Where(l => everyone.Contains(l.UserId)).ExecuteDeleteAsync();
            await db.RefreshTokens.Where(t => everyone.Contains(t.UserId)).ExecuteDeleteAsync();
            await db.EmailResendLogs.Where(l => everyone.Contains(l.UserId)).ExecuteDeleteAsync();
            foreach (var game in games) await gamesRepository.RecomputeMeepleBoardRatingAsync(game);
            var removed = new List<Guid>();
            foreach (var id in everyone)
                if (await users.FindByIdAsync(id.ToString()) is { } user && (await users.DeleteAsync(user)).Succeeded) removed.Add(id);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(recorded.Except(removed).ToList()));
            return Results.Ok(new { removed = removed.Count });
        });
    }
}
