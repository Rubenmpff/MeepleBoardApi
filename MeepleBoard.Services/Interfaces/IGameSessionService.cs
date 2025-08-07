using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Services.Interfaces
{
    public interface IGameSessionService
    {
        Task<IEnumerable<GameSession>> GetAllAsync();
        Task<GameSession?> GetByIdAsync(Guid id);
        Task<GameSession> CreateAsync(string name, Guid organizerId, string? location = null);
        Task AddPlayerAsync(Guid sessionId, Guid userId, bool isOrganizer = false);
        Task RemovePlayerAsync(Guid sessionId, Guid userId);
        Task CloseSessionAsync(Guid sessionId);
    }
}
