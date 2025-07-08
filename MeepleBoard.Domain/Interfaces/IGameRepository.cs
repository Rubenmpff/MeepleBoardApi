using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IGameRepository
    {
        // 🔹 CRUD básico
        Task<Game?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Game>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default);
        Task AddAsync(Game game, CancellationToken cancellationToken = default);
        Task UpdateAsync(Game game, CancellationToken cancellationToken = default);
        Task DeleteAsync(Game game, CancellationToken cancellationToken = default);
        Task<int> CommitAsync(CancellationToken cancellationToken = default);

        // 🔹 Consultas específicas
        Task<Game?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<Game?> GetGameByBggIdAsync(int bggId, CancellationToken cancellationToken = default);
        Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<bool> ExistsByBggIdAsync(int bggId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Game>> GetApprovedAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Game>> GetPendingApprovalAsync(CancellationToken cancellationToken = default);

        Task<List<Game>> SearchByNameAsync(string query, int offset, int limit, CancellationToken cancellationToken);
        Task<List<Game>> SearchBaseGamesByNameAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default);
        Task<List<Game>> SearchExpansionsByNameAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default);

        Task<List<Game>> GetExpansionsForBaseGameAsync(Guid baseGameId, CancellationToken cancellationToken);
        Task<List<Game>> GetExpansionsWithBaseGameBggIdAsync(int baseGameBggId, CancellationToken cancellationToken = default);

        // 🔥 Utilizados no Job de sincronização com o BGG
        Task<IReadOnlyList<Game>> GetRecentlyPlayedAsync(int limit, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Game>> GetMostSearchedAsync(int limit, CancellationToken cancellationToken = default);
    }
}
