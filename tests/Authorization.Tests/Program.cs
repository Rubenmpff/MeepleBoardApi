// Real HTTP pipeline and production controllers/services. No production Program, SQL, secrets or provider calls.
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
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.DTOs.MatchJournal;
using MeepleBoardApi.Controllers;
using MeepleBoardApi.Services.Mapping.AutoMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

var f = new Fixture();
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
builder.Services.AddLogging();
builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddApplicationPart(typeof(MatchController).Assembly);
builder.Services.AddSingleton(f.Matches);
builder.Services.AddSingleton(f.Journals);
builder.Services.AddSingleton(f.Photos);
builder.Services.AddSingleton<IMatchService>(f.MatchService);
builder.Services.AddSingleton<ICampaignService>(f.CampaignService);
builder.Services.AddSingleton<IGameSessionService>(f.SessionService);
await using var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
int passed = 0;
async Task<HttpResponseMessage> Request(HttpMethod method, string path, Guid? user, object? body = null)
{
    using var req = new HttpRequestMessage(method, path);
    if (user.HasValue) req.Headers.Add("X-Fixture-User", user.Value.ToString());
    if (body != null) req.Content = JsonContent.Create(body);
    return await client.SendAsync(req);
}
async Task Check(string name, Func<Task> test) { await test(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
async Task<string> Expect(HttpMethod method, string path, Guid? user, HttpStatusCode status, object? body = null)
{
    using var response = await Request(method, path, user, body);
    var text = await response.Content.ReadAsStringAsync();
    Assert(response.StatusCode == status, $"{method} {path}: expected {status}, received {response.StatusCode}: {text}");
    return text;
}
var matchPath = $"/MeepleBoard/matches/{f.Match.Id}";
var journalPath = $"/MeepleBoard/campaigns/matches/{f.Match.Id}/journal";
var ownPhoto = JournalPhotoReference.Path(f.Match.Id, f.Own.Id, f.Own.PhotoUrls[0]);
var peerPhoto = JournalPhotoReference.Path(f.Match.Id, f.PeerEntry.Id, f.PeerEntry.PhotoUrls[0]);
foreach (var identity in new (string Name, Guid? User, bool Participant)[] {
    ("visitor", null, false), ("author", f.Author, true), ("other participant", f.Peer, true),
    ("outsider", f.Outsider, false), ("session member without participation", f.SessionMember, false),
    ("campaign member without participation", f.CampaignMember, false) })
{
    await Check(identity.Name + ": detail and journal permissions", async () => {
        var expected = identity.User == null ? HttpStatusCode.Unauthorized : identity.Participant ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        var detail = await Expect(HttpMethod.Get, matchPath, identity.User, expected);
        var journal = await Expect(HttpMethod.Get, journalPath, identity.User, expected);
        Assert(!detail.Contains("PRIVATE_") && !detail.Contains("LEGACY_"));
        if (identity.Participant) {
            var ownSecret = identity.User == f.Author ? "PRIVATE_AUTHOR" : "PRIVATE_PEER";
            var foreignSecret = identity.User == f.Author ? "PRIVATE_PEER" : "PRIVATE_AUTHOR";
            Assert(journal.Contains(ownSecret) && !journal.Contains(foreignSecret));
            Assert(journal.Contains("shared-tag") && journal.Contains("photoUrls"));
            Assert(!journal.Contains("res.cloudinary.com") && !journal.Contains("LEGACY_PUBLIC_PHOTO"));
        }
    });
    await Check(identity.Name + ": list, history and pending never expose legacy notes", async () => {
        foreach (var path in new[] { "/MeepleBoard/matches", $"/MeepleBoard/matches/history/game/{f.Game.Id}", "/MeepleBoard/matches/pending-journal" }) {
            using var response = await Request(HttpMethod.Get, path, identity.User);
            var text = await response.Content.ReadAsStringAsync();
            Assert(identity.User == null ? response.StatusCode == HttpStatusCode.Unauthorized : response.IsSuccessStatusCode);
            Assert(!text.Contains("LEGACY_") && !text.Contains(identity.User == f.Author ? "PRIVATE_PEER" : "PRIVATE_AUTHOR"));
            if (identity.User != null && !identity.Participant) Assert(!text.Contains(f.Match.Id.ToString()));
        }
    });
    await Check(identity.Name + ": protected photo authorization and no-store", async () => {
        var expected = identity.User == null ? HttpStatusCode.Unauthorized : identity.Participant ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        using var response = await Request(HttpMethod.Get, ownPhoto, identity.User);
        Assert(response.StatusCode == expected);
        if (identity.Participant) {
            Assert(response.Content.Headers.ContentType?.MediaType == "image/png");
            Assert(response.Headers.CacheControl?.NoStore == true);
            Assert((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 1, 2, 3 }));
        }
    });
    await Check(identity.Name + ": session and campaign responses respect match participation", async () => {
        var sessionStatus = identity.User == null ? HttpStatusCode.Unauthorized : new[] { f.Author, f.Peer, f.SessionMember }.Contains(identity.User.Value) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        var text = await Expect(HttpMethod.Get, $"/MeepleBoard/session/{f.Session.Id}", identity.User, sessionStatus);
        Assert(!text.Contains("PRIVATE_") && !text.Contains("LEGACY_"));
        if (identity.User == f.SessionMember) Assert(!text.Contains(f.Match.Id.ToString()));
        var campaignStatus = identity.User == null ? HttpStatusCode.Unauthorized : new[] { f.Author, f.Peer, f.CampaignMember }.Contains(identity.User.Value) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        var campaign = await Expect(HttpMethod.Get, $"/MeepleBoard/campaigns/{f.Campaign.Id}", identity.User, campaignStatus);
        Assert(!campaign.Contains("PRIVATE_") && !campaign.Contains("LEGACY_"));
        if (identity.User == f.CampaignMember) Assert(campaign.Contains("\"canReadJournal\":false"));
    });
    await Check(identity.Name + ": session and campaign listings are scoped to membership", async () => {
        foreach (var path in new[] { "/MeepleBoard/session", "/MeepleBoard/session/mine", "/MeepleBoard/campaigns/mine", $"/MeepleBoard/campaigns/game/{f.Game.Id}" }) {
            using var response = await Request(HttpMethod.Get, path, identity.User);
            var text = await response.Content.ReadAsStringAsync();
            Assert(identity.User == null ? response.StatusCode == HttpStatusCode.Unauthorized : response.IsSuccessStatusCode);
            Assert(!text.Contains("PRIVATE_") && !text.Contains("LEGACY_"));
            if (identity.User == f.Outsider) Assert(!text.Contains(f.Session.Id.ToString()) && !text.Contains(f.Campaign.Id.ToString()));
        }
    });
    await Check(identity.Name + ": only creator can manually close journal", async () => {
        await Expect(HttpMethod.Post, matchPath + "/close-journal", identity.User,
            identity.User == null ? HttpStatusCode.Unauthorized : identity.User == f.Author ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden);
    });
}
await Check("participant edits only own contribution despite a supplied userId", async () => {
    await Expect(HttpMethod.Put, journalPath, f.Peer, HttpStatusCode.OK, new { userId = f.Author, personalRating = 0, notes = "PRIVATE_PEER_EDITED", tags = "shared-tag" });
    Assert(f.Own.Notes == "PRIVATE_AUTHOR" && f.PeerEntry.Notes == "PRIVATE_PEER_EDITED");
    Assert(f.Match.Notes == "LEGACY_COPY");
    await Expect(HttpMethod.Put, journalPath, f.Author, HttpStatusCode.OK, new { personalRating = 8, notes = (string?)null, tags = "shared-tag" });
    Assert(f.Own.Notes == null && f.PeerEntry.Notes == "PRIVATE_PEER_EDITED");
});
foreach (var user in new Guid?[] { null, f.Outsider, f.SessionMember, f.CampaignMember })
    await Check("non-participant cannot write journal: " + (user?.ToString() ?? "visitor"), async () => {
        await Expect(HttpMethod.Put, journalPath, user, user == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, new { personalRating = 0, notes = "attempt" });
    });
await Check("photo deletion is owner-only, including a forged peer reference", async () => {
    await Expect(HttpMethod.Delete, journalPath + "/photos?photoUrl=" + Uri.EscapeDataString(ownPhoto), f.Peer, HttpStatusCode.Forbidden);
    Assert(f.Own.PhotoUrls.Count == 2 && f.PhotosDeleted == 0);
    await Expect(HttpMethod.Delete, journalPath + "/photos?photoUrl=" + Uri.EscapeDataString(peerPhoto), f.Peer, HttpStatusCode.OK);
    Assert(f.PeerEntry.PhotoUrls.Count == 0 && f.PhotosDeleted == 1);
});
await Check("legacy public photo is not served, even to author", async () => {
    var publicPath = JournalPhotoReference.Path(f.Match.Id, f.Own.Id, f.Own.PhotoUrls.Single(url => url.Contains("/upload/")));
    await Expect(HttpMethod.Get, publicPath, f.Author, HttpStatusCode.NotFound);
});
foreach (var user in new Guid?[] { null, f.Author, f.Peer, f.Outsider, f.SessionMember, f.CampaignMember })
    await Check("photo upload is participant-only: " + (user?.ToString() ?? "visitor"), async () => {
        using var req = new HttpRequestMessage(HttpMethod.Post, journalPath + "/photos");
        if (user.HasValue) req.Headers.Add("X-Fixture-User", user.Value.ToString());
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]); file.Headers.ContentType = new("image/png");
        form.Add(file, "file", "fixture.png"); req.Content = form;
        using var response = await client.SendAsync(req);
        var expected = user == null ? HttpStatusCode.Unauthorized : user == f.Author || user == f.Peer ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        Assert(response.StatusCode == expected);
        var text = await response.Content.ReadAsStringAsync();
        Assert(!text.Contains("res.cloudinary.com"));
        Assert(!text.Contains(user == f.Author ? "PRIVATE_PEER" : "PRIVATE_AUTHOR"));
    });
