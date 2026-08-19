using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    /// <summary>
    /// Repositório responsável por operações de campanhas.
    /// Segue o mesmo padrão do IGameSessionRepository.
    /// </summary>
    public interface ICampaignRepository
    {
        /// <summary>Lista leve — campanhas do utilizador (sem includes pesados).</summary>
        Task<IReadOnlyList<Campaign>> GetListByUserAsync(Guid userId, CancellationToken ct = default);

        /// <summary>Lista campanhas do utilizador para um jogo específico.</summary>
        Task<IReadOnlyList<Campaign>> GetListByGameAsync(Guid gameId, Guid userId, CancellationToken ct = default);

        /// <summary>Detalhe completo com membros, partidas e entradas de diário.</summary>
        Task<Campaign?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);

        /// <summary>Para updates (com tracking).</summary>
        Task<Campaign?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);

        Task AddAsync(Campaign campaign, CancellationToken ct = default);
        Task UpdateAsync(Campaign campaign, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);

        // ── Membros ────────────────────────────────────────────────────────────

        Task<CampaignMember?> GetMemberAsync(Guid campaignId, Guid userId, CancellationToken ct = default);
        Task AddMemberAsync(CampaignMember member, CancellationToken ct = default);
        Task<IReadOnlyList<CampaignMember>> GetMembersAsync(Guid campaignId, CancellationToken ct = default);

        // ── Partidas ───────────────────────────────────────────────────────────

        Task<CampaignMatch?> GetCampaignMatchAsync(Guid campaignId, Guid matchId, CancellationToken ct = default);
        Task AddCampaignMatchAsync(CampaignMatch campaignMatch, CancellationToken ct = default);
        Task RemoveCampaignMatchAsync(Guid campaignId, Guid matchId, CancellationToken ct = default);

        // ── Diário ─────────────────────────────────────────────────────────────

        Task<MatchJournalEntry?> GetJournalEntryAsync(Guid matchId, Guid userId, CancellationToken ct = default);
        Task<IReadOnlyList<MatchJournalEntry>> GetJournalEntriesForMatchAsync(Guid matchId, CancellationToken ct = default);
        Task AddJournalEntryAsync(MatchJournalEntry entry, CancellationToken ct = default);
        Task UpdateJournalEntryAsync(MatchJournalEntry entry, CancellationToken ct = default);
    }
}