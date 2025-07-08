using MeepleBoard.Domain.Entities;
using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    public interface ITokenService
    {
        /// <summary>
        /// Gera um novo Access Token e um Refresh Token para o usuário.
        /// </summary>
        Task<AuthenticationResultDto> GenerateTokensAsync(User user, string deviceInfo);

        /// <summary>
        /// Valida um Refresh Token e gera um novo Access Token.
        /// </summary>
        Task<AuthenticationResultDto> RefreshTokenAsync(string refreshToken);

        /// <summary>
        /// Revoga um Refresh Token específico no banco de dados.
        /// </summary>
        Task<bool> RevokeTokenAsync(string refreshToken);

        /// <summary>
        /// Revoga todos os Refresh Tokens de um usuário (Logout Global).
        /// </summary>
        Task<bool> RevokeAllTokensForUserAsync(Guid userId);
    }
}