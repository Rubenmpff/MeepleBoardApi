// Blocks, reports, suspension and the PT/EN filter (docs/proposta-bloqueio-comunidade.md in the app).
// Production entities, services and controllers; in-memory repositories; no configuration, SQL or external storage.
// The same rules against real SQL and real tokens: tools/DeviceTestApi/verify-community.cjs.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Calculators;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.AutoMapper;
using MeepleBoard.Services.Mapping.Dtos;
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
async Task Throws<T>(Func<Task> action, string message, string? text = null) where T : Exception
{
    try { await action(); }
    catch (T ex) { if (text != null && ex.Message != text) throw new Exception($"{message}: expected «{text}», got «{ex.Message}»"); return; }
    throw new Exception(message);
}

// ── Filter: whole words and expressions only ──────────────────────────────────────────────────────────────────────
await Sync("filter PT/EN: forbidden words and expressions, with accents, capitals, hyphens, spaces or written together", () => {
    foreach (var text in new[] { "caralho", "Que CARALHO de jogada", "Foda-se!", "fodase", "foda se", "filho da puta", "filho-da-puta", "Cabrão", "cabrao",
        "és um paneleiro", "vai-te foder", "vai te foder", "Mata-te", "preto de merda", "atrasado mental", "mongolóide",
        "fuck", "Fucking hell", "motherfucker", "fuck you", "bitch", "Kill yourself", "kys", "you retard", "rapist" })
        Assert(!ContentFilter.IsAllowed(text), "should be refused: " + text);
});
await Sync("filter false positives: names, places and ordinary PT/EN phrases pass", () => {
    foreach (var text in new[] { "computador", "disputa renhida", "Disputámos a final", "Caralhota de Almeirim", "Scunthorpe United", "classic game",
        "assassin", "Assassin's Creed", "Barcelona", "Ancona", "Cabral", "uma cabra no tabuleiro", "Conímbriga", "Iconografia",
        "contar pontos", "putativo vencedor", "Puttin' on the Ritz", "Shitake", "Dickens", "Cocktail party", "Hancock", "Essex", "Sussex",
        "therapist", "grape", "rapeseed", "drapes", "retardar a jogada", "Monheim", "preto e branco", "cigano", "Mataram-me na última ronda",
        "Vou-te matar no Catan!", "I will kill you in Root", "Kyson", "matemática", "Rabelo", "pirâmide", "federal",
        "Fuchsia", "Bitcoin", "Whoreson", "Slutsky", "Nigeria", "Niger", "Faggio", "kysely" })
        Assert(ContentFilter.IsAllowed(text), "should pass: " + text);
    Assert(ContentFilter.IsAllowed(null) && ContentFilter.IsAllowed("   "), "empty passes");
});

// ── Suspension (domain) ──────────────────────────────────────────────────────────────────────────────────────────
await Sync("suspension: for a period or until review, with a reason; ends every session; lifting or expiry keeps earlier tokens refused", () => {
    var now = DateTime.UtcNow; var admin = Guid.NewGuid();
    var u = new User("Suspensa", "s@example.invalid", "Local"); var stamp = u.SecurityStamp;
    Assert(!u.IsSuspended(now), "not suspended at first");
    _ = Assert2(() => u.Suspend(now.AddDays(7), "  ", admin, now)); // reason required
    _ = Assert2(() => u.Suspend(now.AddMinutes(-1), "Motivo", admin, now)); // end in the past
    u.Suspend(now.AddDays(7), "Motivo", admin, now);
    Assert(u.IsSuspended(now) && u.IsSuspended(now.AddDays(6.9)) && !u.IsSuspended(now.AddDays(7)), "period");
    Assert(u.TokensRevokedAt == now && u.SecurityStamp != stamp, "every earlier token is revoked");
    u.LiftSuspension(admin, now.AddDays(1));
    Assert(!u.IsSuspended(now.AddDays(1)) && u.TokensRevokedAt == now && u.SuspensionLiftedById == admin, "lifted, tokens still revoked");
    u.Suspend(null, "Até revisão", admin, now.AddDays(2));
    Assert(u.IsSuspended(now.AddYears(5)), "until review lasts until lifted");
});
static bool Assert2(Action action) { try { action(); } catch (ArgumentException) { return true; } throw new Exception("expected ArgumentException"); }

