using MeepleBoard.Services.Statistics;

namespace MeepleBoard.Services.Calculators;

/// <summary>One shared match as the company comparison needs it: my row plus the friend's result and the other players.</summary>
public sealed record PairRow(StatisticsRow Own, int? FriendScore, string? FriendOutcome, bool OtherWin, bool OtherDraw, int? ParticipantCount = null);

public static class CompanyCalculator
{
 public static string ValidateContext(string context) => context is "all" or "only-two" or "group" ? context : throw new ArgumentException("Contexto de companhia inválido.");
 public static bool InContext(PairRow pair, string context) => ValidateContext(context) switch {
  "only-two" => pair.ParticipantCount == 2, "group" => pair.ParticipantCount >= 3, _ => true
 };
 public static PairContextCounts ContextCounts(IReadOnlyList<PairRow> rows) => new(rows.Count,
  rows.Count(p => p.ParticipantCount == 2), rows.Count(p => p.ParticipantCount >= 3), rows.Count(p => p.ParticipantCount == null || p.ParticipantCount < 2));
 public static string Category(PairRow pair) {
  var r = pair.Own; var mine = StatisticsCalculator.Outcome(r);
  var other = r.Result is "Win" or "Loss" or "Draw" && pair.FriendOutcome is "Win" or "Loss" or "Draw" ? pair.FriendOutcome : null;
  if (mine == null || other == null) return "unknown-pair";
  if (StatisticsCalculator.Mode(r) == "COOPERATIVE")
   return mine == other && mine == r.Result ? "team-" + mine.ToLowerInvariant() : "unknown-pair";
  if (StatisticsCalculator.Mode(r) != "COMPETITIVE") return "unknown-pair";
  return (r.Result, mine, other) switch {
   ("Win", "Win", "Win") => "shared-win", ("Win", "Win", "Loss") => "my-only-win",
   ("Win", "Loss", "Win") => "friend-only-win", ("Win", "Loss", "Loss") when pair.OtherWin => "other-win",
   ("Draw", "Draw", "Draw") => "draw-together", ("Draw", "Draw", "Loss") or ("Draw", "Loss", "Draw") => "draw-with-other",
   ("Draw", "Loss", "Loss") when pair.OtherDraw => "other-draw", _ => "unknown-pair"
  };
 }
 public static readonly string[] Metrics = ["pair-all", "my-win", "friend-win", "shared-win", "my-only-win", "friend-only-win", "pair-draw", "draw-together", "draw-with-other", "other-win", "other-shared-win", "other-draw", "team-win", "team-loss", "team-draw", "pair-known", "unknown-pair", "paired-scores"];
 public static bool Matches(PairRow row, string metric) {
  var category = Category(row);
  return metric switch {
   "pair-all" => true, "my-win" => category is "my-only-win" or "shared-win", "friend-win" => category is "friend-only-win" or "shared-win",
   "pair-draw" => category is "draw-together" or "draw-with-other", "pair-known" => category != "unknown-pair",
   "other-shared-win" => row.OtherWin && category is "my-only-win" or "friend-only-win" or "shared-win",
   "paired-scores" => row.Own.Score.HasValue && row.FriendScore.HasValue,
   _ when Metrics.Contains(metric) => category == metric,
   _ => throw new ArgumentException("Indicador de companhia inválido.")
  };
 }
 public static PairResults Results(IReadOnlyList<PairRow> rows) {
  int Count(string metric) => rows.Count(r => Matches(r, metric));
  return new(Count("my-win"), Count("friend-win"), Count("shared-win"), Count("my-only-win"), Count("friend-only-win"),
   Count("pair-draw"), Count("draw-together"), Count("other-win"), Count("other-shared-win"), Count("other-draw"), Count("team-win"), Count("team-loss"), Count("team-draw"), Count("pair-known"), Count("unknown-pair"));
 }
}
