using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeepleBoard.Domain.Entities;
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
        public async Task<IReadOnlyList<(Guid FriendId, string FriendUserName)>> GetFriendsLiteAsync(
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
                    // Identity pode permitir null aqui; garantimos não-nulo para a tupla:
                    UserName = u.UserName ?? string.Empty
                })
                .ToListAsync(ct);

            // materializa em tuplas readonly
            return friends
                .Select(x => (x.Id, x.UserName))
                .ToList();
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

        public Task SaveChangesAsync(CancellationToken ct = default)
            => _context.SaveChangesAsync(ct);
    }
}