// ── Who may play together ────────────────────────────────────────────────────────────────────────────────────────
var A = Guid.NewGuid(); var B = Guid.NewGuid(); var C = Guid.NewGuid(); var D = Guid.NewGuid();
var community = new Community();
var friends = Friends.Of((A, B), (A, C), (B, C));
var campaign = new Campaign("Campanha de teste", Guid.NewGuid(), A);
campaign.Members.Add(Member(campaign.Id, A, true)); campaign.Members.Add(Member(campaign.Id, D, true));
var campaigns = Fake.Of<ICampaignRepository>((m, a) => m.Name == "GetByIdForUpdateAsync" ? Task.FromResult((Guid)a![0]! == campaign.Id ? campaign : null) : null);

await Check("registering outside sessions: only with accepted friends, even with IDs sent directly; the campaign encounter exception", async () => {
    await MatchCompany.RequireOutsideSessionAsync(A, [A, B, C], null, friends, campaigns, default);
    await Throws<InvalidOperationException>(() => MatchCompany.RequireOutsideSessionAsync(A, [A, D], null, friends, campaigns, default), "D is not a friend", MatchCompany.NotFriends);
    // Campaign encounter: accepted members, with or without friendship; the registrant must be a member too.
    await MatchCompany.RequireOutsideSessionAsync(A, [A, D], campaign.Id, friends, campaigns, default);
    await Throws<InvalidOperationException>(() => MatchCompany.RequireOutsideSessionAsync(A, [A, B], campaign.Id, friends, campaigns, default), "B is not a member", MatchCompany.NotCampaignMembers);
    await Throws<UnauthorizedAccessException>(() => MatchCompany.RequireOutsideSessionAsync(B, [B, A], campaign.Id, friends, campaigns, default), "the registrant must be a member");
    await MatchCompany.RequireOutsideSessionAsync(A, [A], null, friends, campaigns, default); // solo
});
await Check("a block between any two players refuses the match, whoever registers and in both directions", async () => {
    community.Blocks.Add((B, C));
    await Throws<InvalidOperationException>(() => MatchCompany.RequireNoBlockAsync(community, [A, B, C], default), "B blocked C", MatchCompany.Blocked);
    community.Blocks.Clear(); community.Blocks.Add((C, B));
    await Throws<InvalidOperationException>(() => MatchCompany.RequireNoBlockAsync(community, [A, B, C], default), "C blocked B", MatchCompany.Blocked);
    await MatchCompany.RequireNoBlockAsync(community, [A, B], default);
    community.Blocks.Clear();
});

// ── Sessions: invite, accept, the unavailable state ──────────────────────────────────────────────────────────────
var mapper = new MapperConfiguration(c => c.AddProfile<MappingEntityToDto>()).CreateMapper();
var users = Fake.Of<IUserRepository>((m, a) => m.Name == "GetByIdAsync" ? Task.FromResult<User?>(new User("Pessoa", "p@example.invalid", "Local")) : null);
GameSession NewSession(Guid organizer, params (Guid User, bool Accepted)[] players)
{
    var s = new GameSession("Sessão de teste", organizer, null, DateTime.UtcNow.AddDays(1));
    s.Players.Add(new GameSessionPlayer(s.Id, organizer, isOrganizer: true));
    foreach (var (user, accepted) in players) { var p = new GameSessionPlayer(s.Id, user); if (accepted) p.Accept(); s.Players.Add(p); }
    return s;
}
(GameSessionService Service, List<GameSession> Store) Sessions(params GameSession[] sessions)
{
    var store = sessions.ToList();
    var repo = Fake.Of<IGameSessionRepository>((m, a) => m.Name switch {
        "GetListAsync" => Task.FromResult<IReadOnlyList<GameSession>>(store),
        "GetByIdWithDetailsAsync" or "GetByIdWithPlayersForUpdateAsync" or "GetByIdForUpdateAsync" => Task.FromResult(store.FirstOrDefault(s => s.Id == (Guid)a![0]!)),
        "SaveChangesAsync" => Task.CompletedTask, _ => null });
    var links = Fake.Of<IGameSessionPlayerRepository>((m, a) => m.Name switch {
        "GetBySessionAndUserAsync" => Task.FromResult(store.SelectMany(s => s.Players).FirstOrDefault(p => p.SessionId == (Guid)a![0]! && p.UserId == (Guid)a[1]!)),
        "AddAsync" => Add((GameSessionPlayer)a![0]!), _ => null });
    Task Add(GameSessionPlayer p) { store.First(s => s.Id == p.SessionId).Players.Add(p); return Task.CompletedTask; }
    return (new GameSessionService(repo, links, users, mapper, friends, Fake.Of<IGameRepository>((_, _) => null), community), store);
}

