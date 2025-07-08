using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenAsync(string token);

        Task AddAsync(RefreshToken token);

        Task InvalidateTokenAsync(string token);

        Task InvalidateAllTokensForUserAsync(Guid userId);
    }
}