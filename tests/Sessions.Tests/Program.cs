// Session times, automatic end and organizer end time: domain rules, the job and the HTTP endpoint.
// Production entity/service/controller/job; in-memory repositories; no configuration, SQL or Hangfire.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Job;
using MeepleBoardApi.Controllers;
using MeepleBoardApi.Services.Mapping.AutoMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var passed = 0;
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
Task Sync(string name, Action action) => Check(name, () => { action(); return Task.CompletedTask; });
var now = DateTime.UtcNow;

await Sync("no accepted guest: start and end known from the scheduled time, but never active nor auto-closed (cleanup job's case)", () => {
    var scheduled = now.AddHours(-30);
    var s = Sessions.Create(scheduled);
    Assert(s.AutoCloseAt == scheduled.AddHours(24) && s.LatestCloseAt == scheduled.AddHours(48), "times must not depend on guests");
    Assert(s.Status == "Upcoming" && !s.AutoClose(now) && s.EndDate == null, "must not close a session that never ran");
});
await Sync("accepted before the scheduled time: starts at the scheduled time, ends 24 h later, can extend to 48 h", () => {
    var start = now.AddHours(-2);
    var s = Sessions.Create(start, accepted: [start.AddHours(-5)]);
    Assert(s.AutoCloseAt == start.AddHours(24) && s.LatestCloseAt == start.AddHours(48), "wrong times");
    Assert(s.Status == "Active", "should be active");
});
await Sync("an acceptance after the scheduled time activates the session but does not move its start or end", () => {
    var scheduled = now.AddHours(-10);
    var s = Sessions.Create(scheduled, accepted: [now.AddHours(-3), now.AddHours(-1)]);
    Assert(s.Status == "Active" && s.AutoCloseAt == scheduled.AddHours(24) && s.LatestCloseAt == scheduled.AddHours(48), "late acceptance moved the times");
});
await Sync("legacy acceptance without a date: same rule from the scheduled time", () => {
    var scheduled = now.AddDays(-5);
    var s = Sessions.Create(scheduled, accepted: [null]);
    Assert(s.AutoCloseAt == scheduled.AddHours(24), "legacy end");
    Assert(s.Status == "Closed", "past its end the session reads as closed before the job persists it");
});
await Sync("response deadline does not change the end time", () => {
    var scheduled = now.AddHours(-2);
    var s = Sessions.Create(scheduled, accepted: [scheduled.AddHours(-1)], deadline: scheduled.AddHours(-20));
    Assert(s.AutoCloseAt == scheduled.AddHours(24), "deadline must be independent");
});
await Sync("automatic close records the due time once and keeps players and matches", () => {
    var scheduled = now.AddDays(-3);
    var s = Sessions.Create(scheduled, accepted: [scheduled.AddHours(-1)]);
    var match = (Match)Activator.CreateInstance(typeof(Match), true)!; s.Matches.Add(match);
    var players = s.Players.Count;
    Assert(s.AutoClose(now) && s.EndDate == scheduled.AddHours(24) && s.ClosedAutomatically, "first close");
    Assert(!s.AutoClose(now.AddHours(5)) && s.EndDate == scheduled.AddHours(24), "second run must not change anything");
    Assert(s.Status == "Closed" && s.Players.Count == players && s.Matches.Single() == match, "history kept");
});
await Sync("not yet due: nothing happens", () => {
    var s = Sessions.Create(now.AddHours(-1), accepted: [now.AddHours(-2)]);
    Assert(!s.AutoClose(now) && s.EndDate == null && s.Status == "Active", "closed too early");
});
await Sync("cancelled sessions are never auto-closed", () => {
    var s = Sessions.Create(now.AddDays(-3), accepted: [now.AddDays(-4)]);
    Sessions.Set(s, "IsCancelled", true);
    Assert(!s.AutoClose(now) && s.EndDate == null, "cancelled session closed");
});
await Sync("organizer end time: later or earlier within 48 h, never past or beyond", () => {
    var start = now.AddHours(-2);
    var s = Sessions.Create(start, accepted: [start.AddHours(-1)]);
    s.SetClosesAt(start.AddHours(40), now);
    Assert(s.AutoCloseAt == start.AddHours(40), "extend");
    s.SetClosesAt(now.AddHours(2), now);
    Assert(s.AutoCloseAt == now.AddHours(2), "end earlier");
    Throws<ArgumentException>(() => s.SetClosesAt(start.AddHours(48).AddMinutes(5), now), "beyond 48 h");
    Throws<ArgumentException>(() => s.SetClosesAt(now.AddSeconds(30), now), "now or past");
    s.SetClosesAt(start.AddHours(48), now);
    Assert(s.AutoCloseAt == start.AddHours(48), "exactly 48 h");
    var upcoming = Sessions.Create(now.AddHours(3), accepted: [now.AddHours(-1)]);
    Throws<InvalidOperationException>(() => upcoming.SetClosesAt(now.AddHours(10), now), "not active");
});
await Sync("closing by hand after the due time keeps the due time", () => {
    var scheduled = now.AddDays(-2);
    var s = Sessions.Create(scheduled, accepted: [scheduled.AddHours(-1)]);
    s.CloseSession();
    Assert(s.EndDate == scheduled.AddHours(24) && s.ClosedAutomatically, "late manual close");
    var fresh = Sessions.Create(now.AddHours(-1), accepted: [now.AddHours(-2)]);
    fresh.CloseSession();
    Assert(fresh.EndDate >= now && !fresh.ClosedAutomatically, "early manual close");
});
await Check("job closes due sessions, saves once and is idempotent", async () => {
    var due = Sessions.Create(now.AddDays(-2), accepted: [now.AddDays(-3)]);
    var running = Sessions.Create(now.AddHours(-1), accepted: [now.AddHours(-2)]);
    var repo = new Repository([due, running]);
    var job = new SessionAutoCloseJob(repo, NullLogger<SessionAutoCloseJob>.Instance);
    Assert(await job.ExecuteAsync() == 1 && repo.Saves == 1 && due.EndDate != null && running.EndDate == null, "first run");
    Assert(await job.ExecuteAsync() == 0 && repo.Saves == 1, "second run must not save again");
});