await Check("session invite: refused with a block with whoever organises, an accepted participant or someone invited; friends only", async () => {
    var s = NewSession(A, (B, false)); var (service, _) = Sessions(s);
    community.Blocks.Add((C, B)); // the new guest C blocked the pending guest B
    await Throws<ArgumentException>(() => service.InvitePlayerAsync(s.Id, A, C), "block with someone invited", GameSessionService.CannotInvite);
    community.Blocks.Clear(); community.Blocks.Add((A, C)); // the organizer blocked C (the friendship would be gone too; refused all the same)
    await Throws<ArgumentException>(() => service.InvitePlayerAsync(s.Id, A, C), "block with whoever organises", GameSessionService.CannotInvite);
    community.Blocks.Clear();
    await Throws<ArgumentException>(() => service.InvitePlayerAsync(s.Id, A, D), "D is not a friend");
    await service.InvitePlayerAsync(s.Id, A, C);
    Assert(s.Players.Any(p => p.UserId == C && p.Status == GameSessionInviteStatus.Pending), "C invited");
});
await Check("session accept: refused while a block exists with whoever organises or an accepted participant; declining still works; nobody leaves", async () => {
    var s = NewSession(A, (B, true), (C, false)); var (service, _) = Sessions(s);
    community.Blocks.Add((B, C)); // an accepted participant blocked the pending guest
    await Throws<InvalidOperationException>(() => service.RespondInviteAsync(s.Id, C, true), "accept refused", GameSessionService.CannotAccept);
    var detail = await service.GetByIdAsync(s.Id, C);
    Assert(detail!.InviteAvailable == false, "the detail says the invitation is no longer available");
    Assert(!(await service.GetMineAsync(C)).Any(x => x.Id == s.Id), "it leaves the list of invitations");
    Assert((await service.GetByIdAsync(s.Id, B))!.InviteAvailable && (await service.GetMineAsync(B)).Any(x => x.Id == s.Id), "the session is unchanged for B");
    Assert(s.Players.Count(p => p.Status == GameSessionInviteStatus.Accepted) == 2 && !s.IsCancelled, "nobody removed, nothing cancelled");
    await service.RespondInviteAsync(s.Id, C, false);
    Assert(s.Players.Single(p => p.UserId == C).Status == GameSessionInviteStatus.Declined, "declining works");
    community.Blocks.Clear();
});
await Check("session: a block with another pending guest does not make an invitation unavailable; unblocking makes it available again", async () => {
    var s = NewSession(A, (B, false), (C, false)); var (service, _) = Sessions(s);
    community.Blocks.Add((B, C));
    Assert((await service.GetByIdAsync(s.Id, C))!.InviteAvailable, "pending guests do not count");
    await service.RespondInviteAsync(s.Id, C, true);
    Assert((await service.GetByIdAsync(s.Id, B))!.InviteAvailable == false, "now C is accepted: B's invitation is unavailable");
    community.Blocks.Clear();
    Assert((await service.GetByIdAsync(s.Id, B))!.InviteAvailable, "available again after unblocking");
});
await Check("session creation: refused with a block between any two of the people invited together", async () => {
    var (service, _) = Sessions();
    community.Blocks.Add((B, C));
    await Throws<ArgumentException>(() => service.CreateAsync(new CreateGameSessionDto { Name = "Noite de jogos", PlayerIds = [B, C] }, A), "block among invitees", GameSessionService.CannotInvite);
    community.Blocks.Clear();
    await Throws<ArgumentException>(() => service.CreateAsync(new CreateGameSessionDto { Name = "Noite de caralho", PlayerIds = [B] }, A), "filtered name", ContentFilter.Message);
});

