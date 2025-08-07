using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IGameSessionRepository
    {
        Task<GameSession?> GetByIdAsync(Guid id);
        Task<IEnumerable<GameSession>> GetAllAsync();
        Task AddAsync(GameSession session);
        Task UpdateAsync(GameSession session);
        Task DeleteAsync(Guid id);
        Task SaveChangesAsync();
    }
}
