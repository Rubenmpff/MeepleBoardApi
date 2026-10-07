namespace MeepleBoard.Services.Statistics;

public static class StatisticsCalculator
{
    public static readonly string[] Modes = ["COMPETITIVE", "SOLO", "COOPERATIVE", "UNKNOWN"];
    public static string Mode(StatisticsRow row) => row.GameMode is "COMPETITIVE" or "SOLO" or "COOPERATIVE" ? row.GameMode : "UNKNOWN";
    // Preserve legacy evidence, without promoting WinnerId/IsWinner/catalogue guesses into outcomes.
    public static string? Outcome(StatisticsRow row) => row.Result is "Win" or "Loss" or "Draw" && row.Outcome is "Win" or "Loss" or "Draw" ? row.Outcome : null;
    public static StatisticsResults Results(IReadOnlyList<StatisticsRow> rows)
    {
        var wins = rows.Count(r => Outcome(r) == "Win"); var losses = rows.Count(r => Outcome(r) == "Loss");
        var draws = rows.Count(r => Outcome(r) == "Draw"); var known = wins + losses + draws;
        return new(wins, losses, draws, known, rows.Count - known, rows.Count(r => r.Result == null),
            known == 0 ? null : Math.Round(100d * wins / known, 2));
    }
    public static DateOnly LocalDate(StatisticsRow row, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(row.MatchDate, DateTimeKind.Utc), zone));
    public static string Bucket(StatisticsRow row, StatisticsQuery query, TimeZoneInfo zone)
    {
        var date = LocalDate(row, zone);
        return query.EndExclusive.DayNumber - query.Start.DayNumber > 62 ? date.ToString("yyyy-MM") : date.ToString("yyyy-MM-dd");
    }
    public static List<StatisticsGame> Games(IEnumerable<StatisticsRow> rows) => rows.GroupBy(r => r.GameId)
        .Select(g => new StatisticsGame(g.Key, g.First().GameName, g.First().GameImageUrl, g.Count()))
        .OrderByDescending(g => g.Matches).ThenBy(g => g.GameId).ToList();
    public static StatisticsSummary Summary(IReadOnlyList<StatisticsRow> available, StatisticsQuery query, TimeZoneInfo zone)
    {
        available = available.Where(r => query.Mode == null || Mode(r) == query.Mode).DistinctBy(r => r.MatchId).ToList();
        var rows = available.Where(r => !query.GameId.HasValue || r.GameId == query.GameId).DistinctBy(r => r.MatchId).ToList();
        var duration = rows.Where(r => r.DurationInMinutes.HasValue).ToList();
        var ratings = rows.Where(r => r.PersonalRating.HasValue).Select(r => r.PersonalRating!.Value).ToList();
        return new(query.Start, query.EndExclusive, zone.Id, query.GameId, query.Mode,
            rows.Count, rows.Select(r => r.GameId).Distinct().Count(), duration.Count == 0 ? null : duration.Sum(r => (long)r.DurationInMinutes!.Value),
            duration.Count, rows.Count - duration.Count, ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2), ratings.Count,
            Results(rows), Modes.Select(m => { var group = rows.Where(r => Mode(r) == m).ToList(); return new StatisticsMode(m, group.Count, Results(group)); }).ToList(),
            query.EndExclusive.DayNumber - query.Start.DayNumber > 62 ? "month" : "day",
            Evolution(rows, query, zone),
            Games(rows), Games(available));
    }
    public static List<StatisticsBucket> Evolution(IReadOnlyList<StatisticsRow> rows, StatisticsQuery query, TimeZoneInfo zone)
    {
        var counts = rows.GroupBy(r => Bucket(r, query, zone)).ToDictionary(g => g.Key, g => g.Count());
        var monthly = query.EndExclusive.DayNumber - query.Start.DayNumber > 62;
        var cursor = monthly ? new DateOnly(query.Start.Year, query.Start.Month, 1) : query.Start;
        var result = new List<StatisticsBucket>();
        while (cursor < query.EndExclusive) {
            var key = cursor.ToString(monthly ? "yyyy-MM" : "yyyy-MM-dd");
            result.Add(new(key, counts.GetValueOrDefault(key)));
            cursor = monthly ? cursor.AddMonths(1) : cursor.AddDays(1);
        }
        return result;
    }
    public static bool MatchesMetric(StatisticsRow row, string metric) => metric switch
    {
        "all" or "games" => true, "duration" => row.DurationInMinutes.HasValue, "missing-duration" => !row.DurationInMinutes.HasValue, "ratings" => row.PersonalRating.HasValue,
        "known" => Outcome(row) != null, "wins" => Outcome(row) == "Win", "losses" => Outcome(row) == "Loss", "draws" => Outcome(row) == "Draw",
        "undefined" => Outcome(row) == null, "legacy" => row.Result == null,
        _ => throw new ArgumentException("Indicador inválido.")
    };
    public static (TimeZoneInfo Zone, DateTime StartUtc, DateTime EndUtc) Validate(StatisticsQuery query)
    {
        if (query.Start == default || query.EndExclusive <= query.Start || query.EndExclusive.DayNumber - query.Start.DayNumber > 3660)
            throw new ArgumentException("Indica um intervalo válido até dez anos, com fim exclusivo posterior ao início.");
        if (query.GameId == Guid.Empty) throw new ArgumentException("Identificador de jogo inválido.");
        if (query.Mode != null && !Modes.Contains(query.Mode)) throw new ArgumentException("Modo inválido.");
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(query.TimeZone); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { throw new ArgumentException("Fuso horário inválido."); }
        DateTime Utc(DateOnly date) {
            var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) throw new ArgumentException("Este limite local é ambíguo no fuso escolhido.");
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
        return (zone, Utc(query.Start), Utc(query.EndExclusive));
    }
}
