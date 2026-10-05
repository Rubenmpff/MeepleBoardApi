using MeepleBoard.Domain.Entities;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Mapping.Dtos;

public interface IGameService
{
    // Leitura / Pesquisa
    Task<PagedResponse<GameDto>> GetAllAsync(
        int pageIndex,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Jogos ordenados pela nota interna do MeepleBoard
    /// (média das avaliações dos jogadores).
    /// </summary>
    Task<PagedResponse<GameDto>> GetRankingsAsync(
        int pageIndex,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Jogos ordenados por "meepleboard" (nota interna)
    /// ou "bgg" (nota média do BGG).
    /// </summary>
    Task<PagedResponse<GameDto>> GetRankingsAsync(
        int pageIndex,
        int pageSize,
        string source,
        CancellationToken ct = default);

    /// <summary>
    /// Ranking pessoal: só os jogos que o utilizador avaliou,
    /// pela média das suas próprias notas.
    /// </summary>
    Task<PagedResponse<GameDto>> GetPersonalRankingsAsync(
        Guid userId,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Recalcula o MeepleBoardScore de todos os jogos a partir das avaliações
    /// já existentes no diário.
    /// </summary>
    Task<int> RecomputeAllMeepleBoardScoresAsync(
        CancellationToken ct = default);

    Task<GameDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<GameDto?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<List<GameSuggestionDto>> SearchBaseGameSuggestionsAsync(
        string query,
        int offset = 0,
        int limit = 10,
        int? playerCount = null,
        double? minBggRating = null,
        string sort = "relevance",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pesquisa sugestões combinando jogos locais e GameSearchCatalog.
    ///
    /// A filtragem e ordenação devem acontecer antes da paginação para que
    /// infinite scroll, filtros e ordenação sejam consistentes em todas as páginas.
    ///
    /// isExpansion:
    /// - null  = jogos base + expansões;
    /// - false = apenas jogos base;
    /// - true  = apenas expansões.
    ///
    /// sort:
    /// - relevance
    /// - most_known
    /// - bgg_rating
    /// - year_desc
    /// - name_asc
    /// </summary>
    Task<List<GameSuggestionDto>> SearchSuggestionsAsync(
        string query,
        int offset = 0,
        int limit = 10,
        bool? isExpansion = null,
        int? playerCount = null,
        double? minBggRating = null,
        string sort = "relevance",
        CancellationToken ct = default);

    Task<List<GameSuggestionDto>> SearchExpansionSuggestionsAsync(
        string query,
        int offset = 0,
        int limit = 10,
        int? playerCount = null,
        double? minBggRating = null,
        string sort = "relevance",
        CancellationToken cancellationToken = default);

    Task<List<GameSuggestionDto>> GetExpansionSuggestionsForBaseAsync(
        Guid baseGameId,
        CancellationToken cancellationToken = default);

    // Garante que o jogo existe na BD com dados mínimos (sem Description).
    // Usado por MatchService e UserGameLibraryService em interações reais.
    // Pesquisas NUNCA chamam este método.
    Task<Game> GetOrCreateMinimalGameAsync(
        int bggId,
        CancellationToken ct = default);

    // Importações
    Task<GameDto?> GetOrImportByNameAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<GameDto?> ImportByBggIdAsync(
        int bggId,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateFromBggAsync(
        GameDto game,
        CancellationToken cancellationToken = default);

    // Existência
    Task<bool> ExistsByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(
        string name,
        CancellationToken cancellationToken = default);

    // Escrita
    Task<Guid> AddAsync(
        GameDto gameDto,
        CancellationToken cancellationToken = default);

    Task<int> UpdateAsync(
        GameDto gameDto,
        CancellationToken cancellationToken = default);

    Task<int> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<int> ApproveGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default);

    // Estatísticas
    Task<IReadOnlyList<GameDto>> GetRecentlyPlayedAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameDto>> GetMostSearchedAsync(
        int limit,
        CancellationToken cancellationToken = default);

    // Moderação
    Task<IReadOnlyList<GameDto>> GetPendingApprovalAsync(
        CancellationToken cancellationToken = default);
}