// ── Campaigns ────────────────────────────────────────────────────────────────────────────────────────────────────
await Check("campaign invite: only the inviter's accepted friends; refused with a block with any accepted or invited member; accept refused while blocked", async () => {
    var c = new Campaign("Campanha", Guid.NewGuid(), A);
    c.Members.Add(Member(c.Id, A, true)); c.Members.Add(Member(c.Id, B, true));
    var repo = Fake.Of<ICampaignRepository>((m, a) => m.Name switch {
        "GetByIdForUpdateAsync" => Task.FromResult<Campaign?>(c),
        "GetMemberAsync" => Task.FromResult(c.Members.FirstOrDefault(x => x.UserId == (Guid)a![1]!)),
        "GetMembersAsync" => Task.FromResult<IReadOnlyList<CampaignMember>>(c.Members.ToList()),
        "AddMemberAsync" => AddMember((CampaignMember)a![0]!), "SaveChangesAsync" => Task.CompletedTask, _ => null });
    Task AddMember(CampaignMember m) { c.Members.Add(m); return Task.CompletedTask; }
    var service = new CampaignService(repo, Fake.Of<IMatchRepository>((_, _) => null), users, Fake.Of<IGameRepository>((_, _) => null),
        Fake.Of<INotificationService>((_, _) => Task.CompletedTask), Fake.Of<IPhotoStorageService>((_, _) => null), NullLogger<CampaignService>.Instance, friends, community);
    await Throws<ArgumentException>(() => service.InviteMemberAsync(c.Id, D, A), "D is not A's friend");
    community.Blocks.Add((B, C));
    await Throws<ArgumentException>(() => service.InviteMemberAsync(c.Id, C, A), "C and the member B have a block", CampaignService.CannotInvite);
    community.Blocks.Clear();
    await service.InviteMemberAsync(c.Id, C, A);
    community.Blocks.Add((C, B));
    await Throws<InvalidOperationException>(() => service.RespondInviteAsync(c.Id, C, true), "accept refused", CampaignService.CannotAccept);
    await service.RespondInviteAsync(c.Id, C, false);
    Assert(c.Members.Count(m => m.Status == CampaignMemberStatus.Accepted) == 2, "members stay");
    community.Blocks.Clear();
});

// ── HTTP: blocks, reports of people and photos, moderation ───────────────────────────────────────────────────────
var photoUrl = "https://res.cloudinary.com/demo/image/authenticated/s--x--/v1/meepleboard/journal/photo.jpg";
var otherUrl = "https://res.cloudinary.com/demo/image/authenticated/s--y--/v1/meepleboard/journal/other.jpg";
var match = new Match(Guid.NewGuid(), DateTime.UtcNow.AddDays(-1));
match.MatchPlayers.Add(new MatchPlayer(match.Id, A)); match.MatchPlayers.Add(new MatchPlayer(match.Id, B));
var entry = new MatchJournalEntry(match.Id, B); entry.AddPhotoUrl(photoUrl); Set(entry, "Match", match);
var ownEntry = new MatchJournalEntry(match.Id, A); ownEntry.AddPhotoUrl(otherUrl); Set(ownEntry, "Match", match);
community.Entries.AddRange([entry, ownEntry]);
community.Active.UnionWith([A, B, C, D]);
var storage = new Storage();

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddApplicationPart(typeof(CommunityController).Assembly);
var activeUsers = Fake.Of<IUserRepository>((m, a) => m.Name == "IsActiveAsync" ? Task.FromResult(community.Active.Contains((Guid)a![0]!)) : null);
builder.Services.AddSingleton<ICommunityService>(new CommunityService(community, activeUsers, storage, NullLogger<CommunityService>.Instance));
builder.Services.AddSingleton<IPhotoStorageService>(storage);
await using var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
var admin = Guid.NewGuid();
async Task<HttpStatusCode> Send(HttpMethod method, string path, Guid user, object? body = null, bool asAdmin = false)
{
    using var request = new HttpRequestMessage(method, "/MeepleBoard/" + path);
    request.Headers.Add("X-Fixture-User", user.ToString());
    if (asAdmin) request.Headers.Add("X-Fixture-Role", "Admin");
    if (body != null) request.Content = JsonContent.Create(body);
    using var response = await http.SendAsync(request);
    return response.StatusCode;
}
void Expect(HttpStatusCode actual, HttpStatusCode expected, string what) => Assert(actual == expected, $"{what}: expected {(int)expected}, got {(int)actual}");