await Check("a misconfigured public upload is rejected before journal persistence", async () => {
    f.PublicUpload = true;
    var before = f.Own.PhotoUrls.Count;
    using var req = new HttpRequestMessage(HttpMethod.Post, journalPath + "/photos");
    req.Headers.Add("X-Fixture-User", f.Author.ToString());
    using var form = new MultipartFormDataContent(); var file = new ByteArrayContent([1, 2, 3]); file.Headers.ContentType = new("image/png");
    form.Add(file, "file", "fixture.png"); req.Content = form;
    using var response = await client.SendAsync(req);
    Assert(response.StatusCode == HttpStatusCode.Conflict && f.Own.PhotoUrls.Count == before);
    f.PublicUpload = false;
});
await Check("creation persists authenticated creator and journal notes without match copy", async () => {
    var text = await Expect(HttpMethod.Post, "/MeepleBoard/matches", f.Author, HttpStatusCode.Created, new {
        gameId = f.Game.Id, gameName = f.Game.Name, matchDate = DateTime.UtcNow.AddMinutes(-1), isSoloGame = true,
        playerIds = new[] { f.Author, f.Peer }, playerScores = new[] { new { userId = f.Peer, score = 0 } },
        personalRating = 8, notes = "PRIVATE_NEW", tags = "shared-tag", scoreSummary = "shared new summary"
    });
    using var json = JsonDocument.Parse(text); var id = json.RootElement.GetProperty("id").GetGuid();
    Assert(f.Store[id].CreatorId == f.Author && f.Store[id].Notes == null && !text.Contains("PRIVATE_NEW"));
    Assert(f.Entries.Single(e => e.MatchId == id).UserId == f.Author);
    Assert(f.Store[id].MatchPlayers.Single(p => p.UserId == f.Peer).Score == 0);
});
foreach (var user in new Guid?[] { null, f.Peer, f.Outsider, f.SessionMember, f.CampaignMember })
    await Check("only creator may update or delete match: " + (user?.ToString() ?? "visitor"), async () => {
        var expected = user == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        await Expect(HttpMethod.Put, matchPath, user, expected, new { id = f.Match.Id, gameId = f.Game.Id, gameName = f.Game.Name, players = new[] { new { userId = f.Author, userName = "Fixture author" } }, matchDate = f.Match.MatchDate, isSoloGame = true });
        await Expect(HttpMethod.Delete, matchPath, user, expected);
        Assert(f.Store.ContainsKey(f.Match.Id));
    });
