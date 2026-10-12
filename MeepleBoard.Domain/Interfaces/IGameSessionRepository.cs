using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    /// <summary>
    /// Repositório responsável por operações relacionadas a sessões de jogo.
    /// </summary>
    public interface IGameSessionRepository
    {
        // Lista leve para o ecrã de sessões (sem Includes pesados)
        Task<IReadOnlyList<GameSession>> GetListAsync(CancellationToken ct = default);

        // Detalhe completo para /session/{id}
        Task<GameSession?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);

        // Para updates (tracking)
        Task<GameSession?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);

        // Update with players loaded: the start and end times depend on who accepted.
        Task<GameSession?> GetByIdWithPlayersForUpdateAsync(Guid id, CancellationToken ct = default);

        // Not closed nor cancelled, with players, tracked: candidates for automatic closing.
        Task<IReadOnlyList<GameSession>> GetOpenForUpdateAsync(CancellationToken ct = default);

        Task AddAsync(GameSession session, CancellationToken ct = default);
        Task UpdateAsync(GameSession session, CancellationToken ct = default);
        Task DeleteAsync(Guid id, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);
    }
}
