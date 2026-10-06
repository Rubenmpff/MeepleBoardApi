// Isolated HTTP tests: production controller/service, in-memory repositories, no application configuration or SQL connection.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.Interfaces;
using MeepleBoardApi.Controllers;
using MeepleBoardApi.Services.Mapping.AutoMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

var f = new Fixture();
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
builder.Services.AddLogging();
builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddApplicationPart(typeof(UserGameLibraryController).Assembly);
builder.Services.AddSingleton<IUserGameLibraryService>(f.Service);
await using var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
var root = $"/MeepleBoard/users/{f.Owner.Id}/games";
var passed = 0;
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
async Task<string> Request(HttpMethod method, string path, object? body, HttpStatusCode expected, Guid? user) {
    using var req = new HttpRequestMessage(method, path);
    if (user.HasValue) req.Headers.Add("X-Fixture-User", user.Value.ToString());
    if (body != null) req.Content = JsonContent.Create(body);
    using var response = await client.SendAsync(req);
    var text = await response.Content.ReadAsStringAsync();
    Assert(response.StatusCode == expected, $"{method}: expected {expected}, got {response.StatusCode}: {text}");
    return text;
}
async Task VerifyPrice(decimal? value, GameLibraryStatus status) {
    var text = await Request(HttpMethod.Get, root, null, HttpStatusCode.OK, f.Owner.Id);
    using var json = JsonDocument.Parse(text); var entry = json.RootElement[0];
    Assert(value.HasValue ? entry.GetProperty("pricePaid").GetDecimal() == value.Value : entry.GetProperty("pricePaid").ValueKind == JsonValueKind.Null);
    Assert(entry.GetProperty("status").GetInt32() == (int)status);
    Assert(entry.GetProperty("totalTimesPlayed").GetInt32() == 3);
    Assert(await f.Service.GetTotalAmountSpentByUserAsync(f.Owner.Id) == (value ?? 0));
}
foreach (decimal? price in new decimal?[] { null, 0, 35.50m }) {
    await Check($"add and reload price {price?.ToString() ?? "null"}", async () => {
        await Request(HttpMethod.Post, root, new { gameId = f.Game.Id, gameName = f.Game.Name, status = 1, pricePaid = price }, HttpStatusCode.Created, f.Owner.Id);
        await VerifyPrice(price, GameLibraryStatus.Owned);
        await Request(HttpMethod.Delete, root + "/" + f.Game.Id, null, HttpStatusCode.NoContent, f.Owner.Id);
    });
}
await Check("old add request without price remains unknown", async () => {
    await Request(HttpMethod.Post, root, new { gameId = f.Game.Id, gameName = f.Game.Name, status = 1 }, HttpStatusCode.Created, f.Owner.Id);
    await VerifyPrice(null, GameLibraryStatus.Owned);
});
foreach (decimal? price in new decimal?[] { 42.75m, 0, null }) {
    await Check($"edit, clear and reload price {price?.ToString() ?? "null"}", async () => {
        await Request(HttpMethod.Patch, root + "/" + f.Game.Id, new { status = 1, pricePaid = price }, HttpStatusCode.NoContent, f.Owner.Id);
        await VerifyPrice(price, GameLibraryStatus.Owned);
    });
}
foreach (var status in new[] { GameLibraryStatus.Owned, GameLibraryStatus.Wishlist, GameLibraryStatus.Played }) {
    await Check("retain existing status " + status, async () => {
        await Request(HttpMethod.Patch, root + "/" + f.Game.Id, new { status = (int)status, pricePaid = 10m }, HttpStatusCode.NoContent, f.Owner.Id);
        await VerifyPrice(10m, status);
    });
}
await Check("old patch without price clears it, matching existing contract", async () => {
    await Request(HttpMethod.Patch, root + "/" + f.Game.Id, new { status = 1 }, HttpStatusCode.NoContent, f.Owner.Id);
    await VerifyPrice(null, GameLibraryStatus.Owned);
});
await Check("negative price rejected without changing the entry", async () => {
    await Request(HttpMethod.Patch, root + "/" + f.Game.Id, new { status = 1, pricePaid = -1m }, HttpStatusCode.BadRequest, f.Owner.Id);
    await VerifyPrice(null, GameLibraryStatus.Owned);
});
await Check("visitor and another user cannot mutate this library", async () => {
    foreach (var method in new[] { HttpMethod.Patch, HttpMethod.Delete }) {
        var body = method == HttpMethod.Patch ? new { status = 1, pricePaid = (decimal?)null } : null;
        await Request(method, root + "/" + f.Game.Id, body, HttpStatusCode.Unauthorized, null);
        await Request(method, root + "/" + f.Game.Id, body, HttpStatusCode.Forbidden, Guid.NewGuid());
    }
    await VerifyPrice(null, GameLibraryStatus.Owned);
});
await Check("removal preserves catalogue and played history", async () => {
    await Request(HttpMethod.Delete, root + "/" + f.Game.Id, null, HttpStatusCode.NoContent, f.Owner.Id);
    Assert(f.Entry == null && f.CatalogueReads > 0);
    var text = await Request(HttpMethod.Get, $"/MeepleBoard/users/{f.Owner.Id}/played-games", null, HttpStatusCode.OK, f.Owner.Id);
    using var json = JsonDocument.Parse(text); var played = json.RootElement[0];
    Assert(played.GetProperty("gameId").GetGuid() == f.Game.Id && played.GetProperty("timesPlayed").GetInt32() == 3);
    Assert(!played.GetProperty("inLibrary").GetBoolean());
    Assert(played.GetProperty("pricePaid").ValueKind == JsonValueKind.Null);
});
await Check("offline EF SQL needs CreatorId; deleting a library dependent keeps principals", () => {
    using var db = new MeepleBoardDbContext(new DbContextOptionsBuilder<MeepleBoardDbContext>().UseSqlServer("Server=isolated.invalid;Database=Discardable;Integrated Security=True;TrustServerCertificate=True").Options);
    Assert(db.Matches.ToQueryString().Contains("CreatorId"));
    var entry = new UserGameLibrary(f.Owner.Id, f.Game.Id, GameLibraryStatus.Owned, 0);
    db.Attach(f.Game); db.Attach(entry); db.Remove(entry);
    Assert(db.Entry(f.Game).State == EntityState.Unchanged);
    Assert(db.ChangeTracker.Entries().Count(e => e.State == EntityState.Deleted) == 1);
    return Task.CompletedTask;
});
Console.WriteLine($"{passed} isolated library HTTP/service/model tests passed.");
await app.StopAsync();

