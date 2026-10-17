using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class StatisticsRepository(MeepleBoardDbContext db) : IStatisticsRepository
    {
        // The person's matches under the filter, as a subquery of the other reads.
        IQueryable<Guid> OwnMatches(StatisticsMatchFilter f)
        {
            var known = f.KnownModes.ToArray();
            return db.MatchPlayers.Where(p => p.UserId == f.UserId && p.Match!.MatchDate >= f.StartUtc && p.Match.MatchDate < f.EndUtc
                && (!f.GameId.HasValue || p.Match.GameId == f.GameId)
                && (f.Mode == null || (f.Mode == "UNKNOWN" ? p.Match.GameMode == null || !known.Contains(p.Match.GameMode) : p.Match.GameMode == f.Mode)))
                .Select(p => p.MatchId);
        }

        public async Task<IReadOnlyList<StatisticsRow>> GetPlayedRowsAsync(Guid userId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
            => await db.MatchPlayers.AsNoTracking().Where(p => p.UserId == userId && p.Match!.MatchDate >= startUtc && p.Match.MatchDate < endUtc)
                .Select(p => new StatisticsRow(p.MatchId, p.Match!.GameId, p.Match.Game!.Name, p.Match.Game.ImageUrl, p.Match.MatchDate,
                    p.Match.GameMode, p.Match.Result, p.Outcome, p.Match.DurationInMinutes, p.Score,
                    p.Match.JournalEntries.Where(e => e.UserId == userId).Select(e => e.PersonalRating).FirstOrDefault()))
                .ToListAsync(ct);

        public async Task<IReadOnlyList<Guid>> GetAcceptedFriendIdsAsync(Guid userId, CancellationToken ct = default)
            => await db.Friendships.AsNoTracking()
                .Where(f => f.Status == FriendshipStatus.Accepted && (f.UserAId == userId || f.UserBId == userId))
                .Select(f => f.UserAId == userId ? f.UserBId : f.UserAId).ToListAsync(ct);

        public Task<bool> AreAcceptedFriendsAsync(Guid userId, Guid friendId, CancellationToken ct = default)
            => db.Friendships.AnyAsync(f => f.Status == FriendshipStatus.Accepted
                && ((f.UserAId == userId && f.UserBId == friendId) || (f.UserBId == userId && f.UserAId == friendId)), ct);

        public async Task<IReadOnlyList<CompanionProjection>> GetCompanionsAsync(StatisticsMatchFilter filter, IReadOnlyList<Guid> friendIds, string groupContext, CancellationToken ct = default)
        {
            var friends = friendIds.ToList(); var own = OwnMatches(filter);
            var counts = await db.MatchPlayers.AsNoTracking().Where(p => friends.Contains(p.UserId) && own.Contains(p.MatchId)
                    && (groupContext == "all" || (groupContext == "only-two" ? p.Match!.MatchPlayers.Select(x => x.UserId).Distinct().Count() == 2 : p.Match!.MatchPlayers.Select(x => x.UserId).Distinct().Count() >= 3)))
                .GroupBy(p => p.UserId).Select(g => new { Id = g.Key, Matches = g.Select(p => p.MatchId).Distinct().Count() }).ToListAsync(ct);
            // «Com quem jogaste»: only friends with at least one shared match under the selected filters.
            var played = counts.Select(c => c.Id).ToList();
            var names = await db.Users.AsNoTracking().Where(u => played.Contains(u.Id)).Select(u => new { u.Id, u.UserName }).ToListAsync(ct);
            return names.Select(n => new CompanionProjection(n.Id, n.UserName, counts.First(c => c.Id == n.Id).Matches)).ToList();
        }

        public async Task<IReadOnlyList<PartnerProjection>> GetPartnerRowsAsync(StatisticsMatchFilter filter, Guid friendId, CancellationToken ct = default)
        {
            var userId = filter.UserId; var own = OwnMatches(filter);
            return await db.MatchPlayers.AsNoTracking().Where(p => p.UserId == friendId && own.Contains(p.MatchId))
                .Select(p => new PartnerProjection(p.MatchId, p.Score, p.Outcome,
                    p.Match!.MatchPlayers.Select(x => x.UserId).Distinct().Count(),
                    p.Match!.MatchPlayers.Any(x => x.UserId != userId && x.UserId != friendId && x.Outcome == "Win"),
                    p.Match.MatchPlayers.Any(x => x.UserId != userId && x.UserId != friendId && x.Outcome == "Draw"))).ToListAsync(ct);
        }

        public Task<string?> GetUserNameAsync(Guid userId, CancellationToken ct = default)
            => db.Users.Where(u => u.Id == userId).Select(u => u.UserName).SingleAsync(ct);

        public Task<Dictionary<Guid, DateTime>> GetFirstPlayedDatesAsync(Guid userId, CancellationToken ct = default)
            => db.MatchPlayers.AsNoTracking().Where(p => p.UserId == userId).GroupBy(p => p.Match!.GameId)
                .Select(g => new { GameId = g.Key, Date = g.Min(p => p.Match!.MatchDate) }).ToDictionaryAsync(x => x.GameId, x => x.Date, ct);

        public async Task<IReadOnlyList<LibraryEntryProjection>> GetLibraryAddedAsync(Guid userId, DateTime startUtc, DateTime endUtc, Guid? gameId, CancellationToken ct = default)
            => await db.UserGameLibraries.AsNoTracking().Where(l => l.UserId == userId && l.AddedAt >= startUtc && l.AddedAt < endUtc
                    && (!gameId.HasValue || l.GameId == gameId))
                .Select(l => new LibraryEntryProjection(l.Id, l.GameId, l.Game!.Name, l.Game.ImageUrl, l.Status, l.PricePaid, l.AddedAt)).ToListAsync(ct);

        public async Task<IReadOnlyList<Guid>> GetGameIdsPlayedInModeAsync(Guid userId, string mode, IReadOnlyList<string> knownModes, CancellationToken ct = default)
        {
            var known = knownModes.ToArray();
            return await db.MatchPlayers.Where(p => p.UserId == userId && (mode == "UNKNOWN" ? p.Match!.GameMode == null || !known.Contains(p.Match.GameMode) : p.Match!.GameMode == mode))
                .Select(p => p.Match!.GameId).Distinct().ToListAsync(ct);
        }
    }
}
