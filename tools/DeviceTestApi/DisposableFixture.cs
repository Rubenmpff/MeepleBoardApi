using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// DeviceTests only: fresh people for one verifier run (tools/DeviceTestApi/disposable.cjs), never the iPhone walkthrough
// accounts. A run declares who exists and how they relate (accepted friendships, pending requests, administrators,
// collection privacy and entries, duration sharing); the people are recorded in disposable-people.json and removed with
// everything they made when the run ends, even when it fails. People left by an interrupted run are swept on the next
// one. Endpoints answer only on the Mac itself; credentials are returned only there.
public static class DisposableFixture {
    const string RecordFile = "disposable-people.json";
    public const string ResourcePrefix = "https://example.org/meepleboard-device-test/disposable/";

    public sealed record PersonSpec(string Role, bool Admin = false, string? Privacy = null, bool? Contributes = null, List<LibrarySpec>? Library = null);
    public sealed record LibrarySpec(string Game, string Status, decimal? Price = null);
    // Keep: people for a manual validation (the iPhone), never swept; removed only by DELETE /fixture.
    public sealed record PeopleRequest(string Label, List<PersonSpec> People, List<List<string>>? Friends = null, List<List<string>>? Pending = null, bool Keep = false);
    public sealed record LegacyMatch(string Game, List<Guid> Players, Guid? Creator, double? Rating, string? Tags);
    public sealed record Recorded(Guid Id, string Label, DateTime CreatedAt, bool Keep = false);

