using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Duration statistics of a game: «A tua média» (the viewer's matches) and «Média MeepleBoard» (matches every
    /// participant shares), by mode and by number of players. Each match counts once. Unknown durations, «Não sei»
    /// and old zero minutes never count, not even as zero. Nothing is excluded or corrected silently: very distant
    /// values stay in the mean and are only counted.
    /// </summary>
    public static class GameDurationCalculator
    {
        /// <summary>Minimum protection for a community group (an initial safeguard, not a guarantee of anonymity).</summary>
        public const int MinMatches = 5, MinCreators = 3;
        /// <summary>Quartiles and very distant values need at least this many values.</summary>
        public const int MinForSpread = 5;
        public const int CommunityRounding = 5;

        public static bool Valid(DurationRow r) => r.Minutes is > 0 && r.Method != DurationMethod.Unknown;

        /// <summary>1 for solo; 2, 3, 4 and 5 (= 5 or more) for multiplayer.</summary>
        public static int Bucket(DurationRow r) => r.Solo ? 1 : Math.Clamp(r.Players, 2, 5);

        public static GameDurationsDto Calculate(IReadOnlyList<DurationRow> rows)
        {
            var rowsOnce = rows.DistinctBy(r => r.MatchId).ToList();
            var mine = rowsOnce.Where(r => r.ViewerPlayed).ToList();
            var shared = rowsOnce.Where(r => r.EveryoneShares && Valid(r)).ToList();
            return new GameDurationsDto(
                new DurationSideDto(Personal(mine.Where(r => r.Solo)), Personal(mine.Where(r => !r.Solo)),
                    new[] { 2, 3, 4, 5 }.Select(b => new DurationBucketDto(b, Personal(mine.Where(r => !r.Solo && Bucket(r) == b)))).ToList()),
                Community(shared));
        }

        static DurationStatsDto Personal(IEnumerable<DurationRow> group)
        {
            var all = group.ToList();
            var valid = all.Where(Valid).ToList();
            var stats = Stats(valid, round: 1, spreadValues: true);
            return stats with
            {
                Available = valid.Count > 0,
                ExcludedUnknown = all.Count(r => r.Method == DurationMethod.Unknown),
                ExcludedMissing = all.Count(r => r.Minutes == null && r.Method != DurationMethod.Unknown),
                ExcludedZero = all.Count(r => r.Minutes == 0)
            };
        }

        static DurationSideDto Community(List<DurationRow> shared)
        {
            DurationStatsDto Group(IEnumerable<DurationRow> g) { var l = g.ToList(); return Protected(l) ? Stats(l, CommunityRounding, spreadValues: false) : DurationStatsDto.Insufficient; }
            var solo = Group(shared.Where(r => r.Solo));
            var multi = Group(shared.Where(r => !r.Solo));
            var buckets = new[] { 2, 3, 4, 5 }.Select(b => (b, rows: shared.Where(r => !r.Solo && Bucket(r) == b).ToList()))
                .Select(x => (x.b, x.rows, stats: Group(x.rows))).ToList();
            // Complementary suppression: when only one group with matches would be hidden while the total is shown,
            // the smallest shown group is hidden too, so the hidden one cannot be deduced from the total.
            if (multi.Available)
            {
                var hidden = buckets.Count(x => !x.stats.Available && x.rows.Count > 0);
                if (hidden == 1)
                {
                    var smallest = buckets.Where(x => x.stats.Available).OrderBy(x => x.rows.Count).ThenBy(x => x.b).FirstOrDefault();
                    if (smallest.rows != null) buckets = buckets.Select(x => x.b == smallest.b ? (x.b, x.rows, DurationStatsDto.Insufficient) : x).ToList();
                }
            }
            return new DurationSideDto(solo, multi, buckets.Select(x => new DurationBucketDto(x.b, x.stats)).ToList());
        }

        /// <summary>At least 5 matches, registered by at least 3 people, none with more than half. Unknown creators count as one person.</summary>
        public static bool Protected(IReadOnlyCollection<DurationRow> group)
        {
            if (group.Count < MinMatches) return false;
            var byCreator = group.GroupBy(r => r.CreatorId ?? Guid.Empty).Select(g => g.Count()).ToList();
            return byCreator.Count >= MinCreators && byCreator.Max() * 2 <= group.Count;
        }

        static DurationStatsDto Stats(List<DurationRow> valid, int round, bool spreadValues)
        {
            if (valid.Count == 0) return DurationStatsDto.Insufficient;
            var values = valid.Select(r => (double)r.Minutes!.Value).OrderBy(v => v).ToList();
            var withSpread = values.Count >= MinForSpread;
            double? q1 = withSpread ? Quantile(values, 0.25) : null, q3 = withSpread ? Quantile(values, 0.75) : null;
            var distant = new List<int>();
            if (q1.HasValue && q3.HasValue)
            {
                var iqr = q3.Value - q1.Value;
                distant = values.Where(v => v < q1 - 1.5 * iqr || v > q3 + 1.5 * iqr).Select(v => (int)v).ToList();
            }
            return new DurationStatsDto(
                Available: true, Count: values.Count,
                Measured: valid.Count(r => r.Method == DurationMethod.Measured),
                Estimated: valid.Count(r => r.Method == DurationMethod.Estimated),
                NotStated: valid.Count(r => r.Method == null),
                Mean: Round(values.Average(), round), Median: withSpread ? Round(Quantile(values, 0.5), round) : null,
                Q1: q1.HasValue ? Round(q1.Value, round) : null, Q3: q3.HasValue ? Round(q3.Value, round) : null,
                Distant: distant.Count, DistantValues: spreadValues ? distant : null);
        }

        /// <summary>Linear interpolation between ordered values (the usual «type 7» quantile).</summary>
        public static double Quantile(IReadOnlyList<double> sorted, double p)
        {
            var pos = (sorted.Count - 1) * p;
            var lo = (int)Math.Floor(pos);
            var hi = Math.Min(lo + 1, sorted.Count - 1);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }

        static int Round(double minutes, int step) => (int)(Math.Round(minutes / step, MidpointRounding.AwayFromZero) * step);
    }
}
