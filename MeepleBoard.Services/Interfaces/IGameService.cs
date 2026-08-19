using MeepleBoard.Domain.Entities;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Mapping.Dtos;

public interface IGameService
{
    // ?? Leitura / Pesquisa
    Task<PagedResponse<GameDto>> GetAllAsync(int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>Jogos ordenados pela nota interna do MeepleBoard (m?dia das avalia??es dos jogadores).</summary>
    Task<PagedResponse<GameDto>> GetRankingsAsync(int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>Jogos ordenados por "meepleboard" (nota interna) ou "bgg" (nota m?dia do BGG).</summary>
    Task<PagedResponse<GameDto>> GetRankingsAsync(int pageIndex, int pageSize, string source, CancellationToken ct = default);

    /// <summary>Ranking pessoal: só os jogos que o utilizador avaliou, pela média das suas próprias notas.</summary>
    Task<PagedResponse<GameDto>> GetPersonalRankingsAsync(Guid userId, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Recalcula o MeepleBoardScore de TODOS os jogos a partir das avalia??es
    /// j? existentes no di?rio. S? ? preciso correr isto uma vez (backfill),
    /// depois disso o score mant?m-se sincronizado sozinho a cada avalia??o nova.
    /// Devolve quantos jogos foram atualizados.
    /// </summary>
    Task<int> RecomputeAllMeepleBoardScoresAsync(CancellationToken ct = default);
    Task<GameDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<GameDto?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    //Task<List<GameDto>> SearchBaseGamesWithFallbackAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default);
    Task<List<GameSuggestionDto>> SearchBaseGameSuggestionsAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default);
    Task<List<GameSuggestionDto>> SearchSuggestionsAsync(string query, int offset = 0, int limit = 10, CancellationToken ct = default);
    Task<List<GameSuggestionDto>> SearchExpansionSuggestionsAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default);
    Task<List<GameSuggestionDto>> GetExpansionSuggestionsForBaseAsync(Guid baseGameId, CancellationToken cancellationToken = default);

    // ? NOVO ? Garante que o jogo existe na BD com dados m?nimos (sem Description).
    // Usado por MatchService e UserGameLibraryService em intera??es reais.
    // Pesquisas NUNCA chamam este m?todo.
    Task<Game> GetOrCreateMinimalGameAsync(int bggId, CancellationToken ct = default);

    // ?? Importa??es
    Task<GameDto?> GetOrImportByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<GameDto?> ImportByBggIdAsync(int bggId, CancellationToken cancellationToken = default);
    Task<bool> UpdateFromBggAsync(GameDto game, CancellationToken cancellationToken = default);

    // ?? Exist?ncia
    Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);

    // ?? Escrita
    Task<Guid> AddAsync(GameDto gameDto, CancellationToken cancellationToken = default);
    Task<int> UpdateAsync(GameDto gameDto, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> ApproveGameAsync(Guid gameId, CancellationToken cancellationToken = default);

    // ?? Estat?sticas
    Task<IReadOnlyList<GameDto>> GetRecentlyPlayedAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameDto>> GetMostSearchedAsync(int limit, CancellationToken cancellationToken = default);

    // ?? Modera??o
    Task<IReadOnlyList<GameDto>> GetPendingApprovalAsync(CancellationToken cancellationToken = default);
}