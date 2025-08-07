using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IGameSessionPlayerRepository
    {
        /// <summary>
        /// Obtém todos os jogadores de uma sessão específica.
        /// </summary>
        Task<IEnumerable<GameSessionPlayer>> GetBySessionIdAsync(Guid sessionId);

        /// <summary>
        /// Obtém um jogador pelo ID da relação GameSessionPlayer.
        /// </summary>
        Task<GameSessionPlayer?> GetByIdAsync(Guid id);

        /// <summary>
        /// Obtém um jogador específico numa sessão através do SessionId e UserId.
        /// </summary>
        Task<GameSessionPlayer?> GetBySessionAndUserAsync(Guid sessionId, Guid userId);

        /// <summary>
        /// Adiciona um jogador a uma sessão.
        /// </summary>
        Task AddAsync(GameSessionPlayer player);

        /// <summary>
        /// Remove um jogador pelo seu ID.
        /// </summary>
        Task RemoveAsync(Guid id);

        /// <summary>
        /// Confirma as alterações no banco de dados.
        /// </summary>
        Task SaveChangesAsync();
    }
}
