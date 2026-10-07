// Standalone focused tests: dotnet run --project tests/PlayerScores.Tests
// All dependencies are in-memory proxies; never starts the API or accesses SQL.
using System.Reflection;
using System.Text.Json;
using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using MeepleBoardApi.Services.Mapping.AutoMapper;

var passed = 0;
async Task Check(string name, Func<Task> body) { await body(); Console.WriteLine($"PASS {name}"); passed++; }
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }

await Check("legacy request persists absent scores and returns them", async () => {
    var f = new Fixture(); var response = await f.Service.CreateAsync(f.Request(null), f.UserId);
    Assert(f.Saved == 1 && f.Match!.MatchPlayers.All(p => p.Score == null));
    Assert(response.Players.Single().Score == null);
});
await Check("zero and positive scores reach entities and the read DTO", async () => {
    var f = new Fixture(); var other = Guid.NewGuid();
    var response = await f.Service.CreateAsync(f.Request(new() {
        new() { UserId = f.UserId, Score = 0 }, new() { UserId = other, Score = 42 }
    }, other), f.UserId);
    Assert(f.Saved == 1);
    Assert(f.Match!.MatchPlayers.Single(p => p.UserId == f.UserId).Score == 0);
    Assert(response.Players.Single(p => p.UserId == other).Score == 42);
    var read = await f.Service.GetByIdAsync(f.Match.Id, f.UserId);
    Assert(read!.Players.Single(p => p.UserId == f.UserId).Score == 0);
});
await Check("empty score list and explicit null preserve optional scores", async () => {
    var f = new Fixture(); await f.Service.CreateAsync(f.Request(new()), f.UserId);
    Assert(f.Match!.MatchPlayers.Single().Score == null);
    f = new Fixture(); await f.Service.CreateAsync(f.Request(new() { new() { UserId = f.UserId } }), f.UserId);
    Assert(f.Match!.MatchPlayers.Single().Score == null);
});
foreach (var kind in new[] { "outsider", "duplicate", "empty id", "null entry" }) {
    await Check($"{kind} rejected before any dependency call", async () => {
        var f = new Fixture(); var entries = new List<CreateMatchPlayerScoreDto>();
        entries.Add(kind switch {
            "outsider" => new() { UserId = Guid.NewGuid(), Score = 1 },
            "empty id" => new() { UserId = Guid.Empty, Score = 0 },
            "null entry" => null!,
            _ => new() { UserId = f.UserId, Score = 0 }
        });
        if (kind == "duplicate") entries.Add(new() { UserId = f.UserId, Score = 2 });
        try { await f.Service.CreateAsync(f.Request(entries), f.UserId); throw new Exception("Expected rejection"); }
        catch (ArgumentException) { Assert(f.Calls == 0 && f.Saved == 0); }
    });
}
await Check("legacy JSON remains compatible; decimal and overflow are rejected by int?", () => {
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    Assert(JsonSerializer.Deserialize<CreateMatchDto>("{\"playerIds\":[]}", options)!.PlayerScores == null);
    foreach (var value in new[] { "1.5", "2147483648", "-2147483649" }) {
        try { JsonSerializer.Deserialize<CreateMatchDto>($"{{\"playerScores\":[{{\"score\":{value}}}]}}", options); throw new Exception("Expected rejection"); }
        catch (JsonException) { }
    }
    return Task.CompletedTask;
});
foreach (var value in new[] { int.MinValue, -17, 0, int.MaxValue }) {
    await Check($"signed integer {value} persists and is returned without selecting a winner", async () => {
        var f = new Fixture(); var dto = f.Request(new() { new() { UserId = f.UserId, Score = value } });
        var response = await f.Service.CreateAsync(dto, f.UserId);
        Assert(response.Players.Single().Score == value && response.WinnerId == null);
        Assert((await f.Service.GetByIdAsync(f.Match!.Id, f.UserId))!.Players.Single().Score == value);
    });
}
foreach (var kind in new[] { "missing", "null", "partial", "disabled", "implicit partial", "auto-added actor" }) {
    await Check($"{kind} score coverage rejected before any dependency call", async () => {
        var f = new Fixture(); var other = Guid.NewGuid();
        var ids = kind == "auto-added actor" ? new List<Guid> { other } : new List<Guid> { f.UserId, other };
        var scores = kind == "missing" ? null : new List<CreateMatchPlayerScoreDto> {
            new() { UserId = ids[0], Score = kind == "null" ? null : -10 }
        };
        var dto = new CreateMatchDto { GameId = f.Game.Id, GameName = f.Game.Name, PersonalRating = 7.5, MatchDate = DateTime.UtcNow,
            PlayerIds = ids, IsSoloGame = true, PlayerScores = scores,
            ScoresEnabled = kind == "implicit partial" ? null : kind != "disabled" };
        try { await f.Service.CreateAsync(dto, f.UserId); throw new Exception("Expected rejection"); }
        catch (ArgumentException) { Assert(f.Calls == 0 && f.Saved == 0); }
    });
}
await Check("competitive winner is explicit even when another player has higher score", async () => {
    var f = new Fixture(); var other = Guid.NewGuid();
    var response = await f.Service.CreateAsync(new CreateMatchDto { GameId = f.Game.Id, GameName = f.Game.Name, PersonalRating = 7.5,
        MatchDate = DateTime.UtcNow, PlayerIds = new() { f.UserId, other }, WinnerId = f.UserId,
        ScoresEnabled = true, PlayerScores = new() { new() { UserId = f.UserId, Score = -10 }, new() { UserId = other, Score = 0 } }
    }, f.UserId);
    Assert(response.WinnerId == f.UserId && response.Players.Single(p => p.UserId == other).Score == 0);
});
await Check("old partial scores remain readable without replacing null with zero", async () => {
    var f = new Fixture(); var other = Guid.NewGuid();
    await f.Service.CreateAsync(f.Request(null, other), f.UserId);
    f.Match!.MatchPlayers.Single(p => p.UserId == f.UserId).UpdateScore(-5);
    var read = await f.Service.GetByIdAsync(f.Match.Id, f.UserId);
    Assert(read!.Players.Single(p => p.UserId == f.UserId).Score == -5 && read.Players.Single(p => p.UserId == other).Score == null);
});