await Check("unknown legacy author is never inferred", async () => {
    await Expect(HttpMethod.Put, $"/MeepleBoard/matches/{f.Legacy.Id}", f.Author, HttpStatusCode.Forbidden, new { id = f.Legacy.Id, gameId = f.Game.Id, gameName = f.Game.Name, players = new[] { new { userId = f.Author, userName = "Fixture author" } }, matchDate = f.Legacy.MatchDate, isSoloGame = true });
    await Expect(HttpMethod.Delete, $"/MeepleBoard/matches/{f.Legacy.Id}", f.Author, HttpStatusCode.Forbidden);
    Assert(f.Legacy.CreatorId == null && f.Legacy.Notes == "LEGACY_ORPHAN");
});
await Check("creator updates and deletes; score zero remains intact", async () => {
    await Expect(HttpMethod.Put, matchPath, f.Author, HttpStatusCode.NoContent, new { id = f.Match.Id, gameId = f.Game.Id, gameName = f.Game.Name, players = new[] { new { userId = f.Author, userName = "Fixture author" } }, matchDate = f.Match.MatchDate, isSoloGame = true, scoreSummary = "updated shared summary" });
    Assert(f.Match.MatchPlayers.Single(p => p.UserId == f.Peer).Score == 0);
    await Expect(HttpMethod.Delete, matchPath, f.Author, HttpStatusCode.NoContent);
    Assert(!f.Store.ContainsKey(f.Match.Id));
});
await Check("prepared creator migration is additive; SQL generated only, no database connection", () => {
    using var context = new MeepleBoardDbContext(new DbContextOptionsBuilder<MeepleBoardDbContext>().UseSqlServer("Server=isolated.invalid;Database=NeverOpened;Integrated Security=true;TrustServerCertificate=true").Options);
    Assert(context.Model.FindEntityType(typeof(Match))!.FindProperty(nameof(Match.CreatorId))!.IsNullable);
    var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
    var initialized = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
    var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(initialized.GetRelationalModel(), context.GetService<IDesignTimeModel>().Model.GetRelationalModel());
    foreach (var operation in differences) Console.WriteLine("MODEL REVIEW: " + operation.GetType().Name + " " + (operation.GetType().GetProperty("Name")?.GetValue(operation) ?? ""));
    Assert(!differences.Any(operation => Equals(operation.GetType().GetProperty("Name")?.GetValue(operation), "CreatorId")));
    Assert(differences.Count == 0, "Model and snapshot must match, including the catalog index");
    var ids = context.GetService<IMigrationsAssembly>().Migrations.Keys.Order().ToList();
    var creator = ids.IndexOf("20261006120000_AddMatchCreator");
    var script = context.GetService<IMigrator>().GenerateScript(ids[creator - 1], ids[creator]);
    Assert(script.Contains("ADD [CreatorId] uniqueidentifier NULL"));
    Assert(!script.Contains("DELETE FROM [Matches]") && !script.Contains("UPDATE [Matches]"));
    return Task.CompletedTask;
});
await app.StopAsync();
Console.WriteLine($"{passed - 1} HTTP authorization tests + 1 offline migration/model check passed; no SQL or external storage registered.");

