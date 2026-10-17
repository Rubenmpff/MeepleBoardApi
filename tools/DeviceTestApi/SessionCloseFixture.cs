using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// DeviceTests only: disposable sessions for verify-session-close.cjs, between two accounts of their own
// («sessao-organiza» and «sessao-convidada», in session-close-accounts.json, never in accounts.json).
// The iPhone walkthrough accounts and sessions are never touched. Endpoints answer only on the Mac itself.
public static class SessionCloseFixture {
    const string File = "session-close-accounts.json";
    static readonly string[] Names = ["sessao-organiza", "sessao-convidada"];

    public static async Task EnsureAccounts(UserManager<User> users, string dataPath) {
        var path = Path.Combine(dataPath, File);
        var accounts = System.IO.File.Exists(path) ? JsonSerializer.Deserialize<List<TestAccount>>(await System.IO.File.ReadAllTextAsync(path))! : [];
        foreach (var name in Names) {
            var email = name + "@meepleboard.test";
            var account = accounts.SingleOrDefault(a => a.Email == email);
            if (account == null) { account = new(email, "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)), Guid.Empty); accounts.Add(account); }
            var user = await users.FindByEmailAsync(email);
            if (user == null) {
                user = new User("Teste-" + name, email, "Local") { EmailConfirmed = true };
                var r = await users.CreateAsync(user, account.Password); if (!r.Succeeded) throw new Exception("Session fixture account creation failed: " + string.Join(",", r.Errors.Select(e => e.Code)));
                await users.AddToRoleAsync(user, "User");
            }
            account.Id = user.Id;
        }
        await System.IO.File.WriteAllTextAsync(path, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Map(WebApplication app, string dataPath) {
        var group = app.MapGroup("/device-test/session-close").AddEndpointFilter(async (context, next) =>
            context.HttpContext.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) ? await next(context) : Results.NotFound());
        async Task<(Guid Organizer, Guid Guest)> Ids() {
            var accounts = JsonSerializer.Deserialize<List<TestAccount>>(await System.IO.File.ReadAllTextAsync(Path.Combine(dataPath, File)))!;
            return (accounts.Single(a => a.Email == "sessao-organiza@meepleboard.test").Id, accounts.Single(a => a.Email == "sessao-convidada@meepleboard.test").Id);
        }
        // Fresh sessions: running (started 1 h ago), scheduled (in 2 days), due (past its automatic end, not yet stored)
        // and five «race» sessions whose automatic end is 90 s away. Previous fixture sessions go first.
        group.MapPost("/fixture", async (MeepleBoardDbContext db) => {
            var (organizer, guest) = await Ids();
            if (!await Clear(db, organizer)) return Results.Conflict(new { message = "Fixture sessions still have matches." });
            var now = DateTime.UtcNow;
            GameSession Add(string name, DateTime scheduled) {
                var session = new GameSession(name, organizer, "Mesa descartável", now.AddHours(1));
                Set(session, "ScheduledStartDate", scheduled);
                session.Players.Add(new GameSessionPlayer(session.Id, organizer, isOrganizer: true));
                var player = new GameSessionPlayer(session.Id, guest); player.Accept(); Set(player, "RespondedAt", scheduled.AddHours(-2));
                session.Players.Add(player);
                db.GameSessions.Add(session);
                return session;
            }
            var running = Add("Verificação: a decorrer", now.AddHours(-1));
            var scheduled = Add("Verificação: agendada", now.AddDays(2));
            var due = Add("Verificação: fim automático passado", now.AddHours(-25));
            var races = Enumerable.Range(1, 5).Select(i => Add($"Verificação: corrida {i}", now.AddHours(-24).AddSeconds(90))).ToList();
            await db.SaveChangesAsync();
            return Results.Ok(new { running = running.Id, scheduled = scheduled.Id, due = due.Id, dueEnd = due.AutoCloseAt,
                races = races.Select(r => new { id = r.Id, autoCloseAt = r.AutoCloseAt }) });
        });
        // The automatic close for one fixture session, through the same calls as SessionAutoCloseJob, at a given time.
        group.MapPost("/{id:guid}/auto", async (Guid id, DateTime at, IGameSessionRepository sessions) => {
            var (organizer, _) = await Ids();
            var session = await sessions.GetByIdWithPlayersForUpdateAsync(id);
            if (session == null || session.OrganizerId != organizer) return Results.NotFound();
            var stored = session.AutoClose(at.ToUniversalTime()) && await sessions.SaveCloseAsync(session);
            return Results.Ok(new { stored });
        });
        // State as stored in SQL (no cache): end, how it ended and who is in it.
        group.MapGet("/{id:guid}", async (Guid id, MeepleBoardDbContext db) => {
            var (organizer, _) = await Ids();
            var s = await db.GameSessions.AsNoTracking().Where(x => x.Id == id && x.OrganizerId == organizer)
                .Select(x => new { x.EndDate, x.ClosedAutomatically, x.IsCancelled, players = x.Players.Count, matches = x.Matches.Count }).SingleOrDefaultAsync();
            return s == null ? Results.NotFound() : Results.Ok(s);
        });
        group.MapDelete("/fixture", async (MeepleBoardDbContext db) => {
            var (organizer, _) = await Ids();
            return await Clear(db, organizer) ? Results.NoContent() : Results.Conflict(new { message = "Fixture sessions still have matches." });
        });
    }

    // Only sessions organized by the fixture account, and only once their matches were deleted through the API.
    static async Task<bool> Clear(MeepleBoardDbContext db, Guid organizer) {
        var old = await db.GameSessions.Include(s => s.Players).Where(s => s.OrganizerId == organizer).ToListAsync();
        if (old.Count == 0) return true;
        var ids = old.Select(s => s.Id).ToList();
        if (await db.Matches.AnyAsync(m => m.GameSessionId != null && ids.Contains(m.GameSessionId.Value))) return false;
        db.GameSessions.RemoveRange(old);
        await db.SaveChangesAsync();
        return true;
    }

    static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
}
