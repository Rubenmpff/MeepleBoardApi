using MeepleBoard.Domain.Enums;
using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    public interface IUserGameLibraryService
    {
        Task<IEnumerable<UserGameLibraryDto>> GetUserLibraryAsync(Guid requesterId, Guid targetUserId, CancellationToken cancellationToken = default);

        /// <summary>Another person's played games answer as not found when there is a block either way.</summary>
        Task<IEnumerable<PlayedGameDto>> GetPlayedGamesAsync(Guid requesterId, Guid userId, CancellationToken cancellationToken = default);

        Task AddGameToLibraryAsync(Guid userId, Guid gameId, string gameName, GameLibraryStatus status, decimal? pricePaid, CancellationToken cancellationToken = default);

        Task UpdateGameInLibraryAsync(Guid userId, Guid gameId, GameLibraryStatus status, decimal? pricePaid, CancellationToken cancellationToken = default);

        Task RemoveGameFromLibraryAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default);

        Task<GameFriendOwnersDto> GetFriendOwnersAsync(Guid requesterId, Guid gameId, CancellationToken cancellationToken = default);

        Task<decimal> GetTotalAmountSpentByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}