public class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
        Guid.TryParse(Request.Headers["X-Fixture-User"], out var user)
        ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], Scheme.Name)), Scheme.Name))
        : AuthenticateResult.NoResult());
}
public class Fixture
{
    public Guid Author = Guid.NewGuid(), Peer = Guid.NewGuid(), Outsider = Guid.NewGuid(), SessionMember = Guid.NewGuid(), CampaignMember = Guid.NewGuid();
    public Game Game = new("Isolated test game", "", null);
    public Match Match, Legacy;
    public MatchJournalEntry Own, PeerEntry;
    public Campaign Campaign;
    public GameSession Session;
    public Dictionary<Guid, Match> Store = new();
    public List<MatchJournalEntry> Entries = new();
    public IMatchRepository Matches;
    public ICampaignRepository Journals;
    public IPhotoStorageService Photos;
    public MatchService MatchService;
    public CampaignService CampaignService;
    public GameSessionService SessionService;
    public int PhotosDeleted;
    public bool PublicUpload;
    public Fixture()
    {
        Match = new(Game.Id, DateTime.UtcNow.AddHours(-1)); Match.SetCreator(Author); Match.SetSoloGame(true);
        Match.SetJournalData(8, "LEGACY_COPY", "shared-tag"); Match.UpdateMatchDetails("test", "shared summary", 30);
        Legacy = new(Game.Id, DateTime.UtcNow.AddDays(-1)); Legacy.SetSoloGame(true); Legacy.SetJournalData(6, "LEGACY_ORPHAN", null);
        foreach (var match in new[] { Match, Legacy }) {
            Set(match, "Game", Game); match.MatchPlayers.Add(new MatchPlayer(match.Id, Author));
            var peer = new MatchPlayer(match.Id, Peer); peer.UpdateScore(0); match.MatchPlayers.Add(peer); Store[match.Id] = match;
        }
        Own = new(Match.Id, Author); Own.Update(8, "PRIVATE_AUTHOR", "shared-tag");
        Own.AddPhotoUrl("https://res.cloudinary.com/isolated/image/authenticated/v1/meepleboard/match-journal/author.jpg");
        Own.AddPhotoUrl("https://res.cloudinary.com/isolated/image/upload/v1/LEGACY_PUBLIC_PHOTO.jpg");
        PeerEntry = new(Match.Id, Peer); PeerEntry.Update(0, "PRIVATE_PEER", "shared-tag");
        PeerEntry.AddPhotoUrl("https://res.cloudinary.com/isolated/image/authenticated/v1/meepleboard/match-journal/peer.jpg");
        Entries.AddRange([Own, PeerEntry]); Match.JournalEntries.Add(Own); Match.JournalEntries.Add(PeerEntry);
        Session = new GameSession("Isolated session", Author);
        foreach (var user in new[] { Author, Peer, SessionMember }) { var player = new GameSessionPlayer(Session.Id, user); player.Accept(); Session.Players.Add(player); }
        Session.Matches.Add(Match);
        Campaign = new Campaign("Isolated campaign", Game.Id, Author); Campaign.UpdateNotes("shared campaign note");
        foreach (var user in new[] { Author, Peer, CampaignMember }) Campaign.Members.Add(new CampaignMember(Campaign.Id, user, true));
        var cm = new CampaignMatch(Campaign.Id, Match.Id, 1, "isolated encounter"); Set(cm, "Match", Match); Campaign.CampaignMatches.Add(cm);
        Matches = Stub.Create<IMatchRepository>((m, args) => m.Name switch {
            "GetByIdAsync" => new ValueTask<Match?>(Store.GetValueOrDefault((Guid)args![0]!)),
            "GetByUserIdAsync" => Task.FromResult<IReadOnlyList<Match>>(Store.Values.Where(x => x.MatchPlayers.Any(p => p.UserId == (Guid)args![0]!)).ToList()),
            "GetPageForUserAsync" => Task.FromResult<IReadOnlyList<Match>>(Store.Values.Where(x => x.MatchPlayers.Any(p => p.UserId == (Guid)args![0]!)).Skip((int)args![1]! * (int)args![2]!).Take((int)args![2]!).ToList()),
            "GetMatchHistoryByGameForUserAsync" => Task.FromResult<IReadOnlyList<Match>>(Store.Values.Where(x => x.GameId == (Guid)args![0]! && x.MatchPlayers.Any(p => p.UserId == (Guid)args![1]!)).ToList()),
            "GetPendingJournalMatchesForUserAsync" => Task.FromResult<IReadOnlyList<Match>>(Store.Values.Where(x => x.MatchPlayers.Any(p => p.UserId == (Guid)args![0]!) && !x.JournalEntries.Any(e => e.UserId == (Guid)args![0]! && e.PersonalRating != null)).ToList()),
            "DeleteAsync" => Delete((Guid)args![0]!),
            "AddAsync" => Add((Match)args![0]!),
            _ => Default(m.ReturnType)
        });
        Journals = Stub.Create<ICampaignRepository>((m, args) => m.Name switch {
            "GetJournalEntriesForMatchAsync" => Task.FromResult<IReadOnlyList<MatchJournalEntry>>(Entries.Where(e => e.MatchId == (Guid)args![0]!).ToList()),
            "GetJournalEntryAsync" => Task.FromResult(Entries.FirstOrDefault(e => e.MatchId == (Guid)args![0]! && e.UserId == (Guid)args![1]!)),
            "GetByIdWithDetailsAsync" or "GetByIdForUpdateAsync" => Task.FromResult<Campaign?>(Campaign),
            "GetListByUserAsync" => Task.FromResult<IReadOnlyList<Campaign>>(Campaign.Members.Any(member => member.UserId == (Guid)args![0]!) ? [Campaign] : []),
            "GetListByGameAsync" => Task.FromResult<IReadOnlyList<Campaign>>(Campaign.Members.Any(member => member.UserId == (Guid)args![1]!) ? [Campaign] : []),
            "AddJournalEntryAsync" => AddEntry((MatchJournalEntry)args![0]!),
            _ => Default(m.ReturnType)
        });
        Photos = Stub.Create<IPhotoStorageService>((m, args) => m.Name switch {
            "ReadProtectedAsync" => Task.FromResult((new byte[] { 1, 2, 3 }, "image/png")),
            "DeleteAsync" => PhotoDeleted(),
            "UploadAsync" => Task.FromResult(PublicUpload ? "https://res.cloudinary.com/isolated/image/upload/v1/unsafe.jpg" : "https://res.cloudinary.com/isolated/image/authenticated/v1/new.jpg"),
            _ => throw new Exception("Unexpected provider operation")
        });
        var sessions = Stub.Create<IGameSessionRepository>((m, args) => m.Name switch {
            "GetByIdWithDetailsAsync" => Task.FromResult<GameSession?>(Session),
            "GetListAsync" => Task.FromResult<IReadOnlyList<GameSession>>([Session]),
            _ => Default(m.ReturnType)
        });
        var games = Stub.Create<IGameRepository>((m, args) => m.Name == "GetByIdAsync" ? Task.FromResult<Game?>(Game) : Default(m.ReturnType));
        var users = Stub.Create<IUserRepository>((m, args) => m.Name == "GetByIdAsync" ? Task.FromResult<User?>(null) : Default(m.ReturnType));
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingEntityToDto>()).CreateMapper();
        T Empty<T>() where T : class => Stub.Create<T>((m, args) => Default(m.ReturnType));
        var players = Stub.Create<IMatchPlayerRepository>((m, args) => {
            if (m.Name == "AddAsync") { var player = (MatchPlayer)args![0]!; Store[player.MatchId].MatchPlayers.Add(player); return Task.CompletedTask; }
            return Default(m.ReturnType);
        });
        MatchService = new(Matches, players, sessions, Empty<IGameSessionPlayerRepository>(), users, games, Journals, Empty<IBGGService>(), Empty<IGameService>(), Empty<INotificationService>(), mapper);
        CampaignService = new(Journals, Matches, users, games, Empty<INotificationService>(), Photos, Microsoft.Extensions.Logging.Abstractions.NullLogger<CampaignService>.Instance);
        SessionService = new(sessions, Empty<IGameSessionPlayerRepository>(), users, mapper, Empty<IFriendshipRepository>());
    }
    Task Delete(Guid id) { Store.Remove(id); return Task.CompletedTask; }
    Task Add(Match match) { Store[match.Id] = match; return Task.CompletedTask; }
    Task AddEntry(MatchJournalEntry entry) { Entries.Add(entry); return Task.CompletedTask; }
    Task PhotoDeleted() { PhotosDeleted++; return Task.CompletedTask; }
    static void Set(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    static object? Default(Type type) {
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)) {
            var result = type.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result).Invoke(null, [result == typeof(int) ? 1 : result.IsValueType ? Activator.CreateInstance(result) : null]);
        }
        throw new Exception("Unexpected dependency: " + type);
    }
}
public class Stub : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class { var proxy = DispatchProxy.Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy; }
}