await Check("HTTP blocks: each person creates and removes only their own; the other's block stays; self or unknown refused", async () => {
    Expect(await Send(HttpMethod.Post, $"community/blocks/{B}", A), HttpStatusCode.NoContent, "A blocks B");
    Expect(await Send(HttpMethod.Post, $"community/blocks/{B}", A), HttpStatusCode.NoContent, "repeating changes nothing");
    Expect(await Send(HttpMethod.Post, $"community/blocks/{A}", B), HttpStatusCode.NoContent, "B blocks A too");
    Expect(await Send(HttpMethod.Delete, $"community/blocks/{A}", C), HttpStatusCode.NotFound, "C cannot remove a block C did not make");
    Expect(await Send(HttpMethod.Delete, $"community/blocks/{B}", A), HttpStatusCode.NoContent, "A unblocks B");
    Assert(community.Blocks.SetEquals([(B, A)]), "B's block stays");
    Expect(await Send(HttpMethod.Post, $"community/blocks/{A}", A), HttpStatusCode.BadRequest, "self");
    Expect(await Send(HttpMethod.Post, $"community/blocks/{Guid.NewGuid()}", A), HttpStatusCode.NotFound, "unknown account");
    community.Blocks.Clear();
});
await Check("HTTP person report: a known reason, once while open; never self; nothing automatic", async () => {
    Expect(await Send(HttpMethod.Post, $"community/people/{B}/reports", A, new { reason = "Harassment", detail = "Mensagens insistentes" }), HttpStatusCode.NoContent, "report");
    Expect(await Send(HttpMethod.Post, $"community/people/{B}/reports", A, new { reason = "Spam" }), HttpStatusCode.Conflict, "one open report per pair");
    Expect(await Send(HttpMethod.Post, $"community/people/{B}/reports", C, new { reason = "Whatever" }), HttpStatusCode.BadRequest, "unknown reason");
    Expect(await Send(HttpMethod.Post, $"community/people/{A}/reports", A, new { reason = "Spam" }), HttpStatusCode.BadRequest, "self");
    Assert(community.UserReports.Count == 1 && community.Suspended.Count == 0, "one report, no suspension");
});
await Check("HTTP photo report: only a participant, only someone else's photo, the right match and key; once while open; never deleted automatically", async () => {
    var key = JournalPhotoReference.Key(photoUrl);
    var reason = new { reason = "Inappropriate" };
    Expect(await Send(HttpMethod.Post, $"campaigns/matches/{match.Id}/journal/photos/{entry.Id}/{key}/reports", C, reason), HttpStatusCode.Forbidden, "C did not play");
    Expect(await Send(HttpMethod.Post, $"campaigns/matches/{Guid.NewGuid()}/journal/photos/{entry.Id}/{key}/reports", A, reason), HttpStatusCode.NotFound, "entry of another match");
    Expect(await Send(HttpMethod.Post, $"campaigns/matches/{match.Id}/journal/photos/{entry.Id}/{JournalPhotoReference.Key(otherUrl)}/reports", A, reason), HttpStatusCode.NotFound, "key of a photo of another entry");
    Expect(await Send(HttpMethod.Post, $"campaigns/matches/{match.Id}/journal/photos/{ownEntry.Id}/{JournalPhotoReference.Key(otherUrl)}/reports", A, reason), HttpStatusCode.Conflict, "own photo");
    Expect(await Send(HttpMethod.Post, $"campaigns/matches/{match.Id}/journal/photos/{entry.Id}/{key}/reports", A, reason), HttpStatusCode.NoContent, "A reports B's photo");
    Expect(await Send(HttpMethod.Post, $"campaigns/matches/{match.Id}/journal/photos/{entry.Id}/{key}/reports", A, reason), HttpStatusCode.Conflict, "once while open");
    Assert(entry.PhotoUrls.Contains(photoUrl) && storage.Deleted.Count == 0, "nothing removed");
});
await Check("HTTP moderation: administrators only; a photo is readable only while reported; keep or remove is a manual decision", async () => {
    var key = JournalPhotoReference.Key(photoUrl);
    Expect(await Send(HttpMethod.Get, "admin/moderation/people", A), HttpStatusCode.Forbidden, "not an administrator");
    Expect(await Send(HttpMethod.Get, "admin/moderation/photos", A), HttpStatusCode.Forbidden, "not an administrator");
    Expect(await Send(HttpMethod.Get, $"admin/moderation/photos/{entry.Id}/{key}", A), HttpStatusCode.Forbidden, "reading needs the role");
    Expect(await Send(HttpMethod.Get, $"admin/moderation/photos/{ownEntry.Id}/{JournalPhotoReference.Key(otherUrl)}", admin, asAdmin: true), HttpStatusCode.NotFound, "an unreported photo stays closed");
    Expect(await Send(HttpMethod.Get, $"admin/moderation/photos/{entry.Id}/{key}", admin, asAdmin: true), HttpStatusCode.OK, "a reported photo opens");
    Expect(await Send(HttpMethod.Post, $"admin/moderation/photos/{entry.Id}/{key}/remove", admin, asAdmin: true), HttpStatusCode.NoContent, "remove");
    Assert(!entry.PhotoUrls.Contains(photoUrl) && storage.Deleted.SequenceEqual([photoUrl]) && community.PhotoReports.All(r => !r.IsOpen), "removed and resolved");
    Expect(await Send(HttpMethod.Get, $"admin/moderation/photos/{entry.Id}/{key}", admin, asAdmin: true), HttpStatusCode.NotFound, "closed again once resolved");
});
await Check("HTTP suspension: 7 days, 30 days or until review, with a reason; resolves the reports; lifting needs a suspension", async () => {
    Expect(await Send(HttpMethod.Post, $"admin/moderation/people/{B}/suspend", A, new { days = 7, reason = "Assédio" }), HttpStatusCode.Forbidden, "not an administrator");
    Expect(await Send(HttpMethod.Post, $"admin/moderation/people/{B}/suspend", admin, new { days = 3, reason = "Assédio" }, true), HttpStatusCode.BadRequest, "only 7, 30 or until review");
    Expect(await Send(HttpMethod.Post, $"admin/moderation/people/{B}/suspend", admin, new { days = 7, reason = " " }, true), HttpStatusCode.BadRequest, "a reason is required");
    Expect(await Send(HttpMethod.Post, $"admin/moderation/people/{B}/lift-suspension", admin, null, true), HttpStatusCode.NotFound, "nothing to lift");
    Expect(await Send(HttpMethod.Post, $"admin/moderation/people/{B}/suspend", admin, new { days = (int?)null, reason = "Assédio" }, true), HttpStatusCode.NoContent, "until review");
    Assert(community.Suspended.TryGetValue(B, out var until) && until == null && community.UserReports.All(r => !r.IsOpen), "suspended until review, reports resolved");
    Expect(await Send(HttpMethod.Post, $"admin/moderation/people/{B}/lift-suspension", admin, null, true), HttpStatusCode.NoContent, "lifted");
    Assert(!community.Suspended.ContainsKey(B), "no longer suspended");
});

