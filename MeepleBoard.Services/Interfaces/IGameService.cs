using MeepleBoard.Services.DTOs;

public interface IGameService
{
    // ?? Leitura / Pesquisa
    Task<IReadOnlyList<GameDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<GameDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<GameDto?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<List<GameDto>> SearchBaseGamesWithFallbackAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default);

    // ?? Importações
    Task<GameDto?> GetOrImportByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<GameDto?> ImportByBggIdAsync(int bggId, CancellationToken cancellationToken = default);
    Task<bool> UpdateFromBggAsync(GameDto game, CancellationToken cancellationToken = default);

    // ? Existência
    Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);

    // ?? Escrita
    Task<Guid> AddAsync(GameDto gameDto, CancellationToken cancellationToken = default);
    Task<int> UpdateAsync(GameDto gameDto, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> ApproveGameAsync(Guid gameId, CancellationToken cancellationToken = default);

    // ?? Estatísticas
    Task<IReadOnlyList<GameDto>> GetRecentlyPlayedAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameDto>> GetMostSearchedAsync(int limit, CancellationToken cancellationToken = default);

    // ?? Moderação
    Task<IReadOnlyList<GameDto>> GetPendingApprovalAsync(CancellationToken cancellationToken = default);
}
