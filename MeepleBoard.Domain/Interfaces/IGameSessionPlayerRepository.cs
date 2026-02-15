using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    /// <summary>
    /// Repositório responsável pela persistência e consulta de jogadores dentro de uma sessão de jogo.
    /// </summary>
    public interface IGameSessionPlayerRepository
    {
        /// <summary>Obtém todos os jogadores associados a uma sessão específica.</summary>
        Task<IEnumerable<GameSessionPlayer>> GetBySessionIdAsync(Guid sessionId);

        /// <summary>Obtém um registro específico de jogador dentro da sessão.</summary>
        Task<GameSessionPlayer?> GetByIdAsync(Guid id);

        /// <summary>Obtém o vínculo de um jogador numa sessão através do SessionId e UserId.</summary>
        Task<GameSessionPlayer?> GetBySessionAndUserAsync(Guid sessionId, Guid userId);

        /// <summary>Adiciona um jogador à sessão.</summary>
        Task AddAsync(GameSessionPlayer player);

        /// <summary>Remove um jogador de uma sessão pelo seu ID.</summary>
        Task RemoveAsync(Guid id);

        /// <summary>Salva as alterações no banco de dados.</summary>
        Task SaveChangesAsync();
    }
}