foreach (var customDeadline in new[] { false, true }) {
    await Check($"SQL session timestamps retain UTC and {(customDeadline ? "custom" : "automatic")} deadline", () => {
        var scheduled = DateTime.UtcNow.AddDays(3);
        var session = new GameSession("Session UTC test", Guid.NewGuid(), scheduledStartDateUtc: scheduled,
            responseDeadlineUtc: customDeadline ? scheduled.AddHours(-2) : null);
        var player = new GameSessionPlayer(session.Id, Guid.NewGuid());
        session.Players.Add(player);
        // Simulate SQL datetime2 materialization, which loses DateTime.Kind.
        foreach (var field in new[] { "ScheduledStartDate", "StartDate", "ResponseDeadline" }) {
            var property = typeof(GameSession).GetProperty(field)!;
            if (property.GetValue(session) is DateTime value)
                property.SetValue(session, DateTime.SpecifyKind(value, DateTimeKind.Unspecified));
        }
        foreach (var field in new[] { "InvitedAt", "JoinedAt" }) {
            var property = typeof(GameSessionPlayer).GetProperty(field)!;
            property.SetValue(player, DateTime.SpecifyKind((DateTime)property.GetValue(player)!, DateTimeKind.Unspecified));
        }
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingEntityToDto>()).CreateMapper();
        var dto = mapper.Map<GameSessionDto>(session);
        Assert(dto.ScheduledStartDate.Ticks == scheduled.Ticks && dto.ScheduledStartDate.Kind == DateTimeKind.Utc);
        Assert(dto.StartDate.Kind == DateTimeKind.Utc && dto.Players.Single().InvitedAt.Kind == DateTimeKind.Utc);
        Assert(dto.ResponseDeadline.HasValue == customDeadline && dto.EffectiveDeadline.Kind == DateTimeKind.Utc);
        Assert(dto.EffectiveDeadline.Ticks == (customDeadline ? scheduled.AddHours(-2) : scheduled).Ticks);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto));
        Assert(json.RootElement.GetProperty("ScheduledStartDate").GetString()!.EndsWith("Z"));
        Assert(json.RootElement.GetProperty("EffectiveDeadline").GetString()!.EndsWith("Z"));
        return Task.CompletedTask;
    });
}