await Check("resource suggestions: title and author filtered; the URL is never filtered nor changed", async () => {
    GameResource? saved = null;
    var resources = Fake.Of<IGameResourceRepository>((m, a) => m.Name switch {
        "UrlExistsAsync" => Task.FromResult(false), "AddAsync" => Keep((GameResource)a![0]!), "SaveChangesAsync" => Task.CompletedTask, _ => null });
    Task Keep(GameResource r) { saved = r; return Task.CompletedTask; }
    var games = Fake.Of<IGameRepository>((m, _) => m.Name == "GetByIdAsync" ? Task.FromResult<Game?>(new Game("Jogo", "", null)) : null);
    var service = new GameResourceService(resources, games);
    var url = "https://www.youtube.com/watch?v=fuck-cunt-kys";
    await service.SuggestAsync(Guid.NewGuid(), A, new GameResourceRequest("HowToPlay", null, "pt", "Como jogar", "Autor", "YouTube", url, null, false, null), default);
    Assert(saved?.Url == url, "the URL is kept as sent");
    await Throws<ArgumentException>(() => service.SuggestAsync(Guid.NewGuid(), A, new GameResourceRequest("HowToPlay", null, "pt", "Como jogar, caralho", "Autor", "YouTube", url + "2", null, false, null), default), "title filtered", ContentFilter.Message);
    await Throws<ArgumentException>(() => service.SuggestAsync(Guid.NewGuid(), A, new GameResourceRequest("HowToPlay", null, "pt", "Como jogar", "Motherfucker", "YouTube", url + "3", null, false, null), default), "author filtered", ContentFilter.Message);
});

