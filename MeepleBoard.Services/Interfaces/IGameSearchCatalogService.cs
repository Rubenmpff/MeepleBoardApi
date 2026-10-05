using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    /// <summary>
    /// Serviço responsável pela pesquisa no catálogo leve de jogos.
    ///
    /// O catálogo existe apenas para tornar o autocomplete e as pesquisas
    /// rápidas, sem transformar resultados de pesquisa em entidades Game reais.
    /// </summary>
    public interface IGameSearchCatalogService
    {
        /// <summary>
        /// Pesquisa jogos no catálogo leve.
        ///
        /// Os filtros e a ordenação são aplicados antes da paginação para que
        /// todas as páginas representem corretamente o conjunto completo de
        /// resultados.
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
        /// null = jogos base + expansões;
        /// false = apenas jogos base;
        /// true = apenas expansões.
        /// </param>
        /// <param name="playerCount">
        /// Número de jogadores pretendido.
        ///
        /// Valores 1-4 significam que o jogo tem de suportar exatamente esse
        /// número de jogadores.
        ///
        /// O valor 5 representa o filtro "5+" utilizado pelo frontend:
        /// o jogo deve suportar pelo menos cinco jogadores.
        ///
        /// Jogos sem informação de MinPlayers/MaxPlayers não devem ser
        /// considerados correspondências quando este filtro está ativo.
        /// </param>
        /// <param name="minBggRating">
        /// Nota média mínima do BGG.
        /// Jogos sem rating não devem passar este filtro.
        /// </param>
        /// <param name="sort">
        /// Ordenação pedida:
        /// relevance, most_known, bgg_rating, year_desc ou name_asc.
        /// </param>
        /// <param name="cancellationToken">
        /// Token utilizado para cancelar a pesquisa quando necessário.
        /// </param>
        Task<List<GameSuggestionDto>> SearchAsync(
            string query,
            int offset = 0,
            int limit = 10,
            bool? isExpansion = null,
            int? playerCount = null,
            double? minBggRating = null,
            string sort = "relevance",
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Enriquece detalhes de registos que já existem no GameSearchCatalog
        /// através do endpoint /thing do BoardGameGeek.
        ///
        /// Esta operação:
        /// - atualiza apenas GameSearchCatalog;
        /// - nunca cria entidades Game reais;
        /// - pode preencher capa, jogadores, cooperação, campanha,
        ///   rating e ranking.
        ///
        /// A pesquisa normal não deve depender deste método para responder.
        /// O enriquecimento é uma operação separada.
        /// </summary>
        /// <param name="bggIds">
        /// BGG IDs dos registos do catálogo a enriquecer.
        /// IDs inválidos e duplicados devem ser ignorados.
        /// </param>
        /// <param name="cancellationToken">
        /// Token utilizado para cancelar a operação quando necessário.
        /// </param>
        /// <returns>
        /// Número de registos do catálogo efetivamente enriquecidos.
        /// </returns>
        Task<int> EnrichDetailsAsync(
            IReadOnlyCollection<int> bggIds,
            CancellationToken cancellationToken = default);
    }
}