foreach (var rating in new double?[] { null, -0.5, 10.5, 7.25, double.NaN, double.PositiveInfinity }) {
    await Check($"invalid or missing rating {rating} rejected before dependencies", async () => {
        var f = new Fixture();
        try { await f.Service.CreateAsync(new CreateMatchDto { PersonalRating = rating, IsSoloGame = true, PlayerIds = new() { f.UserId } }, f.UserId); throw new Exception("Expected rejection"); }
        catch (ArgumentException) { Assert(f.Calls == 0 && f.Saved == 0); }
    });
}
foreach (var kind in new[] { "one", "duplicate", "session solo" }) {
    await Check($"{kind} participant rule rejects before writes", async () => {
        var f = new Fixture();
        var dto = new CreateMatchDto { PersonalRating = 0, IsSoloGame = kind == "session solo", GameSessionId = kind == "session solo" ? Guid.NewGuid() : null,
            PlayerIds = kind == "duplicate" ? new() { f.UserId, f.UserId } : new() { f.UserId }, WinnerId = f.UserId };
        try { await f.Service.CreateAsync(dto, f.UserId); throw new Exception("Expected rejection"); }
        catch (ArgumentException) { Assert(f.Calls == 0 && f.Saved == 0); }
    });
}
foreach (var rating in new[] { 0d, .5, 7.5, 10d }) {
    await Check($"own journal preserves rating {rating} without rounding or peer ratings", async () => {
        var f = new Fixture(); var other = Guid.NewGuid();
        await f.Service.CreateAsync(new CreateMatchDto { GameId = f.Game.Id, GameName = f.Game.Name, MatchDate = DateTime.UtcNow.AddMinutes(-1),
            PersonalRating = rating, PlayerIds = new() { f.UserId, other }, WinnerId = f.UserId }, f.UserId);
        Assert(f.Entries.Count == 1 && f.Entries.Single().UserId == f.UserId && f.Entries.Single().PersonalRating == rating);
        var read = await f.Service.GetByIdAsync(f.Match!.Id, f.UserId);
        Assert(read!.PersonalRating == rating && f.Match.JournalEntries.Single().PersonalRating == rating);
    });
}

