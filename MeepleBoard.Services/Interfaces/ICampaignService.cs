using MeepleBoard.Services.DTOs.Campaign;
using MeepleBoard.Services.DTOs.MatchJournal;

namespace MeepleBoard.Services.Interfaces
{
    public interface ICampaignService
    {
        // ── Campanhas ──────────────────────────────────────────────────────────
        Task<IEnumerable<CampaignDto>> GetMineAsync(Guid userId, CancellationToken ct = default);
        Task<IEnumerable<CampaignDto>> GetByGameAsync(Guid gameId, Guid userId, CancellationToken ct = default);
        Task<CampaignDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
        Task<CampaignDto> CreateAsync(CreateCampaignDto dto, Guid userId, CancellationToken ct = default);
        Task UpdateAsync(Guid id, UpdateCampaignDto dto, Guid userId, CancellationToken ct = default);
        Task CompleteAsync(Guid id, Guid userId, CancellationToken ct = default);
        Task AbandonAsync(Guid id, Guid userId, CancellationToken ct = default);
        Task DeleteAsync(Guid id, Guid userId, CancellationToken ct = default);

        // ── Membros ────────────────────────────────────────────────────────────
        Task InviteMemberAsync(Guid campaignId, Guid targetUserId, Guid requesterId, CancellationToken ct = default);
        Task RespondInviteAsync(Guid campaignId, Guid userId, bool accept, CancellationToken ct = default);
        Task RemoveMemberAsync(Guid campaignId, Guid targetUserId, Guid requesterId, CancellationToken ct = default);
        Task LeaveCampaignAsync(Guid campaignId, Guid userId, CancellationToken ct = default);

        // ── Partidas ───────────────────────────────────────────────────────────
        Task AddMatchAsync(Guid campaignId, AddMatchToCampaignDto dto, Guid userId, CancellationToken ct = default);
        Task RemoveMatchAsync(Guid campaignId, Guid matchId, Guid userId, CancellationToken ct = default);

        // ── Diário ─────────────────────────────────────────────────────────────
        Task<JournalEntryDto> UpsertJournalEntryAsync(Guid matchId, UpsertJournalEntryDto dto, Guid userId, CancellationToken ct = default);
        Task<IEnumerable<JournalEntryDto>> GetJournalEntriesAsync(Guid matchId, CancellationToken ct = default);

        /// <summary>Adiciona uma foto à entrada de diário do utilizador para esta partida (cria a entrada se ainda não existir).</summary>
        Task<JournalEntryDto> AddJournalPhotoAsync(Guid matchId, Guid userId, Stream fileStream, string fileName, CancellationToken ct = default);

        /// <summary>Remove uma foto da entrada de diário do utilizador para esta partida.</summary>
        Task<JournalEntryDto> RemoveJournalPhotoAsync(Guid matchId, Guid userId, string photoUrl, CancellationToken ct = default);
    }
}