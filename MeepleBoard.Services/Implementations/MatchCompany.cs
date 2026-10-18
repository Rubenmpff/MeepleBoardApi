using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Who may be registered together in a match (docs/proposta-bloqueio-comunidade.md in the app). Never two people with a
    /// block between them, whoever registers. Outside sessions, every other player is an accepted friend of whoever
    /// registers, except a campaign encounter: there the players are accepted members of that campaign. Inside a session,
    /// accepted participants (checked by MatchService), with or without friendship.
    /// </summary>
    public static class MatchCompany
    {
        public const string Blocked = "Não é possível registar a partida com estes jogadores.";
        public const string NotFriends = "Só podes registar partidas com amigos com amizade aceite.";
        public const string NotCampaignMembers = "Todos os jogadores têm de ser membros desta campanha.";

        public static async Task RequireNoBlockAsync(ICommunityRepository community, IEnumerable<Guid> players, CancellationToken ct)
        {
            if (await community.AnyBlockAmongAsync(players, ct)) throw new InvalidOperationException(Blocked);
        }

        /// <summary>Outside a session: friends of whoever registers, or members of the campaign of an encounter.</summary>
        public static async Task RequireOutsideSessionAsync(Guid actorId, IEnumerable<Guid> players, Guid? campaignId,
            IFriendshipRepository friendships, ICampaignRepository campaigns, CancellationToken ct)
        {
            var others = players.Where(p => p != actorId).Distinct().ToList();
            if (others.Count == 0) return;
            if (campaignId is { } id)
            {
                var campaign = await campaigns.GetByIdForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Campanha não encontrada.");
                var members = campaign.Members.Where(m => m.Status == CampaignMemberStatus.Accepted).Select(m => m.UserId).Append(campaign.CreatorId).ToHashSet();
                if (!members.Contains(actorId)) throw new UnauthorizedAccessException("Não és membro desta campanha.");
                if (others.Any(p => !members.Contains(p))) throw new InvalidOperationException(NotCampaignMembers);
                return;
            }
            foreach (var other in others)
                if (!await friendships.ExistsAcceptedAsync(actorId, other, ct)) throw new InvalidOperationException(NotFriends);
        }
    }
}