foreach (var mode in new[] { "COMPETITIVE", "SOLO", "COOPERATIVE" }) foreach (var result in new[] { "Win", "Loss", "Draw", "Undefined" }) {
    if (mode == "COMPETITIVE" && result == "Loss") continue;
    await Check($"explicit {mode}/{result} persists per-player outcome and rereads", async () => {
        var f = new Fixture(); var other = Guid.NewGuid(); var third = Guid.NewGuid();
        var ids = mode == "SOLO" ? new List<Guid> { f.UserId } : new List<Guid> { f.UserId, other, third };
        var selected = mode != "COMPETITIVE" || result == "Undefined" ? new List<Guid>() : result == "Draw" ? new() { f.UserId, other } : new() { f.UserId };
        var response = await f.Service.CreateAsync(new CreateMatchDto { GameId = f.Game.Id, GameName = f.Game.Name, MatchDate = DateTime.UtcNow.AddMinutes(-1), PersonalRating = 0,
            PlayerIds = ids, GameMode = mode, Result = result, IsSoloGame = mode == "SOLO", ResultPlayerIds = selected }, f.UserId);
        Assert(response.GameMode == mode && response.Result == result && response.ResultSource == "Explicit");
        foreach (var p in response.Players) Assert(p.Outcome == (mode == "COMPETITIVE" && result != "Undefined" ? selected.Contains(p.UserId) ? result : "Loss" : result));
        Assert(mode != "COOPERATIVE" || response.WinnerId == null);
        Assert(result == "Win" || response.WinnerIds.Count == 0);
        Assert((await f.Service.GetByIdAsync(response.Id, f.UserId))!.Result == result);
    });
}
await Check("shared competitive victory persists both winners and no arbitrary WinnerId", async () => {
    var f = new Fixture(); var other = Guid.NewGuid();
    var response = await f.Service.CreateAsync(new CreateMatchDto { GameId = f.Game.Id, GameName = f.Game.Name, MatchDate = DateTime.UtcNow.AddMinutes(-1), PersonalRating = .5,
        PlayerIds = new() { f.UserId, other }, GameMode = "COMPETITIVE", Result = "Win", ResultPlayerIds = new() { f.UserId, other }, SharedVictoryAllowed = true }, f.UserId);
    Assert(response.WinnerIds.Count == 2 && response.WinnerId == null && response.Players.All(p => p.Outcome == "Win"));
});
foreach (var kind in new[] { "shared not allowed", "one draw", "outsider", "repeated selection", "competitive loss", "solo two", "team selection", "undefined selection", "solo flag", "bad mode", "bad result" }) {
    await Check($"explicit {kind} rejected before dependencies", async () => {
        var f = new Fixture(); var other = Guid.NewGuid();
        var mode = kind.StartsWith("solo") ? "SOLO" : kind == "team selection" ? "COOPERATIVE" : kind == "bad mode" ? "INVALID" : "COMPETITIVE";
        var result = kind == "one draw" ? "Draw" : kind == "competitive loss" ? "Loss" : kind == "undefined selection" ? "Undefined" : kind == "bad result" ? "INVALID" : "Win";
        var selection = kind == "shared not allowed" ? new List<Guid> { f.UserId, other } : kind == "repeated selection" ? new() { f.UserId, f.UserId } : kind == "outsider" ? new() { Guid.NewGuid() } : mode == "SOLO" ? new() : new() { f.UserId };
        var dto = new CreateMatchDto { GameId = f.Game.Id, GameName = f.Game.Name, MatchDate = DateTime.UtcNow.AddMinutes(-1), PersonalRating = 7.5, GameMode = mode, Result = result,
            IsSoloGame = kind == "solo two", PlayerIds = kind == "solo flag" ? new() { f.UserId } : new() { f.UserId, other }, ResultPlayerIds = selection };
        try { await f.Service.CreateAsync(dto, f.UserId); throw new Exception("Expected rejection"); } catch (ArgumentException) { Assert(f.Calls == 0 && f.Saved == 0); }
    });
}
await Check("legacy winner and solo flags remain unchanged and not promoted to explicit results", async () => {
    var f = new Fixture(); await f.Service.CreateAsync(f.Request(null), f.UserId);
    var read = await f.Service.GetByIdAsync(f.Match!.Id, f.UserId);
    Assert(read!.ResultSource == "Legacy" && read.Result == null && read.GameMode == null && read.Players.Single().Outcome == null);
});
await Check("shared classification distinguishes third-person victory, partial draw, team draw and legacy", () => {
    Assert(MatchOutcomeRules.SharedResult("COMPETITIVE", "Win", "Loss", "Loss") == "bothLost");
    Assert(MatchOutcomeRules.SharedResult("COMPETITIVE", "Draw", "Draw", "Loss") == "currentUserDraw");
    Assert(MatchOutcomeRules.SharedResult("COOPERATIVE", "Draw", "Draw", "Draw") == "teamDraw");
    Assert(MatchOutcomeRules.SharedResult(null, null, null, null) == "legacyUnknown");
    return Task.CompletedTask;
});

