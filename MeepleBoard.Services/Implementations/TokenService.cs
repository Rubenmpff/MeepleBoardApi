using AutoMapper;
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
        private readonly IMapper _mapper;


        public TokenService(
            IOptions<JwtSettings> jwtSettings,
            UserManager<User> userManager,
            IRefreshTokenRepository refreshTokenRepository,
            ILogger<TokenService> logger,
            IMapper mapper)


        {
            _jwtSettings = jwtSettings?.Value ?? throw new ArgumentNullException(nameof(jwtSettings));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _refreshTokenRepository = refreshTokenRepository ?? throw new ArgumentNullException(nameof(refreshTokenRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

            if (string.IsNullOrWhiteSpace(_jwtSettings.Key) || _jwtSettings.Key.Length < 32)
                throw new InvalidOperationException("❌ A chave JWT_KEY é inválida ou muito curta.");

            var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
            if (string.IsNullOrWhiteSpace(jwtKey))
                throw new InvalidOperationException("A chave JWT não foi encontrada. Configure a variável de ambiente JWT_KEY.");

            _jwtSettings.Key = jwtKey;
        }

        /// <summary>
        /// Gera um novo Access Token e Refresh Token para o usuário.
        /// </summary>
        public async Task<AuthenticationResultDto> GenerateTokensAsync(User user, string deviceInfo)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user), "O usuário fornecido não pode ser nulo.");

            _logger.LogInformation($"🔐 Gerando tokens para o usuário {user.Id} ({user.Email}) no dispositivo '{deviceInfo}'.");

            var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? ""),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var accessToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(_jwtSettings.ExpiryHours),
                signingCredentials: creds));

            var refreshToken = Guid.NewGuid().ToString("N");
            var hashedToken = HashToken(refreshToken);

            var refreshTokenEntity = new RefreshToken
            {
                HashedToken = hashedToken,
                UserId = user.Id,
                ExpiryDate = DateTime.UtcNow.AddDays(7),
                IsRevoked = false,
                DeviceInfo = deviceInfo
            };

            await _refreshTokenRepository.AddAsync(refreshTokenEntity);
            _logger.LogInformation("✅ Refresh Token persistido no banco de dados.");
            _logger.LogWarning($"🧪 Original refreshToken enviado para o cliente: {refreshToken}");
            _logger.LogWarning($"🧪 HashedToken salvo no banco: {hashedToken}");

            return new AuthenticationResultDto
            {
                IsSuccess = true,
                Token = accessToken,
                RefreshToken = refreshToken
            };
        }

        /// <summary>
        /// Valida um Refresh Token e gera um novo Access Token.
        /// </summary>
        public async Task<AuthenticationResultDto> RefreshTokenAsync(string refreshToken)
        {
            _logger.LogInformation($"Tentativa de renovar o token: {refreshToken}");

            var hashedToken = HashToken(refreshToken);
            var existingToken = await _refreshTokenRepository.GetByTokenAsync(hashedToken);
            if (existingToken == null || existingToken.IsRevoked || existingToken.ExpiryDate < DateTime.UtcNow)
            {
                _logger.LogWarning($"Tentativa de uso de Refresh Token inválido ou expirado: {refreshToken}");
                return new AuthenticationResultDto
                {
                    IsSuccess = false,
                    Errors = new[] { "Token inválido ou expirado." }
                };
            }

            await _refreshTokenRepository.InvalidateTokenAsync(hashedToken);

            var user = await _userManager.FindByIdAsync(existingToken.UserId.ToString());
            if (user == null)
            {
                _logger.LogWarning($"Usuário associado ao Refresh Token não foi encontrado.");
                return new AuthenticationResultDto { IsSuccess = false, Errors = new[] { "Usuário não encontrado." } };
            }

            _logger.LogInformation($"Refresh Token renovado com sucesso para usuário {user.Id}.");
            return await GenerateTokensAsync(user, existingToken.DeviceInfo);
        }

        /// <summary>
        /// Revoga um Refresh Token no banco de dados.
        /// </summary>
        public async Task<bool> RevokeTokenAsync(string refreshToken)
        {
            var hashedToken = HashToken(refreshToken);
            var existingToken = await _refreshTokenRepository.GetByTokenAsync(hashedToken);
            if (existingToken == null)
            {
                _logger.LogWarning($"Tentativa de revogar um Refresh Token inexistente.");
                return false;
            }

            await _refreshTokenRepository.InvalidateTokenAsync(hashedToken);
            _logger.LogInformation($"Refresh Token revogado com sucesso.");
            return true;
        }

        /// <summary>
        /// Revoga todos os Refresh Tokens do usuário (Logout Global).
        /// </summary>
        public async Task<bool> RevokeAllTokensForUserAsync(Guid userId)
        {
            _logger.LogInformation($"Revogando todos os Refresh Tokens do usuário {userId}.");

            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                _logger.LogWarning($"Usuário {userId} não encontrado.");
                return false;
            }

            await _refreshTokenRepository.InvalidateAllTokensForUserAsync(userId);

            _logger.LogInformation($"Todos os Refresh Tokens do usuário {userId} foram revogados.");
            return true;
        }

        /// <summary>
        /// Gera um hash seguro para o Refresh Token
        /// </summary>
        private static string HashToken(string token)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(hashBytes);
        }
    }
}