    static string Password() => "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    public static void Map(WebApplication app, string dataPath) {
        var group = app.MapGroup("/device-test/disposable").AddEndpointFilter(async (context, next) =>
            context.HttpContext.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) ? await next(context) : Results.NotFound());
        var path = Path.Combine(dataPath, RecordFile);
        var gate = new SemaphoreSlim(1, 1);
        async Task<List<Recorded>> Read() => File.Exists(path) ? JsonSerializer.Deserialize<List<Recorded>>(await File.ReadAllTextAsync(path))! : [];
        async Task Write(List<Recorded> list) => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));

        group.MapPost("/people", async (PeopleRequest r, UserManager<User> users, MeepleBoardDbContext db) => {
            if (r.People.Count == 0 || r.People.Select(p => p.Role).Distinct().Count() != r.People.Count) return Results.BadRequest(new { message = "Distinct roles required." });
            var stamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
            var label = new string(r.Label.Where(char.IsLetterOrDigit).Take(12).ToArray()).ToLowerInvariant();
            var made = new Dictionary<string, object>(); var ids = new Dictionary<string, Guid>();
            await gate.WaitAsync();
            try {
                var record = await Read();
                foreach (var p in r.People) {
                    var role = new string(p.Role.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
                    var email = $"desc-{label}-{role}-{stamp}@meepleboard.test"; var password = Password();
                    var user = new User($"Teste-{role}-{stamp}", email, "Local") { EmailConfirmed = true };
                    var created = await users.CreateAsync(user, password);
                    if (!created.Succeeded) return Results.Problem(string.Join(",", created.Errors.Select(e => e.Code)));
                    await users.AddToRoleAsync(user, "User");
                    if (p.Admin) await users.AddToRoleAsync(user, "Admin");
                    record.Add(new(user.Id, label, DateTime.UtcNow, r.Keep));
                    ids[p.Role] = user.Id;
                    made[p.Role] = new { id = user.Id, email, password, userName = user.UserName };
                }
                await Write(record);
            } finally { gate.Release(); }
            // Relationships and preferences, written as the app would leave them.
            foreach (var p in r.People) {
                var user = await db.Users.SingleAsync(u => u.Id == ids[p.Role]);
                if (p.Privacy != null) user.SetLibraryPrivacy(Enum.Parse<LibraryPrivacy>(p.Privacy));
                if (p.Contributes != null) user.SetContributesDurationStats(p.Contributes.Value);
                foreach (var l in p.Library ?? []) {
                    var game = await db.Games.SingleAsync(g => g.Name == l.Game);
                    db.UserGameLibraries.Add(new UserGameLibrary(user.Id, game.Id, Enum.Parse<GameLibraryStatus>(l.Status), l.Price));
                }
            }
            void Link(List<string> pair, FriendshipStatus status) {
                var (from, to) = (ids[pair[0]], ids[pair[1]]);
                var (a, b) = from.CompareTo(to) < 0 ? (from, to) : (to, from);
                db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserAId = a, UserBId = b, InitiatorId = from, Status = status, CreatedAt = DateTime.UtcNow });
            }
            foreach (var pair in r.Friends ?? []) Link(pair, FriendshipStatus.Accepted);
            foreach (var pair in r.Pending ?? []) Link(pair, FriendshipStatus.Pending);
            await db.SaveChangesAsync();
            return Results.Ok(new { stamp, people = made });
        });

        // A session of these people moved in time (the API only schedules in the future): for the automatic end.
        group.MapPost("/sessions/{id:guid}/schedule", async (Guid id, DateTime scheduledStartDate, DateTime? acceptedAt, MeepleBoardDbContext db) => {
            var recorded = (await Read()).Select(x => x.Id).ToHashSet();
            var session = await db.GameSessions.Include(s => s.Players).SingleOrDefaultAsync(s => s.Id == id);
            if (session == null || !recorded.Contains(session.OrganizerId)) return Results.NotFound();
            Set(session, "ScheduledStartDate", scheduledStartDate.ToUniversalTime());
            if (acceptedAt != null) foreach (var p in session.Players.Where(p => !p.IsOrganizer && p.Status == GameSessionInviteStatus.Accepted)) Set(p, "RespondedAt", acceptedAt.Value.ToUniversalTime());
            await db.SaveChangesAsync();
            return Results.Ok(new { session.Status, session.AutoCloseAt });
        });

        // What SQL holds for these people: blocks, reports, suspension, sessions (refresh tokens counted, never shown).
        group.MapPost("/state", async (List<Guid> ids, MeepleBoardDbContext db) => {
            var recorded = (await Read()).Select(x => x.Id).ToHashSet();
            if (ids.Any(id => !recorded.Contains(id))) return Results.BadRequest(new { message = "Only disposable people." });
            return Results.Ok(new {
                blocks = await db.UserBlocks.AsNoTracking().Where(b => ids.Contains(b.BlockerId) || ids.Contains(b.BlockedId)).Select(b => new { b.BlockerId, b.BlockedId }).ToListAsync(),
                friendships = await db.Friendships.AsNoTracking().Where(f => ids.Contains(f.UserAId) && ids.Contains(f.UserBId)).Select(f => new { f.UserAId, f.UserBId, Status = f.Status.ToString() }).ToListAsync(),
                userReports = await db.UserReports.AsNoTracking().Where(r => ids.Contains(r.ReporterId) || ids.Contains(r.ReportedUserId))
                    .Select(r => new { r.ReporterId, r.ReportedUserId, r.Reason, hasDetail = r.Detail != null, open = r.ResolvedAt == null, r.Outcome }).ToListAsync(),
                photoReports = await db.PhotoReports.AsNoTracking().Where(r => ids.Contains(r.ReporterId))
                    .Select(r => new { r.ReporterId, r.JournalEntryId, r.Reason, open = r.ResolvedAt == null, r.Outcome }).ToListAsync(),
                users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.SuspendedAt, u.SuspendedUntil, u.SuspensionReason,
                    u.SuspensionLiftedAt, u.TokensRevokedAt, refreshTokens = db.RefreshTokens.Count(t => t.UserId == u.Id) }).ToListAsync(),
            });
        });

        // A suspension's end cannot be waited for: the end moves to a minute ago, as if the period had passed.
        group.MapPost("/expire-suspension/{person:guid}", async (Guid person, MeepleBoardDbContext db) => {
            if ((await Read()).All(x => x.Id != person)) return Results.NotFound();
            var until = DateTime.UtcNow.AddMinutes(-1);
            var changed = await db.Users.Where(u => u.Id == person && u.SuspendedAt != null && u.SuspendedUntil != null)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedUntil, until));
            return changed == 1 ? Results.NoContent() : Results.Conflict();
        });

        // A match as older versions stored it: rating and tags on the match itself, no diary entries. With or without a
        // known author (CreatorId), between disposable people only.
        group.MapPost("/legacy-match", async (LegacyMatch r, MeepleBoardDbContext db, IGameRepository games) => {
            var recorded = (await Read()).Select(x => x.Id).ToHashSet();
            if (r.Players.Count == 0 || r.Players.Any(p => !recorded.Contains(p)) || (r.Creator is { } c && !r.Players.Contains(c))) return Results.BadRequest(new { message = "Only disposable people." });
            var game = await db.Games.SingleAsync(g => g.Name == r.Game);
            var match = new Match(game.Id, DateTime.UtcNow.AddDays(-400));
            if (r.Creator is { } creator) match.SetCreator(creator);
            match.SetJournalData(r.Rating, null, r.Tags);
            db.Matches.Add(match);
            foreach (var p in r.Players) db.MatchPlayers.Add(new MatchPlayer(match.Id, p));
            await db.SaveChangesAsync();
            await games.RecomputeMeepleBoardRatingAsync(game.Id);
            return Results.Ok(new { id = match.Id });
        });

        // Everything these people made, then the accounts. Only ids recorded by /people.
        group.MapDelete("/fixture", async (string people, MeepleBoardDbContext db, UserManager<User> users, IGameRepository games) => {
            var ids = people.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
            await gate.WaitAsync();
            try {
                var record = await Read();
                if (ids.Any(id => record.All(x => x.Id != id))) return Results.BadRequest(new { message = "Only disposable people." });
                var removed = await Remove(db, users, games, ids);
                await Write(record.Where(x => !removed.Contains(x.Id)).ToList());
                return Results.Ok(new { removed = removed.Count });
            } finally { gate.Release(); }
        });

        // People left by runs that were interrupted (older than the given minutes, so a run in progress is never touched).
        group.MapPost("/sweep", async (int? olderThanMinutes, MeepleBoardDbContext db, UserManager<User> users, IGameRepository games) => {
            await gate.WaitAsync();
            try {
                var record = await Read();
                var limit = DateTime.UtcNow.AddMinutes(-(olderThanMinutes ?? 30));
                var stale = record.Where(x => x.CreatedAt < limit && !x.Keep).Select(x => x.Id).ToList();
                var removed = stale.Count == 0 ? [] : await Remove(db, users, games, stale);
                await Write(record.Where(x => !removed.Contains(x.Id)).ToList());
                return Results.Ok(new { removed = removed.Count });
            } finally { gate.Release(); }
        });
    }

    static async Task<List<Guid>> Remove(MeepleBoardDbContext db, UserManager<User> users, IGameRepository gamesRepository, List<Guid> everyone) {
        var matches = await db.Matches.Where(m => m.MatchPlayers.Any(p => everyone.Contains(p.UserId)) || (m.CreatorId != null && everyone.Contains(m.CreatorId.Value))).Select(m => m.Id).ToListAsync();
        var games = await db.Matches.Where(m => matches.Contains(m.Id)).Select(m => m.GameId).Distinct().ToListAsync();
        await db.CampaignMatches.Where(c => matches.Contains(c.MatchId)).ExecuteDeleteAsync();
        await db.Matches.Where(m => matches.Contains(m.Id)).ExecuteDeleteAsync();
        await db.GameSessions.Where(s => everyone.Contains(s.OrganizerId) || s.Players.Any(p => everyone.Contains(p.UserId))).ExecuteDeleteAsync();
        await db.Campaigns.Where(c => everyone.Contains(c.CreatorId) || c.Members.Any(m => everyone.Contains(m.UserId))).ExecuteDeleteAsync();
        await db.PhotoReports.Where(r => everyone.Contains(r.ReporterId)).ExecuteDeleteAsync();
        await db.UserReports.Where(r => everyone.Contains(r.ReporterId) || everyone.Contains(r.ReportedUserId)).ExecuteDeleteAsync();
        await db.UserBlocks.Where(b => everyone.Contains(b.BlockerId) || everyone.Contains(b.BlockedId)).ExecuteDeleteAsync();
        await db.ExperienceReports.Where(r => everyone.Contains(r.ReporterId)).ExecuteDeleteAsync();
        await db.GameResourceReports.Where(r => everyone.Contains(r.UserId)).ExecuteDeleteAsync();
        await db.GameResources.Where(r => r.SuggestedById != null && everyone.Contains(r.SuggestedById.Value)).ExecuteDeleteAsync();
        await db.GameResources.Where(r => r.Url.StartsWith(ResourcePrefix)).ExecuteDeleteAsync();
        await db.Friendships.Where(f => everyone.Contains(f.UserAId) || everyone.Contains(f.UserBId)).ExecuteDeleteAsync();
        await db.UserGameLibraries.Where(l => everyone.Contains(l.UserId)).ExecuteDeleteAsync();
        await db.RefreshTokens.Where(t => everyone.Contains(t.UserId)).ExecuteDeleteAsync();
        await db.EmailResendLogs.Where(l => everyone.Contains(l.UserId)).ExecuteDeleteAsync();
        foreach (var game in games) await gamesRepository.RecomputeMeepleBoardRatingAsync(game);
        var removed = new List<Guid>();
        foreach (var id in everyone) {
            var user = await users.FindByIdAsync(id.ToString());
            if (user == null || (await users.DeleteAsync(user)).Succeeded) removed.Add(id);
        }
        return removed;
    }

    static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
}