// HTTP: PUT /MeepleBoard/session/{id}/close-time
var organizer = Guid.NewGuid(); var guest = Guid.NewGuid();
var active = Sessions.Create(now.AddHours(-2), accepted: [now.AddHours(-3)], organizer: organizer, guest: guest);
var waiting = Sessions.Create(now.AddHours(5), accepted: [now.AddHours(-1)], organizer: organizer, guest: guest);
var repository = new Repository([active, waiting]);
var mapper = new MapperConfiguration(c => c.AddProfile<MappingEntityToDto>()).CreateMapper();
var service = new GameSessionService(repository, Stub.Create<IGameSessionPlayerRepository>(), Stub.Create<IUserRepository>(), mapper, Stub.Create<IFriendshipRepository>());
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
builder.Services.AddLogging();
builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddApplicationPart(typeof(GameSessionController).Assembly);
builder.Services.AddSingleton<IGameSessionService>(service);
await using var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
async Task<string> Put(Guid session, DateTime closesAt, Guid? user, HttpStatusCode expected) {
    using var request = new HttpRequestMessage(HttpMethod.Put, $"/MeepleBoard/session/{session}/close-time") { Content = JsonContent.Create(new { closesAt }) };
    if (user.HasValue) request.Headers.Add("X-Fixture-User", user.Value.ToString());
    using var response = await client.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    Assert(response.StatusCode == expected, $"expected {expected}, got {response.StatusCode}: {text}");
    return text;
}
await Check("organizer extends; response carries start, end and limit", async () => {
    var target = active.ScheduledStartDate.AddHours(30);
    using var json = JsonDocument.Parse(await Put(active.Id, target, organizer, HttpStatusCode.OK));
    var root = json.RootElement;
    Assert(root.GetProperty("autoCloseAt").GetDateTime() == target && root.GetProperty("closeTimeChosen").GetBoolean(), "end not returned");
    Assert(root.GetProperty("latestCloseAt").GetDateTime() == active.ScheduledStartDate.AddHours(48), "limit not returned");
    Assert(root.GetProperty("scheduledStartDate").GetDateTime() == active.ScheduledStartDate, "start not returned");
    Assert(repository.Saves == 1, "should persist once");
});
await Check("guest, anonymous, beyond 48 h, not started and unknown are refused without changes", async () => {
    var before = active.AutoCloseAt;
    await Put(active.Id, now.AddHours(10), guest, HttpStatusCode.Forbidden);
    await Put(active.Id, now.AddHours(10), null, HttpStatusCode.Unauthorized);
    await Put(active.Id, active.ScheduledStartDate.AddHours(49), organizer, HttpStatusCode.BadRequest);
    await Put(waiting.Id, now.AddHours(10), organizer, HttpStatusCode.Conflict);
    await Put(Guid.NewGuid(), now.AddHours(10), organizer, HttpStatusCode.NotFound);
    Assert(active.AutoCloseAt == before && repository.Saves == 1, "refused requests changed state");
});
Console.WriteLine($"{passed} isolated session start/end tests passed.");
await app.StopAsync();

static void Throws<T>(Action action, string message) where T : Exception {
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + message);
}

public static class Sessions {
    public static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    // Builds a session as SQL would return it; scheduled times may be in the past.
    public static GameSession Create(DateTime scheduled, DateTime?[]? accepted = null, DateTime? deadline = null, Guid? organizer = null, Guid? guest = null) {
        var owner = organizer ?? Guid.NewGuid();
        var session = new GameSession("Fixture session", owner, null, DateTime.UtcNow.AddHours(1));
        Set(session, "ScheduledStartDate", scheduled);
        Set(session, "ResponseDeadline", deadline);
        session.Players.Add(new GameSessionPlayer(session.Id, owner, isOrganizer: true));
        foreach (var (respondedAt, index) in (accepted ?? []).Select((v, i) => (v, i))) {
            var player = new GameSessionPlayer(session.Id, index == 0 && guest.HasValue ? guest.Value : Guid.NewGuid());
            player.Accept(); Set(player, "RespondedAt", respondedAt);
            session.Players.Add(player);
        }
        return session;
    }
}
public class Repository(List<GameSession> sessions) : IGameSessionRepository {
    public int Saves;
    public Task<IReadOnlyList<GameSession>> GetListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GameSession>>(sessions);
    public Task<GameSession?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default) => Task.FromResult(sessions.FirstOrDefault(s => s.Id == id));
    public Task<GameSession?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default) => GetByIdWithDetailsAsync(id, ct);
    public Task<GameSession?> GetByIdWithPlayersForUpdateAsync(Guid id, CancellationToken ct = default) => GetByIdWithDetailsAsync(id, ct);
    public Task<IReadOnlyList<GameSession>> GetOpenForUpdateAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GameSession>>(sessions.Where(s => s.EndDate == null && !s.IsCancelled).ToList());
    public Task AddAsync(GameSession session, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(GameSession session, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
}
public class Stub : DispatchProxy {
    protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException(method!.Name + " is not used by these tests.");
    public static T Create<T>() where T : class => DispatchProxy.Create<T, Stub>();
}
public class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Guid.TryParse(Request.Headers["X-Fixture-User"], out var user)
        ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], Scheme.Name)), Scheme.Name)) : AuthenticateResult.NoResult());
}
