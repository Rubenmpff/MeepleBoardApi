using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Interfaces
{
    public interface IGameDurationService
    {
        /// <summary>Duration statistics of a game for this viewer: their own matches and the community aggregate.</summary>
        Task<GameDurationsDto> GetAsync(Guid gameId, Guid viewerId, CancellationToken ct = default);
    }
}
