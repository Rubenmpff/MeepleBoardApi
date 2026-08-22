using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using MeepleBoard.Services.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MeepleBoard.Services.Implementations
{
    public class TokenService : ITokenService
    {
        private readonly JwtSettings _jwtSettings;
        private readonly UserManager<User> _userManager;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly ILogger<TokenService> _logger;

        public TokenService(
            IOptions<JwtSettings> jwtSettings,
            UserManager<User> userManager,
            IRefreshTokenRepository refreshTokenRepository,
            ILogger<TokenService> logger)
        {
            _jwtSettings =
                jwtSettings?.Value
                ?? throw new ArgumentNullException(
                    nameof(jwtSettings));

            _userManager =
                userManager
                ?? throw new ArgumentNullException(
                    nameof(userManager));

            _refreshTokenRepository =
                refreshTokenRepository
                ?? throw new ArgumentNullException(
                    nameof(refreshTokenRepository));

            _logger =
                logger
                ?? throw new ArgumentNullException(
                    nameof(logger));

            if (string.IsNullOrWhiteSpace(_jwtSettings.Key)
                || _jwtSettings.Key.Length < 32)
            {
                throw new InvalidOperationException(
                    "A chave JWT não foi encontrada ou é demasiado curta. " +
                    "Configure JWT_KEY através de configuração segura.");
            }
        }

        // ======================================================
        // GENERATE TOKENS
        // ======================================================

        /// <summary>
        /// Gera um novo Access Token e Refresh Token
        /// para o utilizador.
        /// </summary>
        public async Task<AuthenticationResultDto>
            GenerateTokensAsync(
                User user,
                string deviceInfo)
        {
            ArgumentNullException.ThrowIfNull(user);

            deviceInfo =
                string.IsNullOrWhiteSpace(deviceInfo)
                    ? "Unknown Device"
                    : deviceInfo.Trim();

            _logger.LogInformation(
                "A gerar tokens para o utilizador {UserId}.",
                user.Id);

            // ==================================================
            // CLAIMS
            // ==================================================

            var claims = new List<Claim>
            {
                new(
                    JwtRegisteredClaimNames.Sub,
                    user.Id.ToString()),

                new(
                    JwtRegisteredClaimNames.UniqueName,
                    user.UserName ?? string.Empty),

                new(
                    ClaimTypes.NameIdentifier,
                    user.Id.ToString()),

                new(
                    JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString())
            };

            var roles =
                await _userManager.GetRolesAsync(user);

            foreach (var role in roles)
            {
                claims.Add(
                    new Claim(
                        ClaimTypes.Role,
                        role));
            }

            // ==================================================
            // ACCESS TOKEN
            // ==================================================

            var key =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(
                        _jwtSettings.Key));

            var credentials =
                new SigningCredentials(
                    key,
                    SecurityAlgorithms.HmacSha256);

            var jwt =
                new JwtSecurityToken(
                    issuer:
                        _jwtSettings.Issuer,

                    audience:
                        _jwtSettings.Audience,

                    claims:
                        claims,

                    expires:
                        DateTime.UtcNow.AddHours(
                            _jwtSettings.ExpiryHours),

                    signingCredentials:
                        credentials);

            var accessToken =
                new JwtSecurityTokenHandler()
                    .WriteToken(jwt);

            // ==================================================
            // REFRESH TOKEN
            // ==================================================
            //
            // Utilizamos um gerador criptograficamente seguro.
            //
            // 32 bytes = 256 bits de entropia.

            var refreshToken =
                GenerateRefreshToken();

            /*
             * Nunca guardamos o refresh token original
             * na base de dados.
             *
             * Guardamos apenas o SHA-256.
             */

            var hashedToken =
                HashToken(refreshToken);

            var refreshTokenEntity =
                new RefreshToken
                {
                    HashedToken =
                        hashedToken,

                    UserId =
                        user.Id,

                    ExpiryDate =
                        DateTime.UtcNow.AddDays(7),

                    IsRevoked =
                        false,

                    DeviceInfo =
                        deviceInfo
                };

            await _refreshTokenRepository
                .AddAsync(
                    refreshTokenEntity);

            _logger.LogInformation(
                "Refresh Token criado com sucesso para o utilizador {UserId}.",
                user.Id);

            /*
             * IMPORTANTE:
             *
             * Não fazer log de:
             *
             * - accessToken
             * - refreshToken
             * - hashedToken
             */

            return new AuthenticationResultDto
            {
                IsSuccess = true,
                Token = accessToken,
                RefreshToken = refreshToken
            };
        }

        // ======================================================
        // REFRESH TOKEN
        // ======================================================

        /// <summary>
        /// Valida um Refresh Token existente,
        /// invalida-o e gera um novo par de tokens.
        /// </summary>
        public async Task<AuthenticationResultDto>
            RefreshTokenAsync(
                string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(
                    refreshToken))
            {
                return new AuthenticationResultDto
                {
                    IsSuccess = false,

                    Errors = new[]
                    {
                        "Refresh Token obrigatório."
                    }
                };
            }

            _logger.LogInformation(
                "Pedido de renovação de sessão recebido.");

            var hashedToken =
                HashToken(refreshToken);

            var existingToken =
                await _refreshTokenRepository
                    .GetByTokenAsync(
                        hashedToken);

            // ==================================================
            // VALIDATE REFRESH TOKEN
            // ==================================================

            if (existingToken == null
                || existingToken.IsRevoked
                || existingToken.ExpiryDate <= DateTime.UtcNow)
            {
                _logger.LogWarning(
                    "Tentativa de utilização de Refresh Token inválido ou expirado.");

                return new AuthenticationResultDto
                {
                    IsSuccess = false,

                    Errors = new[]
                    {
                        "Token inválido ou expirado."
                    }
                };
            }

            // ==================================================
            // ROTATION
            // ==================================================
            //
            // O token atual deixa imediatamente de poder
            // ser utilizado.

            await _refreshTokenRepository
                .InvalidateTokenAsync(
                    hashedToken);

            // ==================================================
            // USER
            // ==================================================

            var user =
                await _userManager
                    .FindByIdAsync(
                        existingToken
                            .UserId
                            .ToString());

            if (user == null)
            {
                _logger.LogWarning(
                    "O utilizador associado ao Refresh Token não foi encontrado.");

                return new AuthenticationResultDto
                {
                    IsSuccess = false,

                    Errors = new[]
                    {
                        "Usuário não encontrado."
                    }
                };
            }

            _logger.LogInformation(
                "Refresh Token renovado com sucesso para o utilizador {UserId}.",
                user.Id);

            // ==================================================
            // NEW TOKEN PAIR
            // ==================================================
            //
            // GenerateTokensAsync cria:
            //
            // - novo Access Token
            // - novo Refresh Token
            //
            // Isto implementa Refresh Token Rotation.

            return await GenerateTokensAsync(
                user,
                existingToken.DeviceInfo
                    ?? "Unknown Device");
        }

        // ======================================================
        // REVOKE ONE TOKEN
        // ======================================================

        /// <summary>
        /// Revoga um Refresh Token.
        /// </summary>
        public async Task<bool>
            RevokeTokenAsync(
                string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(
                    refreshToken))
            {
                return false;
            }

            var hashedToken =
                HashToken(refreshToken);

            var existingToken =
                await _refreshTokenRepository
                    .GetByTokenAsync(
                        hashedToken);

            if (existingToken == null)
            {
                _logger.LogWarning(
                    "Tentativa de revogar um Refresh Token inexistente.");

                return false;
            }

            if (existingToken.IsRevoked)
            {
                _logger.LogInformation(
                    "O Refresh Token solicitado já se encontra revogado.");

                return false;
            }

            await _refreshTokenRepository
                .InvalidateTokenAsync(
                    hashedToken);

            _logger.LogInformation(
                "Refresh Token revogado com sucesso.");

            return true;
        }

        // ======================================================
        // REVOKE ALL USER TOKENS
        // ======================================================

        /// <summary>
        /// Revoga todos os Refresh Tokens associados
        /// ao utilizador.
        /// </summary>
        public async Task<bool>
            RevokeAllTokensForUserAsync(
                Guid userId)
        {
            _logger.LogInformation(
                "A revogar todas as sessões do utilizador {UserId}.",
                userId);

            var user =
                await _userManager
                    .FindByIdAsync(
                        userId.ToString());

            if (user == null)
            {
                _logger.LogWarning(
                    "Não foi possível revogar sessões: utilizador {UserId} não encontrado.",
                    userId);

                return false;
            }

            await _refreshTokenRepository
                .InvalidateAllTokensForUserAsync(
                    userId);

            _logger.LogInformation(
                "Todas as sessões do utilizador {UserId} foram revogadas.",
                userId);

            return true;
        }

        // ======================================================
        // GENERATE REFRESH TOKEN
        // ======================================================

        /// <summary>
        /// Gera um Refresh Token criptograficamente seguro.
        /// </summary>
        private static string GenerateRefreshToken()
        {
            /*
             * 32 bytes aleatórios = 256 bits.
             *
             * Convert.ToHexString cria uma representação
             * segura para transporte e armazenamento.
             */

            var randomBytes =
                RandomNumberGenerator
                    .GetBytes(32);

            return Convert.ToHexString(
                randomBytes);
        }

        // ======================================================
        // HASH REFRESH TOKEN
        // ======================================================

        /// <summary>
        /// Calcula SHA-256 do Refresh Token antes
        /// de consultar ou guardar na base de dados.
        /// </summary>
        private static string HashToken(
            string token)
        {
            var tokenBytes =
                Encoding.UTF8.GetBytes(
                    token);

            var hashBytes =
                SHA256.HashData(
                    tokenBytes);

            return Convert.ToBase64String(
                hashBytes);
        }
    }
}