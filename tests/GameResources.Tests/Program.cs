// Game resource catalogue over HTTP: permissions, approval, language filters, reports and validation.
// Production controllers/service/entities; in-memory repository; no configuration or SQL connection.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.Interfaces;
using MeepleBoardApi.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

var game = new Game("Fixture game", "", null);
var store = new Store();
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
builder.Services.AddLogging();
builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddApplicationPart(typeof(GameResourcesController).Assembly);
builder.Services.AddSingleton<IGameResourceService>(new GameResourceService(store, Games.With(game)));
await using var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };

var user = Guid.NewGuid(); var other = Guid.NewGuid(); var admin = Guid.NewGuid();
var passed = 0;
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
async Task<JsonElement> Call(HttpMethod method, string path, object? body, Guid? who, HttpStatusCode expected, bool asAdmin = false) {
    using var request = new HttpRequestMessage(method, "/MeepleBoard" + path);
    if (who.HasValue) request.Headers.Add("X-Fixture-User", who.Value.ToString());
    if (asAdmin) request.Headers.Add("X-Fixture-Role", "Admin");
    if (body != null) request.Content = JsonContent.Create(body);
    using var response = await client.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    Assert(response.StatusCode == expected, $"{method} {path}: expected {expected}, got {response.StatusCode}: {text}");
    return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
}
object Resource(string url, string kind = "HowToPlay", string? language = "en", string? category = null, bool? free = null) =>
    new { kind, printCategory = category, language, title = "Title " + url[^4..], author = "Author", platform = "YouTube", url, compatibility = "Base game", isOfficial = false, isFree = free };
var list = (string language) => Call(HttpMethod.Get, $"/game/{game.Id}/resources?language={language}", null, user, HttpStatusCode.OK);

