// Deleting one's own account: domain rules, storage retries and notifications in UserService, and the HTTP contract of
// DELETE /users/me and of the support link pages. Production entity/service/controllers; in-memory fakes; no SQL.
// The SQL behaviour (what is deleted, kept or neutralised) is verified by tools/DeviceTestApi/verify-own-account-deletion.cjs.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AutoMapper;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.AutoMapper;
using MeepleBoardApi.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var passed = 0;
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
Task Sync(string name, Action action) => Check(name, () => { action(); return Task.CompletedTask; });
UserService.PhotoRetryDelays = [TimeSpan.Zero, TimeSpan.Zero];

await Sync("neutral titles are markers in no language; anonymising clears the last activity", () => {
    Assert(User.DeletedSessionName(new DateTime(2026, 10, 17, 21, 30, 0, DateTimeKind.Utc)) == "deleted:session:2026-10-17", "session marker");
    Assert(User.DeletedCampaignName == "deleted:campaign", "campaign marker");
    var user = new User("Pessoa", "pessoa@example.test", "Local");
    Set(user, "LastActiveAt", DateTime.UtcNow);
    user.Anonymize(User.DeletedUserName(user.Id));
    Assert(user.LastActiveAt == null && user.Email == null && user.DeletedAt != null, "last activity or email kept");
});
await Sync("reports by a deleted account lose their free text when resolved; other reports keep it", () => {
    var deleted = new User("Apagada", "x@example.test", "Local"); deleted.Anonymize(User.DeletedUserName(deleted.Id));
    var byDeleted = new ExperienceReport(Guid.NewGuid(), deleted.Id, "Abusive", "texto da pessoa"); Set(byDeleted, "Reporter", deleted);
    var byActive = new ExperienceReport(Guid.NewGuid(), Guid.NewGuid(), "Abusive", "texto de outra pessoa"); Set(byActive, "Reporter", new User("Ativa", "y@example.test", "Local"));
    Assert(byDeleted.Detail != null, "open report keeps its text");
    byDeleted.Resolve(Guid.NewGuid(), "keep"); byActive.Resolve(Guid.NewGuid(), "keep");
    Assert(byDeleted.Detail == null && byActive.Detail == "texto de outra pessoa", "experience report text");
    var resource = new GameResourceReport(Guid.NewGuid(), deleted.Id, "BrokenLink", "comentário"); Set(resource, "User", deleted);
    resource.Resolve(Guid.NewGuid());
    Assert(resource.Comment == null, "resource report text");
});

await Check("storage: two failures are retried and succeed; three keep trying in a background job; the account is deleted either way", async () => {
    var storage = new Storage { FailFirst = 2 };
    var jobs = new Jobs(); var users = new Users();
    var service = Service(users, storage, new Push(), jobs);
    await service.DeleteAsync(users.Id, new Dictionary<Guid, Guid> { [Guid.NewGuid()] = Guid.NewGuid() });
    Assert(users.Transfers!.Count == 1, "transfers not passed to the repository");
    Assert(storage.Attempts == 3 && storage.Deleted.Count == 1 && jobs.Created.Count == 0, $"retry: {storage.Attempts} attempts, {jobs.Created.Count} jobs");
    storage = new Storage { FailFirst = 10 }; jobs = new Jobs(); users = new Users();
    await Service(users, storage, new Push(), jobs).DeleteAsync(users.Id);
    Assert(storage.Attempts == 3 && storage.Deleted.Count == 0 && jobs.Created.SequenceEqual(["PhotoDeletionJob:" + users.Photo]), "job not scheduled with the reference only");
    Assert(users.Anonymised == 1, "account not deleted");
});
await Check("a failing push to the guests of cancelled sessions never stops the deletion", async () => {
    var users = new Users { Tokens = ["ExponentPushToken[a]"] }; var push = new Push { Fail = true };
    await Service(users, new Storage(), push, new Jobs()).DeleteAsync(users.Id);
    Assert(push.Calls == 1 && users.Anonymised == 1, "push failure stopped it");
    users = new Users { Tokens = [] }; push = new Push();
    await Service(users, new Storage(), push, new Jobs()).DeleteAsync(users.Id);
    Assert(push.Calls == 0, "no cancelled session, no push");
});
await Check("each new campaign manager is told, without technical markers; a failing push changes nothing", async () => {
    var users = new Users { Managers = [new("ExponentPushToken[m1]", User.DeletedCampaignName), new("ExponentPushToken[m2]", "Campanha de Ana")] }; var push = new Push();
    await Service(users, new Storage(), push, new Jobs()).DeleteAsync(users.Id);
    Assert(push.Bodies.SequenceEqual(["Passaste a gerir uma campanha.", "Passaste a gerir a campanha \"Campanha de Ana\"."]), string.Join(" | ", push.Bodies));
    users = new Users { Managers = [new("ExponentPushToken[m1]", "Campanha")] }; push = new Push { Fail = true };
    await Service(users, new Storage(), push, new Jobs()).DeleteAsync(users.Id);
    Assert(users.Anonymised == 1 && push.Calls == 1, "failing push stopped the deletion");
});
await Check("an account already deleted is not found: nothing runs again", async () => {
    var users = new Users(); var storage = new Storage();
    await Service(users, storage, new Push(), new Jobs()).DeleteAsync(users.Id);
    users.Deleted = true;
    try { await Service(users, storage, new Push(), new Jobs()).DeleteAsync(users.Id); throw new Exception("expected KeyNotFoundException"); }
    catch (KeyNotFoundException) { }
    Assert(users.Anonymised == 1 && storage.Deleted.Count == 1, "repeat had effects");
});

