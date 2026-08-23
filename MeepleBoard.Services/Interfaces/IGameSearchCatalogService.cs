using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    /// <summary>
    /// Serviço responsável pela pesquisa no catálogo leve de jogos.
    ///
    /// O catálogo existe apenas para tornar o autocomplete e as pesquisas
    /// rápidas, sem transformar todos os resultados externos em entidades
    /// Game reais da aplicação.
    /// </summary>
    public interface IGameSearchCatalogService
    {
        /// <summary>
        /// Pesquisa jogos no catálogo e devolve resultados no mesmo DTO
        /// utilizado atualmente pelo frontend.
        /// </summary>
        /// <param name="query">
        /// Texto introduzido pelo utilizador.
        /// Exemplo: "ro", "root", "robinson".
        /// </param>
        /// <param name="offset">
        /// Número de resultados a ignorar.
        /// </param>
        /// <param name="limit">
        /// Número máximo de resultados a devolver.
        /// </param>
        /// <param name="isExpansion">
        /// null = jogos e expansões;
        /// false = apenas jogos base;
        /// true = apenas expansões.
        /// </param>
        /// <param name="cancellationToken">
        /// Token utilizado para cancelar a pesquisa quando necessário.
        /// </param>
        Task<List<GameSuggestionDto>> SearchAsync(
            string query,
            int offset = 0,
            int limit = 10,
            bool? isExpansion = null,
            CancellationToken cancellationToken = default);
    }
}