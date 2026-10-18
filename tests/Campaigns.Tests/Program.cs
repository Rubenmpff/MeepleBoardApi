// Campaign management permissions (docs/eliminacao-contas.md, «Gestão e autoria das campanhas»): only whoever manages
// (Campaign.CreatorId) removes members, completes, abandons or deletes; complete/abandon only from «Ativa»; inviting and
// editing stay with accepted members; taking a match out needs to have played it or to manage; leaving is refused to the
// manager; notifications never show a technical marker. Production service and controller; in-memory fakes; no SQL.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.Interfaces;
using MeepleBoardApi.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var passed = 0;
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }

Guid author = Guid.NewGuid(), manager = Guid.NewGuid(), member = Guid.NewGuid(), other = Guid.NewGuid(), outsider = Guid.NewGuid(), former = Guid.NewGuid();
var world = new World();
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
builder.Services.AddLogging();
builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddApplicationPart(typeof(CampaignController).Assembly);
builder.Services.AddSingleton<ICampaignService>(_ => new CampaignService(world.Campaigns, world.Matches, world.Users, Stub.Create<IGameRepository>(), world.Push, Stub.Create<IPhotoStorageService>(), NullLogger<CampaignService>.Instance, AllFriends.Create(), NoBlocks.Create()));
await using var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
async Task<HttpStatusCode> Send(HttpMethod method, string path, Guid user, object? body = null) {
    using var request = new HttpRequestMessage(method, "/MeepleBoard/campaigns" + path) { Content = body == null ? null : JsonContent.Create(body) };
    request.Headers.Add("X-Fixture-User", user.ToString());
    using var response = await client.SendAsync(request);
    return response.StatusCode;
}
// A campaign written by «author» (since deleted), now managed by «manager», with two other members and an ex-member.
Campaign Fresh(string name = "Campanha") {
    var c = new Campaign(name, Guid.NewGuid(), author);
    c.Members.Add(new CampaignMember(c.Id, author, isCreator: true));
    foreach (var u in new[] { manager, member, other }) { var m = new CampaignMember(c.Id, u); m.Accept(); c.Members.Add(m); }
    var left = new CampaignMember(c.Id, former); left.Accept(); left.Leave(); c.Members.Add(left);
    Set(c, "CreatorId", manager);
    world.Campaign = c; return c;
}

