using MeepleBoard.Application.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    public interface IGameSessionService
    {
        Task<IEnumerable<GameSessionDto>> GetAllAsync(bool includeRelations = false);
        Task<GameSessionDto?> GetByIdAsync(Guid id, bool includeRelations = true);
        Task<IEnumerable<GameSessionDto>> GetMineAsync(Guid userId);
        Task<GameSessionDto> CreateAsync(CreateGameSessionDto dto, Guid organizerId);

        /// <summary>Organizer convida um jogador (entra Pending).</summary>
        Task InvitePlayerAsync(Guid sessionId, Guid organizerId, Guid targetUserId);

        /// <summary>Convidado aceita ou recusa o convite.</summary>
        Task RespondInviteAsync(Guid sessionId, Guid userId, bool accept);

        /// <summary>Encerra a sessão com sucesso (Closed). Só organizer.</summary>
        Task CloseSessionAsync(Guid sessionId, Guid organizerId);

        /// <summary>
        /// Cancela a sessão manualmente (Cancelled). Só organizer.
        /// Só possível enquanto Upcoming.
        /// A sessão é apagada da BD após cancelamento.
        /// </summary>
        Task CancelSessionAsync(Guid sessionId, Guid organizerId);
    }
}