var ruleOwner = Guid.NewGuid();
var ruleFriend = Guid.NewGuid();
(GameSessionService Service, List<GameSession> Sessions, List<GameSessionPlayer> Links) SessionRules(bool acceptedFriend = true) {
    var sessions = new List<GameSession>();
    var links = new List<GameSessionPlayer>();
    var sessionRepo = Stub.Create<IGameSessionRepository>((method, args) => method.Name switch {
        "AddAsync" => AddSession((GameSession)args![0]!),
        "SaveChangesAsync" => Task.CompletedTask,
        "GetByIdWithDetailsAsync" => Task.FromResult<GameSession?>(sessions.Single()),
        "GetByIdForUpdateAsync" => Task.FromResult<GameSession?>(sessions.Single()),
        _ => throw new Exception("Unexpected session dependency: " + method.Name)
    });
    Task AddSession(GameSession value) { sessions.Add(value); return Task.CompletedTask; }
    var players = Stub.Create<IGameSessionPlayerRepository>((method, args) => {
        if (method.Name == "GetBySessionAndUserAsync") return Task.FromResult<GameSessionPlayer?>(links.FirstOrDefault(p => p.SessionId == (Guid)args![0]! && p.UserId == (Guid)args[1]!));
        if (method.Name != "AddAsync") throw new Exception("Unexpected player dependency");
        var link = (GameSessionPlayer)args![0]!; links.Add(link); sessions.Single().Players.Add(link);
        return Task.CompletedTask;
    });
    var users = Stub.Create<IUserRepository>((method, args) => {
        if (method.Name != "GetByIdAsync") throw new Exception("Unexpected user dependency");
        return Task.FromResult<User?>(new User("Test", "test@example.test", "Local") { Id = (Guid)args![0]! });
    });
    var friends = Stub.Create<IFriendshipRepository>((method, args) => {
        if (method.Name != "ExistsAcceptedAsync") throw new Exception("Unexpected friend dependency");
        return Task.FromResult(acceptedFriend);
    });
    var mapper = new MapperConfiguration(c => c.AddProfile<MappingEntityToDto>()).CreateMapper();
    return (new GameSessionService(sessionRepo, players, users, mapper, friends), sessions, links);
}
foreach (var ids in new List<Guid>?[] { null, new(), new() { ruleOwner }, new() { Guid.Empty, ruleOwner } }) {
    await Check("session without a distinct friend rejected before tracking writes", async () => {
        var f = SessionRules();
        try {
            await f.Service.CreateAsync(new MeepleBoard.Application.DTOs.CreateGameSessionDto { Name = "Test session", PlayerIds = ids! }, ruleOwner);
            throw new Exception("Expected missing friend rejection");
        } catch (ArgumentException ex) {
            Assert(ex.Message == "Seleciona pelo menos um amigo para criar a sessão");
            Assert(f.Sessions.Count == 0 && f.Links.Count == 0);
        }
    });
}
await Check("nonfriend session invite rejected before writes", async () => {
    var f = SessionRules(false);
    try {
        await f.Service.CreateAsync(new MeepleBoard.Application.DTOs.CreateGameSessionDto { Name = "Test session", PlayerIds = new() { ruleFriend } }, ruleOwner);
        throw new Exception("Expected nonfriend rejection");
    } catch (ArgumentException) { Assert(f.Sessions.Count == 0 && f.Links.Count == 0); }
});
await Check("friendship allows creation with pending invitation and deduplicates without counting organizer", async () => {
    var f = SessionRules();
    var dto = await f.Service.CreateAsync(new MeepleBoard.Application.DTOs.CreateGameSessionDto {
        Name = "Test session", ScheduledStartDate = DateTime.UtcNow.AddDays(1), PlayerIds = new() { ruleOwner, ruleFriend, ruleFriend }
    }, ruleOwner);
    Assert(f.Sessions.Count == 1 && f.Links.Count == 2);
    Assert(dto.Players.Single(p => p.UserId == ruleFriend).Status == MeepleBoard.Domain.Enums.GameSessionInviteStatus.Pending);
    Assert(dto.Players.Single(p => p.UserId == ruleOwner).IsOrganizer);
});
await Check("existing organizer-only session remains readable", async () => {
    var f = SessionRules();
    f.Sessions.Add(new GameSession("Legacy session", ruleOwner));
    var dto = await f.Service.GetByIdAsync(f.Sessions.Single().Id, ruleOwner);
    Assert(dto != null && dto.Name == "Legacy session");
});


