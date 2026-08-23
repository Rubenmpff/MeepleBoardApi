namespace MeepleBoard.Services.ExternalServices.Interfaces
{
    /// <summary>
    /// Serviço responsável por importar e sincronizar
    /// o catálogo leve de jogos a partir do ficheiro
    /// oficial de rankings do BoardGameGeek.
    ///
    /// Esta operação atualiza apenas GameSearchCatalog.
    /// Não cria entidades Game reais na aplicação.
    /// </summary>
    public interface IBggGameCatalogImportService
    {
        /// <summary>
        /// Importa ou atualiza o catálogo a partir
        /// da fonte oficial configurada do BGG.
        /// </summary>
        /// <param name="cancellationToken">
        /// Token opcional de cancelamento.
        /// </param>
        /// <returns>
        /// Número de registos processados.
        /// </returns>
        Task<int> ImportAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Importa ou atualiza o catálogo a partir
        /// de um ficheiro CSV ou ZIP local.
        ///
        /// Útil para bootstrap inicial e para importações
        /// manuais enquanto o download automático do BGG
        /// não estiver disponível.
        /// </summary>
        /// <param name="filePath">
        /// Caminho completo para o ficheiro CSV ou ZIP.
        /// </param>
        /// <param name="cancellationToken">
        /// Token opcional de cancelamento.
        /// </param>
        /// <returns>
        /// Número de registos processados.
        /// </returns>
        Task<int> ImportFromFileAsync(
            string filePath,
            CancellationToken cancellationToken = default);
    }
}