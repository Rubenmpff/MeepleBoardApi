using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// DeviceTests only: disposable accounts and data for verify-own-account-deletion.cjs. Two accounts that stay
// («conta-fica-a», «conta-fica-b», in account-deletion-accounts.json) and, each run, fresh people to delete
// («conta-elimina-…», recorded in account-deletion-people.json for clean-up). Never accounts.json, never the
// iPhone walkthrough. Endpoints answer only on the Mac itself; credentials are returned only there.
public static class AccountDeletionFixture {
    const string AccountsFile = "account-deletion-accounts.json", PeopleFile = "account-deletion-people.json";
    public const string GuestPushToken = "ExponentPushToken[device-test-account-deletion]";

    public static async Task EnsureAccounts(UserManager<User> users, string dataPath) {
        var path = Path.Combine(dataPath, AccountsFile);
        var accounts = File.Exists(path) ? JsonSerializer.Deserialize<List<TestAccount>>(await File.ReadAllTextAsync(path))! : [];
        foreach (var name in new[] { "conta-fica-a", "conta-fica-b" }) {
            var email = name + "@meepleboard.test";
            var account = accounts.SingleOrDefault(a => a.Email == email);
            if (account == null) { account = new(email, Password(), Guid.Empty); accounts.Add(account); }
            var user = await users.FindByEmailAsync(email);
            if (user == null) {
                user = new User("Teste-" + name, email, "Local") { EmailConfirmed = true };
                var r = await users.CreateAsync(user, account.Password); if (!r.Succeeded) throw new Exception("Account deletion fixture account failed: " + string.Join(",", r.Errors.Select(e => e.Code)));
                await users.AddToRoleAsync(user, "User");
            }
            account.Id = user.Id;
        }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
    }