await Check("later invitation requires accepted friendship before tracking writes", async () => {
    var f = SessionRules(false);
    f.Sessions.Add(new GameSession("Session friend rule", ruleOwner));
    try { await f.Service.InvitePlayerAsync(f.Sessions.Single().Id, ruleOwner, ruleFriend); throw new Exception("Expected rejection"); }
    catch (ArgumentException) { Assert(f.Links.Count == 0); }
});
await Check("later accepted friend invitation starts pending", async () => {
    var f = SessionRules(); f.Sessions.Add(new GameSession("Session friend rule", ruleOwner));
    await f.Service.InvitePlayerAsync(f.Sessions.Single().Id, ruleOwner, ruleFriend);
    Assert(f.Links.Count == 1 && f.Links.Single().Status == MeepleBoard.Domain.Enums.GameSessionInviteStatus.Pending);
});
foreach (var declined in new[] { false, true }) {
    await Check($"later duplicate {(declined ? "declined" : "pending")} invitation is not resent", async () => {
        var f = SessionRules(false); f.Sessions.Add(new GameSession("Session duplicate rule", ruleOwner));
        var existing = new GameSessionPlayer(f.Sessions.Single().Id, ruleFriend);
        if (declined) existing.Decline(); f.Links.Add(existing);
        try { await f.Service.InvitePlayerAsync(f.Sessions.Single().Id, ruleOwner, ruleFriend); throw new Exception("Expected duplicate rejection"); }
        catch (InvalidOperationException) { Assert(f.Links.Count == 1); }
    });
}
await Check("later invitation still requires the organizer", async () => {
    var f = SessionRules(); f.Sessions.Add(new GameSession("Session organizer rule", ruleOwner));
    try { await f.Service.InvitePlayerAsync(f.Sessions.Single().Id, Guid.NewGuid(), ruleFriend); throw new Exception("Expected organizer rejection"); }
    catch (InvalidOperationException) { Assert(f.Links.Count == 0); }
});


Console.WriteLine($"{passed} focused service/contract tests passed.");

class Fixture {
    public Guid UserId = Guid.NewGuid();
    public Game Game = new("Test game", "", null);
    public Match? Match;
    public int Saved, Calls;
    public List<MatchJournalEntry> Entries = new();
    public MatchService Service;
    public Fixture() {
        T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class => Stub.Create<T>((m, a) => { Calls++; return handler(m, a); });
        T Unused<T>() where T : class => Proxy<T>((m, a) => throw new Exception($"Unexpected call: {m.Name}"));
        var matches = Proxy<IMatchRepository>((m, a) => m.Name switch {
            "AddAsync" => AddMatch((Match)a![0]!),
            "SaveChangesAsync" => Save(),
            "GetByIdAsync" => new ValueTask<Match?>(Match),
            _ => throw new Exception(m.Name)
        });
        var players = Proxy<IMatchPlayerRepository>((m, a) => {
            if (m.Name != "AddAsync") throw new Exception(m.Name);
            Match!.MatchPlayers.Add((MatchPlayer)a![0]!); return Task.CompletedTask;
        });
        var games = Proxy<IGameRepository>((m, a) => m.Name switch {
            "GetByIdAsync" => Task.FromResult<Game?>(Game),
            "GetAveragePersonalRatingAsync" => Task.FromResult<double?>(Entries.Where(e => e.PersonalRating.HasValue).Average(e => e.PersonalRating)),
            "UpdateAsync" => Task.CompletedTask,
            _ => throw new Exception(m.Name)
        });
        var journals = Proxy<ICampaignRepository>((m, a) => m.Name switch {
            "AddJournalEntryAsync" => AddEntry((MatchJournalEntry)a![0]!),
            "SaveChangesAsync" => Task.CompletedTask,
            _ => throw new Exception(m.Name)
        });
        var users = Proxy<IUserRepository>((m, a) => m.Name == "GetByIdAsync" ? Task.FromResult<User?>(null) : throw new Exception(m.Name));
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingEntityToDto>()).CreateMapper();
        Service = new(matches, players, Unused<IGameSessionRepository>(), Unused<IGameSessionPlayerRepository>(), users, games,
            journals, Unused<IBGGService>(), Unused<IGameService>(), Unused<INotificationService>(), mapper);
    }
    Task AddEntry(MatchJournalEntry entry) { Entries.Add(entry); Match!.JournalEntries.Add(entry); return Task.CompletedTask; }
    Task AddMatch(Match match) { Match = match; return Task.CompletedTask; }
    Task<int> Save() { Saved++; return Task.FromResult(1); }
    public CreateMatchDto Request(List<CreateMatchPlayerScoreDto>? scores, Guid? other = null) => new() {
        GameId = Game.Id, GameName = Game.Name, PersonalRating = 7.5, MatchDate = DateTime.UtcNow.AddMinutes(-1),
        PlayerIds = other.HasValue ? new() { UserId, other.Value } : new() { UserId },
        IsSoloGame = true, PlayerScores = scores
    };
}
public class Stub : DispatchProxy {
    public Func<MethodInfo, object?[]?, object?> Handler = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class {
        var proxy = DispatchProxy.Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy;
    }
}