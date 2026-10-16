// Duration statistics of a game: the calculator's rules and the match entity's duration method. No database.
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Services.GameDurations;
using static MeepleBoard.Domain.Enums.DurationMethod;

var passed = 0;
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();
DurationRow Row(int? minutes, DurationMethod? method = Estimated, Guid? creator = null, bool solo = false, int players = 2, bool mine = false, bool shares = true, Guid? id = null)
    => new(id ?? Guid.NewGuid(), creator ?? A, minutes, method, solo, players, mine, shares);
List<DurationRow> Spread(params (Guid creator, int minutes)[] items) => items.Select(i => Row(i.minutes, creator: i.creator)).ToList();

Check("a measured or estimated duration needs minutes above zero; «Não sei» has none; older clients may send minutes only", () => {
    var m = new Match(Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));
    m.SetDuration(65, Estimated); Assert(m.DurationInMinutes == 65 && m.DurationMethod == Estimated, "estimate kept");
    m.SetDuration(null, Unknown); Assert(m.DurationInMinutes == null && m.DurationMethod == Unknown, "Não sei kept");
    foreach (var (min, method) in new (int?, DurationMethod)[] { (0, Measured), (null, Estimated), (30, Unknown) })
    { var threw = false; try { m.SetDuration(min, method); } catch (ArgumentException) { threw = true; } Assert(threw, $"{min}/{method} must be refused"); }
    m.SetDuration(48, Measured); m.UpdateMatchDetails(null, null, 50);
    Assert(m.DurationInMinutes == 50 && m.DurationMethod == null, "minutes changed without a method: the method is no longer known");
});

Check("your average uses your matches only; «Não sei», no duration and old zeros are left out and counted apart, never as zero", () => {
    var rows = new List<DurationRow> { Row(60, Measured, mine: true), Row(40, Estimated, mine: true), Row(50, null, mine: true), Row(null, Unknown, mine: true),
        Row(null, null, mine: true), Row(0, null, mine: true), Row(999, mine: false) };
    var p = GameDurationCalculator.Calculate(rows).Personal.Multiplayer;
    Assert(p.Available && p.Count == 3 && p.Mean == 50, $"mean of 60, 40, 50 = 50 (got {p.Mean}, n={p.Count})");
    Assert(p.Measured == 1 && p.Estimated == 1 && p.NotStated == 1, "methods counted: old positive durations are «not stated», never «estimated»");
    Assert(p.ExcludedUnknown == 1 && p.ExcludedMissing == 1 && p.ExcludedZero == 1, "left out, counted");
    Assert(p.Median == null && p.Q1 == null && p.Distant == 0, "fewer than 5 values: no quartiles nor distant values");
});

Check("solo and multiplayer never mix; by number of players 2, 3, 4 and 5 or more", () => {
    var rows = new List<DurationRow> { Row(30, solo: true, players: 1, mine: true), Row(60, players: 2, mine: true), Row(80, players: 4, mine: true), Row(100, players: 7, mine: true) };
    var p = GameDurationCalculator.Calculate(rows).Personal;
    Assert(p.Solo.Count == 1 && p.Solo.Mean == 30 && p.Multiplayer.Count == 3 && p.Multiplayer.Mean == 80, "separate modes");
    Assert(p.ByPlayers.Single(b => b.Players == 5).Stats.Mean == 100 && !p.ByPlayers.Single(b => b.Players == 3).Stats.Available, "7 players fall in 5+; empty group unavailable");
});

Check("each match counts once, however many participants or rows it has", () => {
    var id = Guid.NewGuid();
    var rows = new List<DurationRow> { Row(60, players: 4, mine: true, id: id), Row(60, players: 4, mine: true, id: id), Row(60, players: 4, mine: true, id: id) };
    Assert(GameDurationCalculator.Calculate(rows).Personal.Multiplayer.Count == 1, "one match");
});

