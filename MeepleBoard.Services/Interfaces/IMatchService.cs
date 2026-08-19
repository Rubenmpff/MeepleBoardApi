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
        /// Historial de partidas de um utilizador para um jogo específico.
        /// </summary>
        Task<IEnumerable<MatchDto>> GetMatchHistoryByGameAsync(Guid gameId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Avaliação pessoal média do utilizador para um jogo (0–10).
        /// Só conta partidas oficiais (IsOfficialMode = true).
        /// </summary>
        Task<double?> GetUserRatingForGameAsync(Guid gameId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Partidas com diário aberto onde o utilizador ainda não avaliou.
        /// Usado no PendingJournalScreen.
        /// </summary>
        Task<IEnumerable<MatchDto>> GetPendingJournalMatchesAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fecha o diário de uma partida manualmente.
        /// Só o criador da partida pode fechar.
        /// Após fechar, jogadores pendentes ainda podem avaliar.
        /// </summary>
        Task CloseJournalAsync(Guid matchId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Criação com regras:
        /// - Quick match: inclui sempre o utilizador autenticado nos players
        /// - Session match: players têm de pertencer à sessão
        /// </summary>
        Task<MatchDto> CreateAsync(CreateMatchDto dto, Guid authenticatedUserId, CancellationToken cancellationToken = default);

        Task<MatchDto> AddAsync(MatchDto matchDto, CancellationToken cancellationToken = default);
        Task<int> UpdateAsync(MatchDto matchDto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}