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
foreach (var kind in new[] { "outsider", "duplicate", "negative", "empty id", "null entry" }) {
    await Check($"{kind} rejected before any dependency call", async () => {
        var f = new Fixture(); var entries = new List<CreateMatchPlayerScoreDto>();
        entries.Add(kind switch {
            "outsider" => new() { UserId = Guid.NewGuid(), Score = 1 },
            "negative" => new() { UserId = f.UserId, Score = -1 },
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
    foreach (var value in new[] { "1.5", "2147483648" }) {
        try { JsonSerializer.Deserialize<CreateMatchDto>($"{{\"playerScores\":[{{\"score\":{value}}}]}}", options); throw new Exception("Expected rejection"); }
        catch (JsonException) { }
    }
    return Task.CompletedTask;
});
Console.WriteLine($"{passed} focused service/contract tests passed.");

class Fixture {
    public Guid UserId = Guid.NewGuid();
    public Game Game = new("Test game", "", null);
    public Match? Match;
    public int Saved, Calls;
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
        var games = Proxy<IGameRepository>((m, a) => m.Name == "GetByIdAsync" ? Task.FromResult<Game?>(Game) : throw new Exception(m.Name));
        var users = Proxy<IUserRepository>((m, a) => m.Name == "GetByIdAsync" ? Task.FromResult<User?>(null) : throw new Exception(m.Name));
        var mapper = new MapperConfiguration(c => c.AddProfile<MappingEntityToDto>()).CreateMapper();
        Service = new(matches, players, Unused<IGameSessionRepository>(), Unused<IGameSessionPlayerRepository>(), users, games,
            Unused<ICampaignRepository>(), Unused<IBGGService>(), Unused<IGameService>(), Unused<INotificationService>(), mapper);
    }
    Task AddMatch(Match match) { Match = match; return Task.CompletedTask; }
    Task<int> Save() { Saved++; return Task.FromResult(1); }
    public CreateMatchDto Request(List<CreateMatchPlayerScoreDto>? scores, Guid? other = null) => new() {
        GameId = Game.Id, GameName = Game.Name, MatchDate = DateTime.UtcNow.AddMinutes(-1),
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