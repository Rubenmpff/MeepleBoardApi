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
            int? playerCount = null,
            double? minBggRating = null,
            string sort = "relevance",
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Obtém candidatos prioritários para enriquecimento através do BGG /thing.
        ///
        /// Apenas devolve registos já existentes no GameSearchCatalog que ainda
        /// não possuem detalhes sincronizados (DetailsSyncedAt == null).
        ///
        /// A prioridade deve favorecer jogos mais conhecidos/relevantes para que
        /// as capas e restantes detalhes apareçam primeiro nos resultados que têm
        /// maior probabilidade de ser pesquisados pelos utilizadores.
        ///
        /// Esta operação é apenas de leitura e nunca cria entidades Game reais.
        /// </summary>
        /// <param name="limit">
        /// Número máximo de candidatos a devolver.
        /// </param>
        /// <param name="cancellationToken">
        /// Token utilizado para cancelar a operação quando necessário.
        /// </param>
        Task<List<GameSearchCatalog>> GetCandidatesForEnrichmentAsync(
            int limit,
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
        /// Reconstrói em bloco o índice auxiliar de tokens usado pelo
        /// autocomplete para pesquisas por início de palavra.
        ///
        /// A implementação deve ser set-based/bulk e não deve inserir
        /// tokens individualmente através do Entity Framework.
        ///
        /// O índice é reconstruível a partir de GameSearchCatalog e,
        /// por isso, pode ser apagado e recriado sem afetar dados de
        /// domínio ou dados pertencentes aos utilizadores.
        /// </summary>
        Task<int> RebuildSearchTokensAsync(
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
