using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    /// <summary>
    /// Repositório de Friendships com operações leves e consistentes.
    /// </summary>
    public sealed class FriendshipRepository : IFriendshipRepository
    {
        private readonly MeepleBoardDbContext _context;

        public FriendshipRepository(MeepleBoardDbContext context)
            => _context = context ?? throw new ArgumentNullException(nameof(context));

        /// <summary>
        /// Garante ordem canónica (A &lt; B) para o par de utilizadores.
        /// </summary>
        private static (Guid A, Guid B) Pair(Guid u1, Guid u2)
            => u1.CompareTo(u2) < 0 ? (u1, u2) : (u2, u1);

        /// <summary>
        /// Indicador "online" nos ecrãs de Amigos: aproximação por pedido HTTP,
        /// não presença em tempo real — considera-se online quem fez algum
        /// pedido autenticado nos últimos 5 minutos.
        /// </summary>
        private static bool IsRecentlyActive(DateTime? lastActiveAt)
            => lastActiveAt.HasValue && lastActiveAt.Value >= DateTime.UtcNow.AddMinutes(-5);

        public async Task<Friendship?> GetByPairAsync(Guid userId1, Guid userId2, CancellationToken ct = default)
        {
            var (a, b) = Pair(userId1, userId2);

            return await _context.Friendships
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserAId == a && x.UserBId == b, ct);
        }

        public Task AddAsync(Friendship friendship, CancellationToken ct = default)
        {
            if (friendship is null) throw new ArgumentNullException(nameof(friendship));

            // Normaliza o par (A,B) antes de inserir (bate certo com o índice único)
            if (friendship.UserAId.CompareTo(friendship.UserBId) > 0)
            {
                (friendship.UserAId, friendship.UserBId) = (friendship.UserBId, friendship.UserAId);
            }

            _context.Friendships.Add(friendship);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Friendship friendship, CancellationToken ct = default)
        {
            if (friendship is null) throw new ArgumentNullException(nameof(friendship));

            // Mantém a ordem canónica mesmo em updates
            if (friendship.UserAId.CompareTo(friendship.UserBId) > 0)
            {
                (friendship.UserAId, friendship.UserBId) = (friendship.UserBId, friendship.UserAId);
            }

            _context.Friendships.Update(friendship);
            return Task.CompletedTask;
        }

        public async Task<bool> ExistsAcceptedAsync(Guid userId1, Guid userId2, CancellationToken ct = default)
        {
            var (a, b) = Pair(userId1, userId2);

            return await _context.Friendships
                .AsNoTracking()
                .AnyAsync(f => f.UserAId == a
                            && f.UserBId == b
                            && f.Status == FriendshipStatus.Accepted, ct);
        }

        /// <summary>
        /// Devolve a lista “leve” (Id + UserName) dos amigos do utilizador.
        /// </summary>
        public async Task<IReadOnlyList<FriendListProjection>> GetFriendsLiteAsync(
            Guid currentUserId, CancellationToken ct = default)
        {
            // ids dos amigos (apenas Accepted)
            var friendIdsQuery =
                _context.Friendships.AsNoTracking()
                    .Where(f => f.Status == FriendshipStatus.Accepted
                             && (f.UserAId == currentUserId || f.UserBId == currentUserId))
                    .Select(f => f.UserAId == currentUserId ? f.UserBId : f.UserAId);

            // projeção leve para evitar carregar entidades completas de Identity
            var friends = await _context.Users
                .AsNoTracking()
                .Where(u => friendIdsQuery.Contains(u.Id))
                .OrderBy(u => u.UserName)
                .Select(u => new
                {
                    u.Id,
                    UserName = u.UserName ?? string.Empty,
                    u.ProfilePictureUrl,
                    u.LastActiveAt
                })
                .ToListAsync(ct);

            var friendIds = friends.Select(x => x.Id).ToList();
            var sharedRows = await _context.MatchPlayers.AsNoTracking()
                .Where(mp => friendIds.Contains(mp.UserId)
                          && mp.Match!.MatchPlayers.Any(p => p.UserId == currentUserId))
                .Select(mp => new
                {
                    FriendId = mp.UserId,
                    mp.MatchId,
                    mp.Match!.MatchDate,
                    GameName = mp.Match.Game!.Name
                })
                .ToListAsync(ct);

            return friends.Select(friend =>
            {
                var rows = sharedRows.Where(x => x.FriendId == friend.Id).ToList();
                var mostPlayedGame = rows.GroupBy(x => x.GameName)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                    .Select(g => g.Key).FirstOrDefault();
                return new FriendListProjection(
                    friend.Id, friend.UserName, friend.ProfilePictureUrl,
                    rows.Select(x => x.MatchId).Distinct().Count(),
                    rows.Count == 0 ? null : rows.Max(x => (DateTime?)x.MatchDate),
                    mostPlayedGame, IsRecentlyActive(friend.LastActiveAt));
            }).ToList();
        }

        /// <summary>
        /// Pedidos pendentes “a entrar” (enviados por outros para mim).
        /// </summary>
        public async Task<IReadOnlyList<(Guid RequestId, Guid FromUserId, string FromUserName, DateTime CreatedAt)>>
            GetIncomingRequestsAsync(Guid currentUserId, CancellationToken ct = default)
        {
            var q = from f in _context.Friendships.AsNoTracking()
                    where f.Status == FriendshipStatus.Pending
                       && (f.UserAId == currentUserId || f.UserBId == currentUserId)
                       && f.InitiatorId != currentUserId
                    join u in _context.Users.AsNoTracking() on f.InitiatorId equals u.Id
                    orderby f.CreatedAt descending
                    select new
                    {
                        f.Id,
                        FromUserId = u.Id,
                        FromUserName = u.UserName ?? string.Empty,
                        f.CreatedAt
                    };

            var list = await q.ToListAsync(ct);

            return list
                .Select(x => (x.Id, x.FromUserId, x.FromUserName, x.CreatedAt))
                .ToList();
        }

        public Task<Friendship?> GetByIdAsync(Guid id, CancellationToken ct = default)
    => _context.Friendships.FirstOrDefaultAsync(x => x.Id == id, ct);

        public async Task<IReadOnlyList<(Guid RequestId, Guid ToUserId, string ToUserName, DateTime CreatedAt)>>
            GetOutgoingRequestsAsync(Guid currentUserId, CancellationToken ct = default)
        {
            var list = await (
                from f in _context.Friendships.AsNoTracking()
                where f.Status == FriendshipStatus.Pending && f.InitiatorId == currentUserId
                let toUserId = f.UserAId == currentUserId ? f.UserBId : f.UserAId
                join u in _context.Users.AsNoTracking() on toUserId equals u.Id
                orderby f.CreatedAt descending
                select new { f.Id, ToUserId = u.Id, ToUserName = u.UserName ?? string.Empty, f.CreatedAt })
                .ToListAsync(ct);

            return list.Select(x => (x.Id, x.ToUserId, x.ToUserName, x.CreatedAt)).ToList();
        }

        public async Task<IReadOnlyList<FriendSearchProjection>> SearchUsersAsync(
            Guid currentUserId, string query, int take, CancellationToken ct = default)
        {
            var normalized = query.Trim();
            var users = await _context.Users.AsNoTracking()
                .Where(u => u.Id != currentUserId
                         && u.UserName != null
                         && EF.Functions.Like(u.UserName, $"%{normalized}%"))
                .OrderBy(u => u.UserName)
                .Take(take)
                .Select(u => new { u.Id, UserName = u.UserName!, u.ProfilePictureUrl, u.LastActiveAt })
                .ToListAsync(ct);

            var ids = users.Select(u => u.Id).ToList();
            var relations = await _context.Friendships.AsNoTracking()
                .Where(f => (f.UserAId == currentUserId && ids.Contains(f.UserBId))
                         || (f.UserBId == currentUserId && ids.Contains(f.UserAId)))
                .ToListAsync(ct);

            return users.Select(u =>
            {
                var relation = relations.FirstOrDefault(f => f.UserAId == u.Id || f.UserBId == u.Id);
                return new FriendSearchProjection(u.Id, u.UserName, u.ProfilePictureUrl,
                    relation?.Status, relation?.Id, relation?.InitiatorId, IsRecentlyActive(u.LastActiveAt));
            }).ToList();
        }

        public async Task<FriendProfileProjection?> GetProfileAsync(
            Guid currentUserId, Guid targetUserId, CancellationToken ct = default)
        {
            var user = await _context.Users.AsNoTracking()
                .Where(u => u.Id == targetUserId)
                .Select(u => new { u.Id, UserName = u.UserName ?? string.Empty, u.ProfilePictureUrl, u.LibraryPrivacy, u.LastActiveAt })
                .FirstOrDefaultAsync(ct);
            if (user is null) return null;

            var friendship = await GetByPairAsync(currentUserId, targetUserId, ct);
            var isAcceptedFriend = friendship?.Status == FriendshipStatus.Accepted;
            var canViewLibrary = user.LibraryPrivacy switch
            {
                LibraryPrivacy.Public => true,
                LibraryPrivacy.FriendsOnly => isAcceptedFriend,
                LibraryPrivacy.Private => false,
                _ => false
            };

            var targetMatches = _context.Matches.AsNoTracking()
                .Where(m => m.MatchPlayers.Any(p => p.UserId == targetUserId));
            var sharedMatchesQuery = targetMatches
                .Where(m => m.MatchPlayers.Any(p => p.UserId == currentUserId));

            var totalMatches = await targetMatches.CountAsync(ct);
            var totalGamesPlayed = await targetMatches.Select(m => m.GameId).Distinct().CountAsync(ct);
            // 🔒 Só conta a coleção se a privacidade permitir — senão o número
            // já revelaria "quantos jogos tens" a quem a coleção diz que não pode ver.
            var totalGamesOwned = canViewLibrary
                ? await _context.UserGameLibraries.AsNoTracking()
                    .CountAsync(x => x.UserId == targetUserId && x.Status == GameLibraryStatus.Owned, ct)
                : 0;

            if (friendship?.Status != FriendshipStatus.Accepted)
            {
                return new FriendProfileProjection(
                    user.Id, user.UserName, user.ProfilePictureUrl, friendship?.Status,
                    friendship?.Id, friendship?.InitiatorId, null,
                    totalMatches, totalGamesPlayed, totalGamesOwned,
                    0, 0, 0, 0, 0, 0, 0,
                    Array.Empty<SharedGameProjection>(), Array.Empty<SharedGameProjection>(),
                    Array.Empty<SharedMatchProjection>(), Array.Empty<SharedSessionProjection>(),
                    user.LibraryPrivacy.ToString(), canViewLibrary, IsRecentlyActive(user.LastActiveAt));
            }

            var sharedRows = await sharedMatchesQuery
                .OrderByDescending(m => m.MatchDate)
                .Select(m => new
                {
                    m.Id,
                    m.GameId,
                    GameName = m.Game!.Name,
                    m.Game.ImageUrl,
                    m.Game.IsCooperative,
                    m.MatchDate,
                    m.DurationInMinutes,
                    CurrentWon = m.MatchPlayers.Any(p => p.UserId == currentUserId && p.IsWinner),
                    OtherWon = m.MatchPlayers.Any(p => p.UserId == targetUserId && p.IsWinner)
                })
                .ToListAsync(ct);

            var topGames = sharedRows
                .GroupBy(m => new { m.GameId, m.GameName, m.ImageUrl, m.IsCooperative })
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.GameName)
                .Take(5)
                .Select(g => new SharedGameProjection(
                    g.Key.GameId, g.Key.GameName, g.Key.ImageUrl, g.Count(),
                    g.Count(x => !x.IsCooperative && x.CurrentWon),
                    g.Count(x => !x.IsCooperative && x.OtherWon),
                    g.Count(x => x.IsCooperative && x.CurrentWon && x.OtherWon),
                    g.Key.IsCooperative))
                .ToList();

            // 🔒 "Jogos em comum" mostra quais jogos o amigo possui — só faz sentido
            // devolver isto se a privacidade da coleção permitir vê-la.
            IReadOnlyList<SharedGameProjection> commonOwned = Array.Empty<SharedGameProjection>();
            if (canViewLibrary)
            {
                var ownedByCurrent = _context.UserGameLibraries.AsNoTracking()
                    .Where(x => x.UserId == currentUserId && x.Status == GameLibraryStatus.Owned)
                    .Select(x => x.GameId);
                commonOwned = await _context.UserGameLibraries.AsNoTracking()
                    .Where(x => x.UserId == targetUserId && x.Status == GameLibraryStatus.Owned
                             && ownedByCurrent.Contains(x.GameId))
                    .OrderBy(x => x.Game!.Name).Take(8)
                    .Select(x => new SharedGameProjection(x.GameId, x.Game!.Name, x.Game.ImageUrl,
                        0, 0, 0, 0, x.Game.IsCooperative))
                    .ToListAsync(ct);
            }

            var sharedSessionRows = await _context.GameSessions.AsNoTracking()
                .Where(s => s.Players.Any(p => p.UserId == currentUserId && p.Status == GameSessionInviteStatus.Accepted)
                         && s.Players.Any(p => p.UserId == targetUserId && p.Status == GameSessionInviteStatus.Accepted))
                .OrderByDescending(s => s.ScheduledStartDate)
                .Select(s => new SharedSessionProjection(s.Id, s.Name, s.ScheduledStartDate, s.Matches.Count))
                .ToListAsync(ct);

            var recentMatches = sharedRows.Take(5).Select(m =>
            {
                var result = m.IsCooperative
                    ? (m.CurrentWon && m.OtherWon ? "teamWin" : "teamLoss")
                    : m.CurrentWon ? "currentUserWin" : m.OtherWon ? "otherUserWin" : "draw";
                return new SharedMatchProjection(m.Id, m.GameId, m.GameName, m.ImageUrl, m.MatchDate, result);
            }).ToList();

            var competitive = sharedRows.Where(m => !m.IsCooperative).ToList();
            return new FriendProfileProjection(
                user.Id, user.UserName, user.ProfilePictureUrl, friendship?.Status,
                friendship?.Id, friendship?.InitiatorId,
                friendship?.Status == FriendshipStatus.Accepted ? friendship.UpdatedAt ?? friendship.CreatedAt : null,
                totalMatches, totalGamesPlayed, totalGamesOwned,
                sharedRows.Count, sharedRows.Select(m => m.GameId).Distinct().Count(),
                sharedRows.Sum(m => m.DurationInMinutes ?? 0), sharedSessionRows.Count,
                competitive.Count(m => m.CurrentWon), competitive.Count(m => m.OtherWon),
                competitive.Count(m => !m.CurrentWon && !m.OtherWon), topGames, commonOwned,
                recentMatches, sharedSessionRows.Take(5).ToList(),
                user.LibraryPrivacy.ToString(), canViewLibrary, IsRecentlyActive(user.LastActiveAt));
        }

        /// <summary>
        /// Query base de partidas onde ambos os utilizadores participaram —
        /// reutilizada pela paginação da tab "Partidas" e pelo histórico por jogo.
        /// </summary>
        private IQueryable<Match> SharedMatchesQuery(Guid userId1, Guid userId2)
            => _context.Matches.AsNoTracking()
                .Where(m => m.MatchPlayers.Any(p => p.UserId == userId1)
                         && m.MatchPlayers.Any(p => p.UserId == userId2));

        private static SharedMatchDetailProjection MapSharedMatchDetail(
            Guid currentUserId, Guid targetUserId,
            Guid matchId, Guid gameId, string gameName, string? imageUrl, bool isCooperative,
            DateTime matchDate, int? durationInMinutes, string? location,
            bool currentWon, bool otherWon, int? currentScore, int? otherScore)
        {
            var result = isCooperative
                ? (currentWon && otherWon ? "teamWin" : "teamLoss")
                : currentWon ? "currentUserWin" : otherWon ? "otherUserWin" : "draw";

            return new SharedMatchDetailProjection(
                matchId, gameId, gameName, imageUrl, matchDate, result,
                durationInMinutes, location, currentScore, otherScore);
        }

        public async Task<(IReadOnlyList<SharedMatchDetailProjection> Items, int TotalCount)> GetSharedMatchesAsync(
            Guid currentUserId, Guid targetUserId, int page, int pageSize, CancellationToken ct = default)
        {
            var query = SharedMatchesQuery(currentUserId, targetUserId);
            var totalCount = await query.CountAsync(ct);

            var rows = await query
                .OrderByDescending(m => m.MatchDate)
                .Skip(Math.Max(0, page) * pageSize)
                .Take(pageSize)
                .Select(m => new
                {
                    m.Id,
                    m.GameId,
                    GameName = m.Game!.Name,
                    m.Game.ImageUrl,
                    m.Game.IsCooperative,
                    m.MatchDate,
                    m.DurationInMinutes,
                    m.Location,
                    CurrentWon = m.MatchPlayers.Any(p => p.UserId == currentUserId && p.IsWinner),
                    OtherWon = m.MatchPlayers.Any(p => p.UserId == targetUserId && p.IsWinner),
                    CurrentScore = m.MatchPlayers.Where(p => p.UserId == currentUserId).Select(p => p.Score).FirstOrDefault(),
                    OtherScore = m.MatchPlayers.Where(p => p.UserId == targetUserId).Select(p => p.Score).FirstOrDefault(),
                })
                .ToListAsync(ct);

            var items = rows.Select(m => MapSharedMatchDetail(
                currentUserId, targetUserId, m.Id, m.GameId, m.GameName, m.ImageUrl, m.IsCooperative,
                m.MatchDate, m.DurationInMinutes, m.Location, m.CurrentWon, m.OtherWon, m.CurrentScore, m.OtherScore)).ToList();

            return (items, totalCount);
        }

        public async Task<IReadOnlyList<SharedMatchDetailProjection>> GetSharedMatchesForGameAsync(
            Guid currentUserId, Guid targetUserId, Guid gameId, CancellationToken ct = default)
        {
            var rows = await SharedMatchesQuery(currentUserId, targetUserId)
                .Where(m => m.GameId == gameId)
                .OrderByDescending(m => m.MatchDate)
                .Take(200) // limite de segurança — não há paginação neste ecrã por agora
                .Select(m => new
                {
                    m.Id,
                    m.GameId,
                    GameName = m.Game!.Name,
                    m.Game.ImageUrl,
                    m.Game.IsCooperative,
                    m.MatchDate,
                    m.DurationInMinutes,
                    m.Location,
                    CurrentWon = m.MatchPlayers.Any(p => p.UserId == currentUserId && p.IsWinner),
                    OtherWon = m.MatchPlayers.Any(p => p.UserId == targetUserId && p.IsWinner),
                    CurrentScore = m.MatchPlayers.Where(p => p.UserId == currentUserId).Select(p => p.Score).FirstOrDefault(),
                    OtherScore = m.MatchPlayers.Where(p => p.UserId == targetUserId).Select(p => p.Score).FirstOrDefault(),
                })
                .ToListAsync(ct);

            return rows.Select(m => MapSharedMatchDetail(
                currentUserId, targetUserId, m.Id, m.GameId, m.GameName, m.ImageUrl, m.IsCooperative,
                m.MatchDate, m.DurationInMinutes, m.Location, m.CurrentWon, m.OtherWon, m.CurrentScore, m.OtherScore)).ToList();
        }


        public Task SaveChangesAsync(CancellationToken ct = default)
            => _context.SaveChangesAsync(ct);

    }
}