await app.StopAsync();
Console.WriteLine($"{passed} isolated community tests passed.");

static CampaignMember Member(Guid campaignId, Guid user, bool accepted) { var m = new CampaignMember(campaignId, user); if (accepted) m.Accept(); return m; }
static void Set(object target, string property, object? value) =>
    target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

/// <summary>An interface answered by a handler; a null answer means the call was not expected by the test.</summary>
public class Fake : DispatchProxy
{
    Func<MethodInfo, object?[]?, object?> handler = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => handler(method!, args) ?? throw new NotSupportedException(method!.Name + " is not used by this test.");
    public static T Of<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class { var p = Create<T, Fake>(); ((Fake)(object)p).handler = handler; return p; }
}

public static class Friends
{
    /// <summary>Accepted friendships between these pairs (either direction); nothing else is used.</summary>
    public static IFriendshipRepository Of(params (Guid, Guid)[] pairs)
    {
        var accepted = pairs.SelectMany(p => new[] { p, (p.Item2, p.Item1) }).ToHashSet();
        return Fake.Of<IFriendshipRepository>((m, a) => m.Name == "ExistsAcceptedAsync" ? Task.FromResult(accepted.Contains(((Guid)a![0]!, (Guid)a[1]!))) : null);
    }
}

/// <summary>Blocks, reports and diary entries in memory, with the same meaning as CommunityRepository.</summary>
public class Community : ICommunityRepository
{
    public HashSet<(Guid Blocker, Guid Blocked)> Blocks = [];
    public List<UserReport> UserReports = [];
    public List<PhotoReport> PhotoReports = [];
    public List<MatchJournalEntry> Entries = [];
    public HashSet<Guid> Active = [];
    public Dictionary<Guid, DateTime?> Suspended = [];
    bool Either(Guid a, Guid b) => Blocks.Contains((a, b)) || Blocks.Contains((b, a));
    public Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken ct = default) => Task.FromResult(Either(a, b));
    public Task<bool> AnyBlockWithAsync(Guid user, IEnumerable<Guid> others, CancellationToken ct = default) => Task.FromResult(others.Any(o => o != user && Either(user, o)));
    public Task<bool> AnyBlockAmongAsync(IEnumerable<Guid> users, CancellationToken ct = default) { var ids = users.Distinct().ToList(); return Task.FromResult(ids.Any(a => ids.Any(b => a != b && Either(a, b)))); }
    public Task<IReadOnlyList<Guid>> BlockedEitherWayAsync(Guid user, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(Blocks.Where(b => b.Blocker == user || b.Blocked == user).Select(b => b.Blocker == user ? b.Blocked : b.Blocker).Distinct().ToList());
    public Task<IReadOnlyList<BlockedPerson>> MyBlocksAsync(Guid blocker, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BlockedPerson>>(Blocks.Where(b => b.Blocker == blocker).Select(b => new BlockedPerson(b.Blocked, "Pessoa", DateTime.UtcNow)).ToList());
    public Task<bool> BlockAsync(Guid blocker, Guid blocked, CancellationToken ct = default) => Task.FromResult(Blocks.Add((blocker, blocked)));
    public Task<bool> UnblockAsync(Guid blocker, Guid blocked, CancellationToken ct = default) => Task.FromResult(Blocks.Remove((blocker, blocked)));
    public Task AddUserReportAsync(UserReport report, CancellationToken ct = default) { UserReports.Add(report); return Task.CompletedTask; }
    public Task AddPhotoReportAsync(PhotoReport report, CancellationToken ct = default) { PhotoReports.Add(report); return Task.CompletedTask; }
    public Task<bool> HasOpenUserReportAsync(Guid reporter, Guid reported, CancellationToken ct = default) => Task.FromResult(UserReports.Any(r => r.ReporterId == reporter && r.ReportedUserId == reported && r.IsOpen));
    public Task<bool> HasOpenPhotoReportAsync(Guid reporter, Guid entry, string url, CancellationToken ct = default) => Task.FromResult(PhotoReports.Any(r => r.ReporterId == reporter && r.JournalEntryId == entry && r.PhotoUrl == url && r.IsOpen));
    public Task<bool> PhotoHasOpenReportAsync(Guid entry, string url, CancellationToken ct = default) => Task.FromResult(PhotoReports.Any(r => r.JournalEntryId == entry && r.PhotoUrl == url && r.IsOpen));
    public Task<IReadOnlyList<ReportedPerson>> ReportedPeopleAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReportedPerson>>(UserReports.Where(r => r.IsOpen).GroupBy(r => r.ReportedUserId).Select(g => new ReportedPerson(g.Key, "Pessoa",
            g.GroupBy(r => r.Reason).ToDictionary(x => x.Key, x => x.Count()), g.Where(r => r.Detail != null).Select(r => r.Detail!).ToList(), g.Max(r => r.CreatedAt), null, null, null)).ToList());
    public Task<IReadOnlyList<ReportedPhoto>> ReportedPhotosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReportedPhoto>>(PhotoReports.Where(r => r.IsOpen).GroupBy(r => (r.JournalEntryId, r.PhotoUrl)).Select(g => {
            var e = Entries.Single(x => x.Id == g.Key.JournalEntryId);
            return new ReportedPhoto(e.Id, e.MatchId, e.UserId, "Dona", g.Key.PhotoUrl, g.GroupBy(r => r.Reason).ToDictionary(x => x.Key, x => x.Count()), [], g.Max(r => r.CreatedAt));
        }).ToList());
    public Task<IReadOnlyList<SuspendedPerson>> SuspendedPeopleAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SuspendedPerson>>(Suspended.Select(s => new SuspendedPerson(s.Key, "Pessoa", DateTime.UtcNow, s.Value, "Motivo")).ToList());
    public Task<int> ResolveUserReportsAsync(Guid reported, Guid admin, string outcome, CancellationToken ct = default)
    { var open = UserReports.Where(r => r.ReportedUserId == reported && r.IsOpen).ToList(); open.ForEach(r => r.Resolve(admin, outcome)); return Task.FromResult(open.Count); }
    public Task<int> ResolvePhotoReportsAsync(Guid entry, string url, Guid admin, string outcome, CancellationToken ct = default)
    { var open = PhotoReports.Where(r => r.JournalEntryId == entry && r.PhotoUrl == url && r.IsOpen).ToList(); open.ForEach(r => r.Resolve(admin, outcome)); return Task.FromResult(open.Count); }
    public Task<MatchJournalEntry?> GetJournalEntryAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));
    public Task<bool> SuspendAsync(Guid user, DateTime? until, string reason, Guid admin, CancellationToken ct = default)
    { if (!Active.Contains(user)) return Task.FromResult(false); Suspended[user] = until; return Task.FromResult(true); }
    public Task<bool> LiftSuspensionAsync(Guid user, Guid admin, CancellationToken ct = default) => Task.FromResult(Suspended.Remove(user));
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class Storage : IPhotoStorageService
{
    public List<string> Deleted = [];
    public Task<string> UploadAsync(Stream file, string name, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(string url, CancellationToken ct = default) { Deleted.Add(url); return Task.CompletedTask; }
    public Task<(byte[] Content, string ContentType)> ReadProtectedAsync(string url, CancellationToken ct = default) => Task.FromResult((new byte[] { 1, 2, 3 }, "image/jpeg"));
}

public class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.Headers["X-Fixture-User"], out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()) };
        if (Request.Headers["X-Fixture-Role"] == "Admin") claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