await Check("only the manager completes or abandons, and only from «Ativa»; the status otherwise stays", async () => {
    var c = Fresh();
    foreach (var u in new[] { member, author, outsider, former }) {
        Assert(await Send(HttpMethod.Post, $"/{c.Id}/complete", u) == HttpStatusCode.Forbidden, "complete by " + u);
        Assert(await Send(HttpMethod.Post, $"/{c.Id}/abandon", u) == HttpStatusCode.Forbidden, "abandon by " + u);
    }
    Assert(c.Status == CampaignStatus.Active && world.Saves == 0, "a refused request changed the campaign");
    Assert(await Send(HttpMethod.Post, $"/{c.Id}/complete", manager) == HttpStatusCode.NoContent && c.Status == CampaignStatus.Completed, "manager completes");
    Assert(await Send(HttpMethod.Post, $"/{c.Id}/abandon", manager) == HttpStatusCode.Conflict && c.Status == CampaignStatus.Completed, "abandon a completed campaign");
    Assert(await Send(HttpMethod.Post, $"/{c.Id}/complete", manager) == HttpStatusCode.Conflict, "complete twice");
    c = Fresh();
    Assert(await Send(HttpMethod.Post, $"/{c.Id}/abandon", manager) == HttpStatusCode.NoContent && c.Status == CampaignStatus.Abandoned, "manager abandons");
    Assert(await Send(HttpMethod.Post, $"/{c.Id}/complete", manager) == HttpStatusCode.Conflict && c.Status == CampaignStatus.Abandoned, "complete an abandoned campaign");
});
await Check("only the manager deletes the campaign", async () => {
    var c = Fresh();
    foreach (var u in new[] { member, author, outsider }) Assert(await Send(HttpMethod.Delete, $"/{c.Id}", u) == HttpStatusCode.Forbidden, "delete by " + u);
    Assert(world.Deleted == 0, "deleted by a refused request");
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}", manager) == HttpStatusCode.NoContent && world.Deleted == 1, "manager deletes");
});
await Check("only the manager removes members; nobody removes the manager or the original author", async () => {
    var c = Fresh();
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}/members/{other}", member) == HttpStatusCode.Forbidden, "member removes another");
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}/members/{other}", outsider) == HttpStatusCode.Forbidden, "outsider removes");
    Assert(c.Members.Single(m => m.UserId == other).Status == CampaignMemberStatus.Accepted, "refused removal changed the member");
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}/members/{other}", manager) == HttpStatusCode.NoContent, "manager removes");
    Assert(c.Members.Single(m => m.UserId == other).Status == CampaignMemberStatus.Removed, "not removed");
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}/members/{author}", manager) == HttpStatusCode.Conflict, "the original author stays (authorship)");
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}/members/{manager}", manager) == HttpStatusCode.Conflict, "the manager cannot remove themselves");
    Assert(world.Push.Texts.Any(t => t.Contains("Foste removido da campanha")), "removal notice");
});
await Check("inviting and editing name/notes stay with accepted members; others are refused", async () => {
    var c = Fresh();
    Assert(await Send(HttpMethod.Put, $"/{c.Id}", member, new { name = "Nome de um membro", notes = "notas" }) == HttpStatusCode.NoContent && c.Name == "Nome de um membro", "member edits");
    Assert(await Send(HttpMethod.Put, $"/{c.Id}", former, new { name = "Ex-membro", notes = "x" }) == HttpStatusCode.Forbidden && c.Name == "Nome de um membro", "ex-member edits");
    Assert(await Send(HttpMethod.Post, $"/{c.Id}/members/invite", member, new { userId = outsider }) == HttpStatusCode.OK || c.Members.Any(m => m.UserId == outsider), "member invites");
    Assert(await Send(HttpMethod.Post, $"/{c.Id}/members/invite", former, new { userId = Guid.NewGuid() }) == HttpStatusCode.Forbidden, "ex-member invites");
});
await Check("taking a match out: whoever played it or the manager; others are refused", async () => {
    var c = Fresh();
    var match = world.MatchWith(member);
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}/matches/{match.Id}", other) == HttpStatusCode.Forbidden, "member who did not play");
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}/matches/{match.Id}", outsider) == HttpStatusCode.Forbidden, "outsider");
    Assert(world.RemovedMatches == 0, "refused removal reached the repository");
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}/matches/{match.Id}", member) == HttpStatusCode.NoContent, "player removes");
    Assert(await Send(HttpMethod.Delete, $"/{c.Id}/matches/{world.MatchWith(other).Id}", manager) == HttpStatusCode.NoContent, "manager removes a match they did not play");
    Assert(world.RemovedMatches == 2, "removals");
});
await Check("leaving: members may; the manager may not", async () => {
    var c = Fresh();
    Assert(await Send(HttpMethod.Post, $"/{c.Id}/members/leave", manager) == HttpStatusCode.Conflict, "manager leaves");
    Assert(await Send(HttpMethod.Post, $"/{c.Id}/members/leave", member) == HttpStatusCode.NoContent, "member leaves");
});
await Check("no notification shows a technical marker: neutral campaign names become plain wording", async () => {
    var c = Fresh(User.DeletedCampaignName);
    world.Push.Texts.Clear();
    await Send(HttpMethod.Delete, $"/{c.Id}/members/{other}", manager);
    Assert(world.Push.Texts.Single() == "Foste removido de uma campanha.", "removal: " + string.Join(" | ", world.Push.Texts));
    Assert(User.WithoutDeletedMarkers("Foi «deleted:campaign» de deleted:1a2b3c4d em deleted:session:2026-10-17") == "Foi «campanha sem título» de Conta eliminada em sessão", "last guard");
});
Console.WriteLine($"{passed} isolated campaign permission tests passed.");
await app.StopAsync();

static void Set(object target, string property, object? value) =>
    target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