// HTTP: DELETE /MeepleBoard/users/me and the support pages, over a fake IAccountDeletionService.
var deletion = new Deletion();
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
builder.Services.AddLogging();
builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddApplicationPart(typeof(UserController).Assembly);
builder.Services.AddSingleton<IAccountDeletionService>(deletion);
builder.Services.AddSingleton<IUserService>(Stub.Create<IUserService>());
await using var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
async Task<(HttpStatusCode Status, string Body)> Send(HttpMethod method, string path, object? body, Guid? user, string? role = null, HttpContent? content = null) {
    using var request = new HttpRequestMessage(method, path) { Content = content ?? (body == null ? null : JsonContent.Create(body)) };
    if (user.HasValue) request.Headers.Add("X-Fixture-User", user.Value.ToString());
    if (role != null) request.Headers.Add("X-Fixture-Role", role);
    using var response = await client.SendAsync(request);
    return (response.StatusCode, await response.Content.ReadAsStringAsync());
}
var me = Guid.NewGuid();
await Check("DELETE /users/me: 204 deleted, 400 wrong password, 429 locked, 404 gone, 401 anonymous; transfers passed through", async () => {
    var campaign = Guid.NewGuid(); var manager = Guid.NewGuid();
    foreach (var (outcome, status) in new[] { (OwnAccountDeletionOutcome.Deleted, HttpStatusCode.NoContent), (OwnAccountDeletionOutcome.WrongPassword, HttpStatusCode.BadRequest),
        (OwnAccountDeletionOutcome.LockedOut, HttpStatusCode.TooManyRequests), (OwnAccountDeletionOutcome.NotFound, HttpStatusCode.NotFound) }) {
        deletion.Next = outcome;
        var r = await Send(HttpMethod.Delete, "/MeepleBoard/users/me", new { password = "segredo", campaignTransfers = new[] { new { campaignId = campaign, newManagerId = manager } } }, me);
        Assert(r.Status == status, $"{outcome}: {r.Status} {r.Body}");
    }
    Assert(deletion.LastUser == me && deletion.LastPassword == "segredo" && deletion.LastTransfers![campaign] == manager, "request not passed through");
    var calls = deletion.Calls;
    Assert((await Send(HttpMethod.Delete, "/MeepleBoard/users/me", new { password = "x" }, null)).Status == HttpStatusCode.Unauthorized, "anonymous");
    Assert((await Send(HttpMethod.Delete, "/MeepleBoard/users/me", new { password = "x", campaignTransfers = new[] { new { campaignId = campaign, newManagerId = manager }, new { campaignId = campaign, newManagerId = me } } }, me)).Status == HttpStatusCode.BadRequest, "two managers for one campaign");
    deletion.Throw = new ArgumentException("O novo gestor tem de ser um membro ativo de uma campanha que criaste.");
    Assert((await Send(HttpMethod.Delete, "/MeepleBoard/users/me", new { password = "x" }, me)).Status == HttpStatusCode.BadRequest, "invalid manager");
    deletion.Throw = null;
    Assert(deletion.Calls == calls + 1, "refused requests reached the service");
});
await Check("support: only administrators register a request; the answer never says whether the account exists", async () => {
    Assert((await Send(HttpMethod.Post, "/MeepleBoard/admin/account-deletion/requests", new { email = "a@example.test" }, me)).Status == HttpStatusCode.Forbidden, "non-admin");
    var r = await Send(HttpMethod.Post, "/MeepleBoard/admin/account-deletion/requests", new { email = "a@example.test" }, me, "Admin");
    Assert(r.Status == HttpStatusCode.Accepted && deletion.RequestedEmail == "a@example.test" && deletion.ConfirmationUrl!.EndsWith("/MeepleBoard/account-deletion/confirm"), "request");
    Assert(r.Body.Contains("Se o endereço pertencer a uma conta"), "answer reveals existence");
});
await Check("support link: GET only shows the page (scanners open links); POST deletes; used or invalid links change nothing", async () => {
    deletion.ValidToken = "token-certo";
    var page = await Send(HttpMethod.Get, $"/MeepleBoard/account-deletion/confirm?u={me}&t=token-certo", null, null);
    Assert(page.Status == HttpStatusCode.OK && page.Body.Contains("<form method=\"post\"") && deletion.Confirmed == 0, "GET deleted or showed no form");
    var bad = await Send(HttpMethod.Get, $"/MeepleBoard/account-deletion/confirm?u={me}&t=errado", null, null);
    Assert(bad.Status == HttpStatusCode.BadRequest && bad.Body.Contains("Ligação inválida ou expirada"), "invalid link page");
    var form = (string t) => new FormUrlEncodedContent(new Dictionary<string, string> { ["u"] = me.ToString(), ["t"] = t });
    var done = await Send(HttpMethod.Post, "/MeepleBoard/account-deletion/confirm", null, null, content: form("token-certo"));
    Assert(done.Status == HttpStatusCode.OK && done.Body.Contains("Conta eliminada") && deletion.Confirmed == 1, "POST did not delete");
    var again = await Send(HttpMethod.Post, "/MeepleBoard/account-deletion/confirm", null, null, content: form("token-certo"));
    Assert(again.Status == HttpStatusCode.BadRequest && deletion.Confirmed == 1, "a used link deleted again");
});
Console.WriteLine($"{passed} isolated account deletion tests passed.");
await app.StopAsync();

