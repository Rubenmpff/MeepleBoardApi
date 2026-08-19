using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class CampaignRepository : ICampaignRepository
    {
        private readonly MeepleBoardDbContext _context;

        public CampaignRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        // ── Campanhas ─────────────────────────────────────────────────────────

        /// <summary>
        /// Lista LEVE — campanhas onde o utilizador é membro aceite ou criador.
        /// Sem carregar partidas/entradas de diário em memória.
        /// </summary>
        public async Task<IReadOnlyList<Campaign>> GetListByUserAsync(
            Guid userId, CancellationToken ct = default)
        {
            return await _context.Campaigns
                .AsNoTracking()
                .Include(c => c.Game)
                .Include(c => c.Creator)
                .Include(c => c.Members)
                .Where(c =>
                    c.CreatorId == userId ||
                    c.Members.Any(m =>
                        m.UserId == userId &&
                        m.Status == CampaignMemberStatus.Accepted))
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync(ct);
        }

        /// <summary>Lista campanhas do utilizador para um jogo específico.</summary>
        public async Task<IReadOnlyList<Campaign>> GetListByGameAsync(
            Guid gameId, Guid userId, CancellationToken ct = default)
        {
            return await _context.Campaigns
                .AsNoTracking()
                .Include(c => c.Game)
                .Include(c => c.Creator)
                .Include(c => c.Members)
                .Where(c =>
                    c.GameId == gameId &&
                    (c.CreatorId == userId ||
                     c.Members.Any(m =>
                         m.UserId == userId &&
                         m.Status == CampaignMemberStatus.Accepted)))
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync(ct);
        }

        /// <summary>Detalhe COMPLETO com membros, partidas e entradas de diário.</summary>
        public async Task<Campaign?> GetByIdWithDetailsAsync(
            Guid id, CancellationToken ct = default)
        {
            return await _context.Campaigns
                .AsNoTracking()
                .Include(c => c.Game)
                .Include(c => c.Creator)
                .Include(c => c.Members)
                    .ThenInclude(m => m.User)
                .Include(c => c.CampaignMatches)
                    .ThenInclude(cm => cm.Match)
                        .ThenInclude(m => m!.Game)
                .Include(c => c.CampaignMatches)
                    .ThenInclude(cm => cm.Match)
                        .ThenInclude(m => m!.MatchPlayers)
                            .ThenInclude(mp => mp.User)
                .Include(c => c.CampaignMatches)
                    .ThenInclude(cm => cm.Match)
                        .ThenInclude(m => m!.JournalEntries)
                .FirstOrDefaultAsync(c => c.Id == id, ct);
        }

        /// <summary>Para updates (com tracking ligado).</summary>
        public async Task<Campaign?> GetByIdForUpdateAsync(
            Guid id, CancellationToken ct = default)
        {
            return await _context.Campaigns
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.Id == id, ct);
        }

        public async Task AddAsync(Campaign campaign, CancellationToken ct = default)
        {
            if (campaign == null) throw new ArgumentNullException(nameof(campaign));
            await _context.Campaigns.AddAsync(campaign, ct);
        }

        public Task UpdateAsync(Campaign campaign, CancellationToken ct = default)
        {
            if (campaign == null) throw new ArgumentNullException(nameof(campaign));
            _context.Campaigns.Update(campaign);
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var campaign = await _context.Campaigns.FindAsync(new object[] { id }, ct);
            if (campaign != null)
                _context.Campaigns.Remove(campaign);
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
            => _context.SaveChangesAsync(ct);

        // ── Membros ───────────────────────────────────────────────────────────

        public async Task<CampaignMember?> GetMemberAsync(
            Guid campaignId, Guid userId, CancellationToken ct = default)
        {
            return await _context.CampaignMembers
                .FirstOrDefaultAsync(m =>
                    m.CampaignId == campaignId && m.UserId == userId, ct);
        }

        public async Task AddMemberAsync(
            CampaignMember member, CancellationToken ct = default)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            await _context.CampaignMembers.AddAsync(member, ct);
        }

        public async Task<IReadOnlyList<CampaignMember>> GetMembersAsync(
            Guid campaignId, CancellationToken ct = default)
        {
            return await _context.CampaignMembers
                .AsNoTracking()
                .Include(m => m.User)
                .Where(m => m.CampaignId == campaignId)
                .ToListAsync(ct);
        }

        // ── Partidas ──────────────────────────────────────────────────────────

        public async Task<CampaignMatch?> GetCampaignMatchAsync(
            Guid campaignId, Guid matchId, CancellationToken ct = default)
        {
            return await _context.CampaignMatches
                .FirstOrDefaultAsync(cm =>
                    cm.CampaignId == campaignId && cm.MatchId == matchId, ct);
        }

        public async Task AddCampaignMatchAsync(
            CampaignMatch campaignMatch, CancellationToken ct = default)
        {
            if (campaignMatch == null) throw new ArgumentNullException(nameof(campaignMatch));
            await _context.CampaignMatches.AddAsync(campaignMatch, ct);
        }

        public async Task RemoveCampaignMatchAsync(
            Guid campaignId, Guid matchId, CancellationToken ct = default)
        {
            var cm = await _context.CampaignMatches
                .FirstOrDefaultAsync(x =>
                    x.CampaignId == campaignId && x.MatchId == matchId, ct);
            if (cm != null)
                _context.CampaignMatches.Remove(cm);
        }

        // ── Diário ────────────────────────────────────────────────────────────

        public async Task<MatchJournalEntry?> GetJournalEntryAsync(
            Guid matchId, Guid userId, CancellationToken ct = default)
        {
            return await _context.MatchJournalEntries
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.MatchId == matchId && e.UserId == userId, ct);
        }

        public async Task<IReadOnlyList<MatchJournalEntry>> GetJournalEntriesForMatchAsync(
            Guid matchId, CancellationToken ct = default)
        {
            return await _context.MatchJournalEntries
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e => e.MatchId == matchId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task AddJournalEntryAsync(
            MatchJournalEntry entry, CancellationToken ct = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            await _context.MatchJournalEntries.AddAsync(entry, ct);
        }

        public Task UpdateJournalEntryAsync(
            MatchJournalEntry entry, CancellationToken ct = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            _context.MatchJournalEntries.Update(entry);
            return Task.CompletedTask;
        }
    }
}