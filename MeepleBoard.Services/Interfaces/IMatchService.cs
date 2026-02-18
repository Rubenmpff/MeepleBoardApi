using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    public interface IMatchService
    {
        Task<IEnumerable<MatchDto>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default);

        Task<MatchDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<IEnumerable<MatchDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<IEnumerable<MatchDto>> GetByGameIdAsync(Guid gameId, CancellationToken cancellationToken = default);

        Task<IEnumerable<MatchDto>> GetRecentMatchesAsync(int count, CancellationToken cancellationToken = default);

        Task<LastMatchDto?> GetLastMatchForUserAsync(Guid userId);

        /// <summary>
        /// Criação com regras:
        /// - Quick match: inclui sempre o utilizador autenticado nos players
        /// - Session match: players têm de pertencer à sessão (e auth user tem de ser membro)
        /// </summary>
        Task<MatchDto> CreateAsync(CreateMatchDto dto, Guid authenticatedUserId, CancellationToken cancellationToken = default);

        // Mantido (legacy). Podes remover depois quando migrares tudo.
        Task<MatchDto> AddAsync(MatchDto matchDto, CancellationToken cancellationToken = default);

        Task<int> UpdateAsync(MatchDto matchDto, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
