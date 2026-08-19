using MeepleBoard.Domain.Enums;
using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    public interface IUserGameLibraryService
    {
        Task<IEnumerable<UserGameLibraryDto>> GetUserLibraryAsync(Guid requesterId, Guid targetUserId, CancellationToken cancellationToken = default);

        Task<IEnumerable<PlayedGameDto>> GetPlayedGamesAsync(Guid userId, CancellationToken cancellationToken = default);

        Task AddGameToLibraryAsync(Guid userId, Guid gameId, string gameName, GameLibraryStatus status, decimal? pricePaid, CancellationToken cancellationToken = default);

        Task UpdateGameInLibraryAsync(Guid userId, Guid gameId, GameLibraryStatus status, decimal? pricePaid, CancellationToken cancellationToken = default);

        Task RemoveGameFromLibraryAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default);

        Task<decimal> GetTotalAmountSpentByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}