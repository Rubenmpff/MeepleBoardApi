using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    public interface IBGGService
    {
        Task<GameDto?> GetGameByNameAsync(string gameName, CancellationToken cancellationToken = default);

        Task<GameDto?> GetGameByIdAsync(string gameId, CancellationToken cancellationToken = default);

        Task<List<GameDto>> GetHotGamesAsync(CancellationToken cancellationToken = default);

        Task<List<GameDto>> GetGamesByIdsAsync(List<string> ids, CancellationToken cancellationToken = default);

        Task<List<GameDto>> SearchGamesAsync(string gameName, CancellationToken cancellationToken = default);

        Task<List<GameSuggestionDto>> SearchGameSuggestionsAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default);
    }
}