await Check("signed-out visitors cannot read or suggest; permissions tell who manages", async () => {
    await Call(HttpMethod.Get, $"/game/{game.Id}/resources", null, null, HttpStatusCode.Unauthorized);
    await Call(HttpMethod.Post, $"/game/{game.Id}/resources/suggestions", Resource("https://example.org/a001"), null, HttpStatusCode.Unauthorized);
    Assert(!(await Call(HttpMethod.Get, "/game-resources/permissions", null, user, HttpStatusCode.OK)).GetProperty("canManage").GetBoolean(), "user manages");
    Assert((await Call(HttpMethod.Get, "/game-resources/permissions", null, admin, HttpStatusCode.OK, asAdmin: true)).GetProperty("canManage").GetBoolean(), "admin cannot manage");
});
await Check("only administrators use the administration routes", async () => {
    await Call(HttpMethod.Post, $"/admin/game/{game.Id}/resources", Resource("https://example.org/a002"), user, HttpStatusCode.Forbidden);
    await Call(HttpMethod.Get, "/admin/game-resources", null, user, HttpStatusCode.Forbidden);
    await Call(HttpMethod.Get, "/admin/game-resources/reports", null, user, HttpStatusCode.Forbidden);
    Assert(store.Resources.Count == 0, "a refused request stored something");
});
Guid created = default;
await Check("administrator entries are public at once, unknown price stays unknown", async () => {
    var item = await Call(HttpMethod.Post, $"/admin/game/{game.Id}/resources", Resource("https://example.org/en01"), admin, HttpStatusCode.Created, asAdmin: true);
    created = item.GetProperty("id").GetGuid();
    Assert(item.GetProperty("status").GetString() == "Approved" && item.GetProperty("isFree").ValueKind == JsonValueKind.Null, "not approved or price invented");
    var page = await list("all");
    Assert(page.GetProperty("items").GetArrayLength() == 1 && page.GetProperty("total").GetInt32() == 1, "not public");
});
Guid suggestion = default;
await Check("suggestions wait for approval and are not public until approved", async () => {
    var item = await Call(HttpMethod.Post, $"/game/{game.Id}/resources/suggestions", Resource("https://example.org/pt01", "Rules", "pt"), user, HttpStatusCode.Created);
    suggestion = item.GetProperty("id").GetGuid();
    Assert(item.GetProperty("status").GetString() == "Pending", "suggestion should be pending");
    Assert((await list("all")).GetProperty("items").GetArrayLength() == 1, "pending suggestion became public");
    var pending = await Call(HttpMethod.Get, "/admin/game-resources?status=Pending", null, admin, HttpStatusCode.OK, asAdmin: true);
    Assert(pending.GetArrayLength() == 1 && pending[0].GetProperty("resource").GetProperty("id").GetGuid() == suggestion, "not in review queue");
    await Call(HttpMethod.Post, $"/admin/game-resources/{suggestion}/approve", null, user, HttpStatusCode.Forbidden);
    await Call(HttpMethod.Post, $"/admin/game-resources/{suggestion}/approve", null, admin, HttpStatusCode.OK, asAdmin: true);
    Assert((await list("all")).GetProperty("items").GetArrayLength() == 2, "approved suggestion not public");
});
await Check("rejected suggestions stay hidden, keep the note and free the address", async () => {
    var item = await Call(HttpMethod.Post, $"/game/{game.Id}/resources/suggestions", Resource("https://example.org/x001"), other, HttpStatusCode.Created);
    var id = item.GetProperty("id").GetGuid();
    var rejected = await Call(HttpMethod.Post, $"/admin/game-resources/{id}/reject", new { note = "Link for another edition" }, admin, HttpStatusCode.OK, asAdmin: true);
    Assert(rejected.GetProperty("status").GetString() == "Rejected", "not rejected");
    Assert((await list("all")).GetProperty("items").GetArrayLength() == 2, "rejected resource is public");
    var review = await Call(HttpMethod.Get, "/admin/game-resources?status=Rejected", null, admin, HttpStatusCode.OK, asAdmin: true);
    Assert(review[0].GetProperty("reviewNote").GetString() == "Link for another edition", "note lost");
    await Call(HttpMethod.Post, $"/game/{game.Id}/resources/suggestions", Resource("https://example.org/x001"), other, HttpStatusCode.Created);
});
await Check("invalid input is refused: duplicates, http, 3D without category, category elsewhere, bad language", async () => {
    await Call(HttpMethod.Post, $"/game/{game.Id}/resources/suggestions", Resource("https://example.org/en01"), user, HttpStatusCode.Conflict);
    await Call(HttpMethod.Post, $"/game/{game.Id}/resources/suggestions", Resource("http://example.org/plain"), user, HttpStatusCode.BadRequest);
    await Call(HttpMethod.Post, $"/game/{game.Id}/resources/suggestions", Resource("https://example.org/m001", "PrintModel", null), user, HttpStatusCode.BadRequest);
    await Call(HttpMethod.Post, $"/game/{game.Id}/resources/suggestions", Resource("https://example.org/m002", "Rules", "en", "Organizer"), user, HttpStatusCode.BadRequest);
    await Call(HttpMethod.Post, $"/game/{game.Id}/resources/suggestions", Resource("https://example.org/m003", "Rules", "por"), user, HttpStatusCode.BadRequest);
    await Call(HttpMethod.Post, $"/game/{Guid.NewGuid()}/resources/suggestions", Resource("https://example.org/m004"), user, HttpStatusCode.NotFound);
    await Call(HttpMethod.Get, $"/game/{game.Id}/resources?language=portuguese", null, user, HttpStatusCode.BadRequest);
});
await Check("language filter: chosen language plus language-independent items; all; counts per language and kind", async () => {
    await Call(HttpMethod.Post, $"/admin/game/{game.Id}/resources", Resource("https://example.org/3d01", "PrintModel", null, "Organizer", free: true), admin, HttpStatusCode.Created, asAdmin: true);
    var pt = await list("pt");
    var ptKinds = pt.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("language").ValueKind == JsonValueKind.Null ? "-" : i.GetProperty("language").GetString()).ToArray();
    Assert(ptKinds.OrderBy(k => k).SequenceEqual(new[] { "-", "pt" }), "pt filter: " + string.Join(",", ptKinds));
    Assert((await list("en")).GetProperty("items").GetArrayLength() == 2 && (await list("all")).GetProperty("items").GetArrayLength() == 3, "en/all filter");
    var es = await list("es");
    Assert(es.GetProperty("items").GetArrayLength() == 1 && es.GetProperty("total").GetInt32() == 3, "no Spanish: only the language-independent model, total still known");
    var languages = es.GetProperty("languages").EnumerateArray().ToDictionary(l => l.GetProperty("code").ValueKind == JsonValueKind.Null ? "-" : l.GetProperty("code").GetString()!, l => l);
    Assert(languages["en"].GetProperty("kinds").GetProperty("HowToPlay").GetInt32() == 1 && languages["pt"].GetProperty("kinds").GetProperty("Rules").GetInt32() == 1, "alternatives per kind");
    var model = (await list("all")).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "PrintModel");
    Assert(model.GetProperty("printCategory").GetString() == "Organizer" && model.GetProperty("isFree").GetBoolean(), "3D fields");
});
await Check("reports: one open report per user, only for public resources, resolved by administrators", async () => {
    await Call(HttpMethod.Post, $"/game-resources/{created}/reports", new { reason = "BrokenLink", comment = (string?)null }, user, HttpStatusCode.Created);
    await Call(HttpMethod.Post, $"/game-resources/{created}/reports", new { reason = "WrongLanguage", comment = (string?)null }, user, HttpStatusCode.Conflict);
    await Call(HttpMethod.Post, $"/game-resources/{created}/reports", new { reason = "Other", comment = "" }, other, HttpStatusCode.BadRequest);
    await Call(HttpMethod.Post, $"/game-resources/{created}/reports", new { reason = "Incompatible", comment = "Needs the 2nd edition" }, other, HttpStatusCode.Created);
    var pendingId = store.Resources.First(r => r.Status == "Pending").Id;
    await Call(HttpMethod.Post, $"/game-resources/{pendingId}/reports", new { reason = "BrokenLink", comment = (string?)null }, user, HttpStatusCode.NotFound);
    var open = await Call(HttpMethod.Get, "/admin/game-resources/reports", null, admin, HttpStatusCode.OK, asAdmin: true);
    Assert(open.GetArrayLength() == 2, "open reports");
    var queue = await Call(HttpMethod.Get, "/admin/game-resources?status=Approved", null, admin, HttpStatusCode.OK, asAdmin: true);
    Assert(queue.EnumerateArray().Single(r => r.GetProperty("resource").GetProperty("id").GetGuid() == created).GetProperty("openReports").GetInt32() == 2, "report count");
    await Call(HttpMethod.Post, $"/admin/game-resources/reports/{open[0].GetProperty("id").GetGuid()}/resolve", null, user, HttpStatusCode.Forbidden);
    await Call(HttpMethod.Post, $"/admin/game-resources/reports/{open[0].GetProperty("id").GetGuid()}/resolve", null, admin, HttpStatusCode.NoContent, asAdmin: true);
    Assert((await Call(HttpMethod.Get, "/admin/game-resources/reports", null, admin, HttpStatusCode.OK, asAdmin: true)).GetArrayLength() == 1, "resolved report still open");
});
await Check("administrators edit and remove; users cannot", async () => {
    var body = new { kind = "Playthrough", printCategory = (string?)null, language = "en", title = "Edited", author = (string?)null, platform = "YouTube", url = "https://example.org/en02", compatibility = (string?)null, isOfficial = true, isFree = (bool?)null };
    await Call(HttpMethod.Put, $"/admin/game-resources/{created}", body, user, HttpStatusCode.Forbidden);
    var edited = await Call(HttpMethod.Put, $"/admin/game-resources/{created}", body, admin, HttpStatusCode.OK, asAdmin: true);
    Assert(edited.GetProperty("title").GetString() == "Edited" && edited.GetProperty("kind").GetString() == "Playthrough" && edited.GetProperty("isOfficial").GetBoolean(), "edit");
    await Call(HttpMethod.Delete, $"/admin/game-resources/{created}", null, user, HttpStatusCode.Forbidden);
    await Call(HttpMethod.Delete, $"/admin/game-resources/{created}", null, admin, HttpStatusCode.NoContent, asAdmin: true);
    Assert(!store.Resources.Any(r => r.Id == created) && !store.Reports.Any(r => r.ResourceId == created), "removal should take its reports");
    await Call(HttpMethod.Delete, $"/admin/game-resources/{created}", null, admin, HttpStatusCode.NotFound, asAdmin: true);
});
Console.WriteLine($"{passed} isolated game resource HTTP tests passed.");
await app.StopAsync();

