using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IGameSearchCatalogRepository
    {
        Task<GameSearchCatalog?> GetByBggIdAsync(
            int bggId,
            CancellationToken cancellationToken = default);

        Task<Dictionary<int, GameSearchCatalog>> GetByBggIdsAsync(
            IReadOnlyCollection<int> bggIds,
            CancellationToken cancellationToken = default);

        Task<List<GameSearchCatalog>> SearchAsync(
            string normalizedQuery,
            int offset = 0,
            int limit = 10,
            bool? isExpansion = null,
            CancellationToken cancellationToken = default);

        Task AddAsync(
            GameSearchCatalog game,
            CancellationToken cancellationToken = default);

        Task AddRangeAsync(
            IEnumerable<GameSearchCatalog> games,
            CancellationToken cancellationToken = default);

        Task UpdateAsync(
            GameSearchCatalog game,
            CancellationToken cancellationToken = default);

        Task<int> CommitAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Insere ou atualiza em bloco os registos do catálogo de pesquisa.
        ///
        /// Este método é destinado à importação massiva do catálogo BGG
        /// e deve ser implementado pela infraestrutura utilizando uma
        /// estratégia eficiente para SQL Server.
        /// </summary>
        Task<int> BulkUpsertAsync(
            IReadOnlyCollection<GameSearchCatalog> games,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove as entidades atualmente acompanhadas pelo Entity Framework.
        ///
        /// Utilizado após batches grandes de importação para impedir
        /// crescimento contínuo do ChangeTracker.
        /// </summary>
        void ClearTracking();
    }
}