static UserService Service(Users users, Storage storage, Push push, Jobs jobs) =>
    new(users.Proxy, Stub.Create<IMatchRepository>(), new MapperConfiguration(c => c.AddProfile<MappingEntityToDto>()).CreateMapper(), storage, NullLogger<UserService>.Instance, push, jobs);
static void Set(object target, string property, object? value) =>
    target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

// The repository as UserService sees it: one account with one photo; cancelled sessions give guest tokens.
public class Users {
    public Guid Id = Guid.NewGuid(); public string Photo = "https://res.cloudinary.com/x/image/authenticated/" + Guid.NewGuid() + ".jpg";
    public bool Deleted; public int Anonymised; public IReadOnlyDictionary<Guid, Guid>? Transfers; public List<string> Tokens = []; public List<SuccessorNotice> Managers = [];
    public IUserRepository Proxy => Stub.Create<IUserRepository>((method, args) => method.Name switch {
        "GetByIdAsync" => Task.FromResult<User?>(Account()),
        "AnonymizeAsync" => Anonymise((IReadOnlyDictionary<Guid, Guid>?)args[2]),
        _ => throw new NotSupportedException(method.Name),
    });
    User Account() { var u = new User("Pessoa", "p@example.test", "Local"); typeof(User).GetProperty("Id")!.SetValue(u, Id); if (Deleted) u.Anonymize(User.DeletedUserName(Id)); return u; }
    Task<AccountDeletionResult> Anonymise(IReadOnlyDictionary<Guid, Guid>? transfers) { Anonymised++; Transfers = transfers; return Task.FromResult(new AccountDeletionResult([], [Photo], Tokens, Managers)); }
}
public class Storage : IPhotoStorageService {
    public int FailFirst, Attempts; public List<string> Deleted = [];
    public Task DeleteAsync(string url, CancellationToken ct = default) { Attempts++; if (Attempts <= FailFirst) throw new IOException("storage down"); Deleted.Add(url); return Task.CompletedTask; }
    public Task<string> UploadAsync(Stream s, string n, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(byte[] Content, string ContentType)> ReadProtectedAsync(string u, CancellationToken ct = default) => throw new NotSupportedException();
}
public class Push : INotificationService {
    public bool Fail; public int Calls; public List<string> Bodies = [];
    public Task SendToManyAsync(IEnumerable<string> tokens, string title, string body, object? data = null, CancellationToken ct = default) { Calls++; Bodies.Add(body); return Fail ? throw new HttpRequestException("push down") : Task.CompletedTask; }
    public Task SendAsync(string token, string title, string body, object? data = null, CancellationToken ct = default) { Calls++; Bodies.Add(body); return Fail ? throw new HttpRequestException("push down") : Task.CompletedTask; }
    public Task NotifyMatchCreatedAsync(Guid id, string name, IEnumerable<string> tokens, CancellationToken ct = default) => throw new NotSupportedException();
    public Task NotifyJournalEntryAddedAsync(Guid id, string name, string evaluator, IEnumerable<string> tokens, CancellationToken ct = default) => throw new NotSupportedException();
    public Task NotifyMatchJournalClosedAsync(Guid id, string name, IEnumerable<string> tokens, CancellationToken ct = default) => throw new NotSupportedException();
    public Task NotifyCampaignInviteAsync(string token, string name, string inviter, CancellationToken ct = default) => throw new NotSupportedException();
}
public class Jobs : IBackgroundJobClient {
    public List<string> Created = [];
    public string Create(Job job, IState state) { Created.Add(job.Type.Name + ":" + string.Join(",", job.Args)); return "job"; }
    public bool ChangeState(string jobId, IState state, string expectedState) => false;
}
public class Deletion : IAccountDeletionService {
    public OwnAccountDeletionOutcome Next; public Exception? Throw; public int Calls, Confirmed;
    public Guid LastUser; public string? LastPassword, RequestedEmail, ConfirmationUrl, ValidToken; public IReadOnlyDictionary<Guid, Guid>? LastTransfers;
    readonly HashSet<string> used = [];
    public Task<OwnAccountDeletionOutcome> DeleteOwnAsync(Guid userId, string? password, IReadOnlyDictionary<Guid, Guid> transfers, CancellationToken ct = default) {
        Calls++; if (Throw != null) throw Throw; (LastUser, LastPassword, LastTransfers) = (userId, password, transfers); return Task.FromResult(Next);
    }
    public Task RequestThroughSupportAsync(string email, string url, CancellationToken ct = default) { (RequestedEmail, ConfirmationUrl) = (email, url); return Task.CompletedTask; }
    public Task<bool> IsValidLinkAsync(Guid userId, string token, CancellationToken ct = default) => Task.FromResult(token == ValidToken && !used.Contains(token));
    public Task<bool> ConfirmThroughSupportAsync(Guid userId, string token, CancellationToken ct = default) {
        if (token != ValidToken || !used.Add(token)) return Task.FromResult(false); Confirmed++; return Task.FromResult(true);
    }
}
public class Stub : DispatchProxy {
    Func<MethodInfo, object?[], object?>? handler;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => handler != null ? handler(method!, args!) : throw new NotSupportedException(method!.Name + " is not used by these tests.");
    public static T Create<T>(Func<MethodInfo, object?[], object?>? handler = null) where T : class { var p = DispatchProxy.Create<T, Stub>(); ((Stub)(object)p).handler = handler; return p; }
}
public class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (!Guid.TryParse(Request.Headers["X-Fixture-User"], out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()) };
        if (Request.Headers.TryGetValue("X-Fixture-Role", out var role)) claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