Check("community: only matches every participant shares; below 5 matches or 3 people, or one person with more than half, nothing is shown", () => {
    var five = Spread((A, 40), (A, 50), (B, 60), (B, 70), (C, 80));
    Assert(GameDurationCalculator.Protected(five), "5 matches by 3 people, none above half");
    Assert(!GameDurationCalculator.Protected(five.Take(4).ToList()), "4 matches are not enough");
    Assert(!GameDurationCalculator.Protected(Spread((A, 40), (A, 50), (A, 60), (B, 70), (C, 80))), "one person with 3 of 5 (more than half)");
    Assert(!GameDurationCalculator.Protected(Spread((A, 40), (A, 50), (B, 60), (B, 70), (B, 80), (A, 90))), "only 2 people");
    var c = GameDurationCalculator.Calculate(five.Concat(new[] { Row(500, creator: D, shares: false) }).ToList()).Community.Multiplayer;
    Assert(c.Available && c.Count == 5 && c.Mean == 60, $"the non-shared match does not count (mean {c.Mean})");
    var hidden = GameDurationCalculator.Calculate(five.Take(4).ToList()).Community.Multiplayer;
    Assert(!hidden.Available && hidden.Count == 0 && hidden.Mean == null, "insufficient: no number at all");
});

Check("the community average is the same set for everyone: the viewer's own shared matches count like any other", () => {
    var rows = Spread((A, 40), (A, 50), (B, 60), (B, 70), (C, 80));
    var asOutsider = GameDurationCalculator.Calculate(rows).Community.Multiplayer;
    var asPlayer = GameDurationCalculator.Calculate(rows.Select((r, i) => i < 2 ? r with { ViewerPlayed = true } : r).ToList()).Community.Multiplayer;
    Assert(asOutsider == asPlayer, "same value whoever reads it");
});

Check("median, middle half (Q1–Q3 by linear interpolation) and very distant values; distant values stay in the mean", () => {
    var values = new[] { 40, 45, 50, 55, 60, 240 };
    var p = GameDurationCalculator.Calculate(values.Select(v => Row(v, mine: true)).ToList()).Personal.Multiplayer;
    // sorted 40,45,50,55,60,240: Q1 at 1.25 = 46.25, median 52.5, Q3 at 3.75 = 58.75, IQR 12.5, upper fence 77.5
    Assert(p.Q1 == 46 && p.Median == 53 && p.Q3 == 59, $"quartiles {p.Q1}/{p.Median}/{p.Q3}");
    Assert(p.Distant == 1 && p.DistantValues!.SequenceEqual(new[] { 240 }), "240 is very distant and listed for you");
    Assert(p.Mean == 82, $"the distant value counts in the mean (490/6 = 81.7 → 82, got {p.Mean})");
    Assert(Math.Abs(GameDurationCalculator.Quantile(new double[] { 1, 2, 3, 4 }, 0.25) - 1.75) < 1e-9, "type 7 quantile");
});

Check("community values are rounded to 5 minutes and distant values are only counted", () => {
    var rows = Spread((A, 41), (A, 47), (B, 52), (B, 58), (C, 63), (C, 300));
    var c = GameDurationCalculator.Calculate(rows).Community.Multiplayer;
    Assert(c.Mean % 5 == 0 && c.Median % 5 == 0 && c.Q1 % 5 == 0 && c.Q3 % 5 == 0, "multiples of 5");
    Assert(c.Distant == 1 && c.DistantValues == null, "count only, no values");
    Assert(c.ExcludedUnknown == null && c.ExcludedMissing == null, "nothing about non-counted matches");
});

Check("complementary suppression: if only one player group with matches is hidden, the smallest shown one is hidden too", () => {
    var two = Spread((A, 40), (A, 45), (B, 50), (B, 55), (C, 60), (C, 65));
    var three = Spread((A, 70), (A, 75), (B, 80), (B, 85), (C, 90)).Select(r => r with { Players = 3 }).ToList();
    var four = new List<DurationRow> { Row(120, creator: A, players: 4), Row(130, creator: B, players: 4) };
    var c = GameDurationCalculator.Calculate(two.Concat(three).Concat(four).ToList()).Community;
    Assert(c.Multiplayer.Available, "total shown");
    Assert(!c.ByPlayers.Single(b => b.Players == 4).Stats.Available, "4 players: too few");
    Assert(!c.ByPlayers.Single(b => b.Players == 3).Stats.Available && c.ByPlayers.Single(b => b.Players == 2).Stats.Available,
        "3 players (5 matches, the smallest shown) hidden too; 2 players (6) stays");
});

Console.WriteLine($"{passed} checks passed");