public class Store : IGameResourceRepository {
    public List<GameResource> Resources = []; public List<GameResourceReport> Reports = [];
    static void Link(GameResource r, Game game) => typeof(GameResource).GetProperty("Game")!.SetValue(r, game);
    public Task<IReadOnlyList<GameResource>> GetApprovedByGameAsync(Guid gameId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GameResource>>(Resources.Where(r => r.GameId == gameId && r.Status == "Approved").ToList());
    public Task<IReadOnlyList<GameResource>> GetForReviewAsync(string? status, Guid? gameId, CancellationToken ct = default) {
        foreach (var r in Resources) { r.Reports.Clear(); foreach (var x in Reports.Where(x => x.ResourceId == r.Id)) r.Reports.Add(x); }
        return Task.FromResult<IReadOnlyList<GameResource>>(Resources.Where(r => (status == null || r.Status == status) && (gameId == null || r.GameId == gameId)).ToList());
    }
    public Task<GameResource?> GetForUpdateAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Resources.FirstOrDefault(r => r.Id == id));
    public Task<bool> UrlExistsAsync(Guid gameId, string url, Guid? exceptId = null, CancellationToken ct = default) => Task.FromResult(Resources.Any(r => r.GameId == gameId && r.Url == url && r.Status != "Rejected" && r.Id != exceptId));
    public Task<bool> HasOpenReportAsync(Guid resourceId, Guid userId, CancellationToken ct = default) => Task.FromResult(Reports.Any(r => r.ResourceId == resourceId && r.UserId == userId && r.IsOpen));
    public Task<IReadOnlyList<GameResourceReport>> GetOpenReportsAsync(CancellationToken ct = default) {
        foreach (var r in Reports) {
            typeof(GameResourceReport).GetProperty("Resource")!.SetValue(r, Resources.First(x => x.Id == r.ResourceId));
            typeof(GameResourceReport).GetProperty("User")!.SetValue(r, new User("Reporter", "reporter@example.invalid", "local"));
        }
        return Task.FromResult<IReadOnlyList<GameResourceReport>>(Reports.Where(r => r.IsOpen).ToList());
    }
    public Task<GameResourceReport?> GetReportForUpdateAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Reports.FirstOrDefault(r => r.Id == id));
    public Task AddAsync(GameResource resource, CancellationToken ct = default) { Link(resource, Games.Current!); Resources.Add(resource); return Task.CompletedTask; }
    public Task AddReportAsync(GameResourceReport report, CancellationToken ct = default) { Reports.Add(report); return Task.CompletedTask; }
    // Mirrors the SQL cascade from a resource to its reports.
    public void Remove(GameResource resource) { Resources.Remove(resource); Reports.RemoveAll(r => r.ResourceId == resource.Id); }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
public class Games : DispatchProxy {
    public static Game? Current;
    public static IGameRepository With(Game game) { Current = game; return DispatchProxy.Create<IGameRepository, Games>(); }
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == "GetByIdAsync"
        ? Task.FromResult((Guid)args![0]! == Current!.Id ? Current : null) : throw new NotSupportedException(method.Name);
}
public class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (!Guid.TryParse(Request.Headers["X-Fixture-User"], out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()) };
        if (Request.Headers["X-Fixture-Role"] == "Admin") claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