public class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Guid.TryParse(Request.Headers["X-Fixture-User"], out var user)
        ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], Scheme.Name)), Scheme.Name)) : AuthenticateResult.NoResult());
}
public class Fixture {
    public User Owner = new("Fixture", "fixture@example.invalid", "local");
    public Game Game = new("Fixture game", "", null);
    public UserGameLibrary? Entry;
    public int CatalogueReads;
    public UserGameLibraryService Service;
    public Fixture() {
        T Unused<T>() where T : class => Stub.Create<T>((m, a) => throw new Exception("Unexpected dependency " + m.Name));
        var library = Stub.Create<IUserGameLibraryRepository>((m, a) => m.Name switch {
            "ExistsAsync" => Task.FromResult(Entry != null),
            "GetByUserAndGameAsync" => Task.FromResult(Entry),
            "GetByUserIdAsync" => Task.FromResult<IReadOnlyList<UserGameLibrary>>(Entry == null ? [] : [Entry]),
            "GetTotalAmountSpentByUserAsync" => Task.FromResult(Entry?.PricePaid ?? 0m),
            "AddAsync" => Add((UserGameLibrary)a![0]!),
            "UpdateAsync" => Task.CompletedTask,
            "RemoveAsync" => Remove(),
            "CommitAsync" => Task.FromResult(1),
            _ => throw new Exception(m.Name)
        });
        var games = Stub.Create<IGameRepository>((m, a) => {
            CatalogueReads++;
            return m.Name switch { "GetByIdAsync" => Task.FromResult<Game?>(Game), "GetByIdsAsync" => Task.FromResult<IReadOnlyList<Game>>([Game]), _ => throw new Exception("Unexpected catalogue mutation: " + m.Name) };
        });
        var users = Stub.Create<IUserRepository>((m, a) => m.Name == "GetByIdAsync" ? Task.FromResult<User?>(Owner) : throw new Exception(m.Name));
        var matches = Stub.Create<IMatchRepository>((m, a) => m.Name == "GetPlayCountsByGameForUserAsync" ? Task.FromResult<IReadOnlyDictionary<Guid, (int Count, DateTime LastPlayed)>>(new Dictionary<Guid, (int Count, DateTime LastPlayed)> { [Game.Id] = (3, DateTime.UtcNow.AddDays(-1)) }) : throw new Exception("Unexpected match mutation: " + m.Name));
        Service = new(library, games, users, matches, Unused<IFriendshipRepository>(), Unused<IBGGService>(), Unused<IGameService>(), new MapperConfiguration(c => c.AddProfile<MappingEntityToDto>()).CreateMapper());
    }
    Task Add(UserGameLibrary entry) { entry.GetType().GetProperty("Game")!.SetValue(entry, Game); Entry = entry; return Task.CompletedTask; }
    Task Remove() { Entry = null; return Task.CompletedTask; }
}
public class Stub : DispatchProxy {
    public Func<MethodInfo, object?[]?, object?> Handler = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class { var proxy = DispatchProxy.Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy; }
}
