using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    /// <summary>
    /// Repositório responsável por operações relacionadas a sessões de jogo.
    /// </summary>
    public interface IGameSessionRepository
    {
        Task<GameSession?> GetByIdAsync(Guid id, bool includeRelations = false);
        Task<IEnumerable<GameSession>> GetAllAsync(bool includeRelations = false);
        Task AddAsync(GameSession session);
        Task UpdateAsync(GameSession session);
        Task DeleteAsync(Guid id);
        Task SaveChangesAsync();
    }
}
