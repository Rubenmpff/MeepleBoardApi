using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    public interface IMatchService
    {
        /// <summary>
        /// Obtém todas as partidas registradas com suporte a paginação.
        /// </summary>
        /// <param name="pageIndex">Índice da página (0 = primeira página).</param>
        /// <param name="pageSize">Quantidade de partidas por página.</param>
        Task<IEnumerable<MatchDto>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default);

        /// <summary>
        /// Obtém uma partida específica pelo ID.
        /// </summary>
        Task<MatchDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Obtém todas as partidas jogadas por um usuário específico.
        /// </summary>
        Task<IEnumerable<MatchDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Obtém todas as partidas de um jogo específico.
        /// </summary>
        Task<IEnumerable<MatchDto>> GetByGameIdAsync(Guid gameId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Obtém as partidas mais recentes.
        /// </summary>
        /// <param name="count">Número máximo de partidas a serem retornadas.</param>
        Task<IEnumerable<MatchDto>> GetRecentMatchesAsync(int count, CancellationToken cancellationToken = default);

        Task<LastMatchDto?> GetLastMatchForUserAsync(Guid userId);

        /// <summary>
        /// Cria uma nova partida e retorna os detalhes da partida criada.
        /// </summary>
        /// <param name="MatchDto">Objeto DTO contendo os dados da partida.</param>
        Task<MatchDto> AddAsync(MatchDto matchDto, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atualiza os detalhes de uma partida existente.
        /// </summary>
        /// <param name="matchDto">Objeto DTO contendo os dados da partida.</param>
        Task<int> UpdateAsync(MatchDto matchDto, CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove uma partida pelo ID e retorna `true` se for bem-sucedido.
        /// </summary>
        Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}