    static string Password() => "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    public static void Map(WebApplication app, string dataPath) {
        var group = app.MapGroup("/device-test/account-deletion").AddEndpointFilter(async (context, next) =>
            context.HttpContext.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) ? await next(context) : Results.NotFound());
        async Task<(Guid A, Guid B)> Stay() {
            var accounts = JsonSerializer.Deserialize<List<TestAccount>>(await File.ReadAllTextAsync(Path.Combine(dataPath, AccountsFile)))!;
            return (accounts.Single(a => a.Email.StartsWith("conta-fica-a@")).Id, accounts.Single(a => a.Email.StartsWith("conta-fica-b@")).Id);
        }
        async Task<List<Guid>> People() {
            var path = Path.Combine(dataPath, PeopleFile);
            return File.Exists(path) ? JsonSerializer.Deserialize<List<Guid>>(await File.ReadAllTextAsync(path))! : [];
        }

        // Fresh people to delete: P (in the app) and Q (through support), friends of A and B; A has a push token.
        group.MapPost("/people", async (UserManager<User> users, MeepleBoardDbContext db) => {
            var (a, b) = await Stay();
            var created = new List<object>(); var ids = await People();
            foreach (var role in new[] { "p", "q" }) {
                var email = $"conta-elimina-{role}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}@meepleboard.test";
                var password = Password();
                var user = new User("Teste-elimina-" + role + "-" + email[17..21], email, "Local") { EmailConfirmed = true };
                var r = await users.CreateAsync(user, password); if (!r.Succeeded) return Results.Problem(string.Join(",", r.Errors.Select(e => e.Code)));
                await users.AddToRoleAsync(user, "User");
                ids.Add(user.Id);
                foreach (var other in new[] { a, b }) {
                    var (x, y) = user.Id.CompareTo(other) < 0 ? (user.Id, other) : (other, user.Id);
                    db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserAId = x, UserBId = y, InitiatorId = user.Id, Status = FriendshipStatus.Accepted });
                }
                created.Add(new { role, id = user.Id, email, password });
            }
            var guest = await db.Users.SingleAsync(u => u.Id == a);
            Set(guest, "ExpoPushToken", GuestPushToken);
            await db.SaveChangesAsync();
            await File.WriteAllTextAsync(Path.Combine(dataPath, PeopleFile), JsonSerializer.Serialize(ids));
            return Results.Ok(new { people = created, a, b });
        });

        // Sessions and campaigns around P, with dates and states the API cannot create (past starts, ended sessions).
        group.MapPost("/structure/{person:guid}", async (Guid person, MeepleBoardDbContext db) => {
            var (a, b) = await Stay();
            if (!(await People()).Contains(person)) return Results.NotFound();
            var now = DateTime.UtcNow;
            GameSession Session(string name, Guid organizer, DateTime scheduled, string? location, params (Guid User, bool Accepted)[] guests) {
                var s = new GameSession(name, organizer, location, now.AddHours(1));
                Set(s, "ScheduledStartDate", scheduled);
                s.Players.Add(new GameSessionPlayer(s.Id, organizer, isOrganizer: true));
                foreach (var (user, accepted) in guests) { var p = new GameSessionPlayer(s.Id, user); if (accepted) { p.Accept(); Set(p, "RespondedAt", scheduled.AddHours(-2)); } s.Players.Add(p); }
                db.GameSessions.Add(s); return s;
            }
            var scheduled = Session("Jantar em casa do P", person, now.AddDays(2), "Rua Exemplo 1", (a, false), (b, true));
            var running = Session("Tarde do P", person, now.AddHours(-1), "Café Exemplo", (a, true));
            var closed = Session("Noite do P", person, now.AddDays(-3), "Rua Exemplo 2", (a, true));
            Set(closed, "EndDate", now.AddDays(-2));
            var ofA = Session("Sessão de A", a, now.AddDays(2), "Casa de A", (person, true));
            var game = await db.Games.SingleAsync(g => g.BGGId == 990001);
            Campaign Campaign(string name, Guid creator, string? notes, params (Guid User, CampaignMemberStatus Status)[] members) {
                var c = new Campaign(name, game.Id, creator); if (notes != null) c.UpdateNotes(notes);
                c.Members.Add(new CampaignMember(c.Id, creator, isCreator: true));
                foreach (var (user, status) in members) { var m = new CampaignMember(c.Id, user); if (status == CampaignMemberStatus.Accepted) m.Accept(); else if (status == CampaignMemberStatus.Declined) m.Decline(); c.Members.Add(m); }
                db.Campaigns.Add(c); return c;
            }
            var transfer = Campaign("Campanha do P (passa para A)", person, "Notas que A escreveu", (a, CampaignMemberStatus.Accepted), (b, CampaignMemberStatus.Accepted));
            var readOnly = Campaign("Campanha do P (sem gestor)", person, null, (b, CampaignMemberStatus.Accepted));
            var alone = Campaign("Campanha só do P", person, "Notas só do P", (b, CampaignMemberStatus.Declined));
            var campaignOfA = Campaign("Campanha de A", a, "Notas de A", (person, CampaignMemberStatus.Accepted));
            await db.SaveChangesAsync();
            return Results.Ok(new { sessions = new { scheduled = scheduled.Id, running = running.Id, closed = closed.Id, ofA = ofA.Id },
                campaigns = new { transfer = transfer.Id, readOnly = readOnly.Id, alone = alone.Id, ofA = campaignOfA.Id }, gameId = game.Id });
        });

        // An encounter title written by whoever recorded the match (the API adds matches to campaigns without a title).
        group.MapPost("/encounter", async (Guid campaign, Guid match, string title, MeepleBoardDbContext db) => {
            db.CampaignMatches.Add(new CampaignMatch(campaign, match, 1, title)); await db.SaveChangesAsync(); return Results.Ok();
        });

        group.MapPost("/fail", (int photos, bool push) => {
            Interlocked.Exchange(ref LocalPhotos.FailNextDeletes, photos);
            Interlocked.Exchange(ref OfflineNotifications.FailNextBulk, push ? 1 : 0);
            return Results.Ok();
        });
        group.MapGet("/recorded", () => Results.Ok(new { jobs = DisabledBackgroundJobs.Recorded.ToArray(), pushBulk = OfflineNotifications.BulkSent }));

        // State as stored in SQL, for the ids the verifier asks about.
        group.MapPost("/state", async (StateRequest r, MeepleBoardDbContext db) => Results.Ok(new {
            users = await db.Users.AsNoTracking().Where(u => r.Users.Contains(u.Id)).Select(u => new { u.Id, u.UserName, u.Email, u.LastActiveAt, u.DeletedAt, u.ExpoPushToken }).ToListAsync(),
            sessions = await db.GameSessions.AsNoTracking().Where(s => r.Sessions.Contains(s.Id)).Select(s => new { s.Id, s.Name, s.Location, s.EndDate, s.IsCancelled, s.ClosedAutomatically, players = s.Players.Count }).ToListAsync(),
            campaigns = await db.Campaigns.AsNoTracking().Where(c => r.Campaigns.Contains(c.Id)).Select(c => new { c.Id, c.Name, c.Notes, c.CreatorId,
                creators = c.Members.Where(m => m.IsCreator).Select(m => m.UserId).ToList(), matches = c.CampaignMatches.Select(m => new { m.MatchId, m.SessionTitle }).ToList() }).ToListAsync(),
            matches = await db.Matches.AsNoTracking().Where(m => r.Matches.Contains(m.Id)).Select(m => new { m.Id, m.Location, m.ScoreSummary, m.Tags, m.Notes, m.DurationInMinutes, m.Result, m.CreatorId,
                players = m.MatchPlayers.Select(p => new { p.UserId, p.Score, p.Outcome }).ToList(), journals = m.JournalEntries.Select(e => new { e.UserId, e.PersonalRating, e.Notes, e.PhotoUrls }).ToList() }).ToListAsync(),
            reports = await db.ExperienceReports.AsNoTracking().Where(x => r.Reporters.Contains(x.ReporterId)).Select(x => new { x.Id, x.ReporterId, x.Reason, x.Detail, open = x.ResolvedAt == null }).ToListAsync(),
            resourceReports = await db.GameResourceReports.AsNoTracking().Where(x => r.Reporters.Contains(x.UserId)).Select(x => new { x.Id, x.UserId, x.Reason, x.Comment, open = x.ResolvedAt == null }).ToListAsync(),
            resources = await db.GameResources.AsNoTracking().Where(x => r.Resources.Contains(x.Id)).Select(x => new { x.Id, x.Title, x.SuggestedById }).ToListAsync(),
            libraries = await db.UserGameLibraries.AsNoTracking().CountAsync(l => r.Users.Contains(l.UserId)),
            friendships = await db.Friendships.AsNoTracking().CountAsync(f => r.Users.Contains(f.UserAId) || r.Users.Contains(f.UserBId)),
            photoFiles = r.Photos.Select(p => File.Exists(Path.Combine(dataPath, "photos", Path.GetFileNameWithoutExtension(p) + ".jpg"))).ToList(),
        }));

        // The retention step of UserCleanupJob, run now for the fixture's deleted people only.
        group.MapPost("/report-retention", async (DateTime deletedBefore, IUserRepository users) =>
            Results.Ok(new { cleared = await users.ClearDeletedReporterTextAsync(deletedBefore.ToUniversalTime(), await People()) }));

        // Everything the fixture people and A/B left: matches, sessions and campaigns; game ratings recalculated.
        group.MapDelete("/fixture", async (MeepleBoardDbContext db, IGameRepository gamesRepository) => {
            var (a, b) = await Stay();
            var everyone = (await People()).Append(a).Append(b).ToList();
            var matches = await db.MatchPlayers.Where(p => everyone.Contains(p.UserId)).Select(p => p.MatchId).Distinct().ToListAsync();
            var games = await db.Matches.Where(m => matches.Contains(m.Id)).Select(m => m.GameId).Distinct().ToListAsync();
            await db.CampaignMatches.Where(c => matches.Contains(c.MatchId)).ExecuteDeleteAsync();
            await db.Matches.Where(m => matches.Contains(m.Id)).ExecuteDeleteAsync();
            await db.GameSessions.Where(s => everyone.Contains(s.OrganizerId)).ExecuteDeleteAsync();
            await db.Campaigns.Where(c => everyone.Contains(c.CreatorId)).ExecuteDeleteAsync();
            await db.ExperienceReports.Where(r => everyone.Contains(r.ReporterId)).ExecuteDeleteAsync();
            await db.GameResourceReports.Where(r => everyone.Contains(r.UserId)).ExecuteDeleteAsync();
            // Resources the verifier created (also those left by an interrupted run); their reports go with them.
            await db.GameResources.Where(r => r.Url.StartsWith("https://example.org/meepleboard-device-test/account-deletion/")).ExecuteDeleteAsync();
            foreach (var game in games) await gamesRepository.RecomputeMeepleBoardRatingAsync(game);
            return Results.NoContent();
        });
    }

    public sealed record StateRequest(List<Guid> Users, List<Guid> Sessions, List<Guid> Campaigns, List<Guid> Matches, List<Guid> Reporters, List<Guid> Resources, List<string> Photos);

    static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
}