public class World {
    public Campaign Campaign = null!; public int Saves, Deleted, RemovedMatches; public readonly Push Push = new();
    readonly Dictionary<Guid, Match> matches = [];
    public Match MatchWith(Guid player) {
        var m = new Match(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
        m.MatchPlayers.Add(new MatchPlayer(m.Id, player)); matches[m.Id] = m; return m;
    }
    public ICampaignRepository Campaigns => Stub.Create<ICampaignRepository>((method, args) => method.Name switch {
        "GetByIdForUpdateAsync" or "GetByIdWithDetailsAsync" => Task.FromResult<Campaign?>((Guid)args[0]! == Campaign.Id ? Campaign : null),
        "GetMemberAsync" => Task.FromResult(Campaign.Members.FirstOrDefault(m => m.UserId == (Guid)args[1]!)),
        "AddMemberAsync" => Add((CampaignMember)args[0]!),
        "SaveChangesAsync" => Task.FromResult(Saves++),
        "DeleteAsync" => Task.FromResult(Deleted++),
        "RemoveCampaignMatchAsync" => Task.FromResult(RemovedMatches++),
        "LeaveAsync" => Task.FromResult(Leave((Guid)args[1]!)),
        _ => throw new NotSupportedException(method.Name),
    });
    Task Add(CampaignMember m) { Campaign.Members.Add(m); return Task.CompletedTask; }
    CampaignLeaveOutcome Leave(Guid user) {
        if (Campaign.CreatorId == user) return CampaignLeaveOutcome.IsManager;
        var m = Campaign.Members.FirstOrDefault(x => x.UserId == user);
        if (m == null || m.Status != CampaignMemberStatus.Accepted) return CampaignLeaveOutcome.NotMember;
        m.Leave(); return CampaignLeaveOutcome.Left;
    }
    public IMatchRepository Matches => Stub.Create<IMatchRepository>((method, args) => method.Name == "GetByIdAsync"
        ? new ValueTask<Match?>(matches.GetValueOrDefault((Guid)args[0]!)) : throw new NotSupportedException(method.Name));
    public IUserRepository Users => Stub.Create<IUserRepository>((method, args) => method.Name == "GetByIdAsync"
        ? Task.FromResult<User?>(Person((Guid)args[0]!)) : throw new NotSupportedException(method.Name));
    static User Person(Guid id) { var u = new User("Pessoa", id + "@example.test", "Local"); typeof(User).GetProperty("Id")!.SetValue(u, id); typeof(User).GetProperty("ExpoPushToken")!.SetValue(u, "ExponentPushToken[" + id + "]"); return u; }
}
public class Push : INotificationService {
    public readonly List<string> Texts = [];
    public Task SendAsync(string token, string title, string body, object? data = null, CancellationToken ct = default) { Texts.Add(body); return Task.CompletedTask; }
    public Task SendToManyAsync(IEnumerable<string> tokens, string title, string body, object? data = null, CancellationToken ct = default) { Texts.Add(body); return Task.CompletedTask; }
    public Task NotifyCampaignInviteAsync(string token, string name, string inviter, CancellationToken ct = default) { Texts.Add($"invite:{name}"); return Task.CompletedTask; }
    public Task NotifyMatchCreatedAsync(Guid id, string name, IEnumerable<string> tokens, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyJournalEntryAddedAsync(Guid id, string name, string evaluator, IEnumerable<string> tokens, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyMatchJournalClosedAsync(Guid id, string name, IEnumerable<string> tokens, CancellationToken ct = default) => Task.CompletedTask;
}
public class Stub : DispatchProxy {
    Func<MethodInfo, object?[], object?>? handler;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => handler != null ? handler(method!, args!) : throw new NotSupportedException(method!.Name + " is not used by these tests.");
    public static T Create<T>(Func<MethodInfo, object?[], object?>? handler = null) where T : class { var p = DispatchProxy.Create<T, Stub>(); ((Stub)(object)p).handler = handler; return p; }
}
public class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Guid.TryParse(Request.Headers["X-Fixture-User"], out var user)
        ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], Scheme.Name)), Scheme.Name)) : AuthenticateResult.NoResult());
}


/// <summary>Community rules are covered by Community.Tests: here nobody has blocked anybody.</summary>
public class NoBlocks : System.Reflection.DispatchProxy
{
    public static MeepleBoard.Domain.Interfaces.ICommunityRepository Create() => Create<MeepleBoard.Domain.Interfaces.ICommunityRepository, NoBlocks>();
    protected override object? Invoke(System.Reflection.MethodInfo? m, object?[]? a) => m!.ReturnType == typeof(Task<bool>) ? Task.FromResult(false)
        : m.ReturnType == typeof(Task<IReadOnlyList<Guid>>) ? Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>())
        : throw new InvalidOperationException("Unexpected community call: " + m.Name);
}

/// <summary>Who may play together is covered by Community.Tests: here every pair is an accepted friendship.</summary>
public class AllFriends : System.Reflection.DispatchProxy
{
    public static IFriendshipRepository Create() => Create<IFriendshipRepository, AllFriends>();
    protected override object? Invoke(System.Reflection.MethodInfo? m, object?[]? a) => m!.Name == "ExistsAcceptedAsync" ? Task.FromResult(true)
        : throw new InvalidOperationException("Unexpected friendship call: " + m.Name);
}
