using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.IdentityModel.Tokens.Jwt;
using System.Net;

namespace MeepleBoard.CrossCutting.Middlewares
{
    public class TokenAutoRefreshMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITokenService _tokenService;
        private readonly ILogger<TokenAutoRefreshMiddleware> _logger;

        public TokenAutoRefreshMiddleware(RequestDelegate next, ITokenService tokenService, ILogger<TokenAutoRefreshMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                if (context.Request.Headers.TryGetValue("Authorization", out StringValues authHeader))
                {
                    var token = authHeader.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

                    if (IsTokenExpired(token))
                    {
                        _logger.LogInformation("🔁 Access Token expired. Attempting to refresh using Refresh Token...");

                        var refreshToken = context.Request.Cookies["refreshToken"];
                        if (string.IsNullOrWhiteSpace(refreshToken))
                        {
                            _logger.LogWarning("🚫 No Refresh Token found in cookies.");
                            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                            await context.Response.WriteAsync("Session expired. Please log in again.");
                            return;
                        }

                        var refreshResult = await _tokenService.RefreshTokenAsync(refreshToken);

                        if (refreshResult.IsSuccess && !string.IsNullOrWhiteSpace(refreshResult.RefreshToken))
                        {
                            _logger.LogInformation("✅ New Access Token successfully generated.");

                            // Atualiza o cabeçalho Authorization com o novo JWT
                            context.Request.Headers["Authorization"] = $"Bearer {refreshResult.Token}";

                            // Atualiza o Cookie de Refresh Token
                            context.Response.Cookies.Append("refreshToken", refreshResult.RefreshToken, new CookieOptions
                            {
                                HttpOnly = true,
                                Secure = true,
                                SameSite = SameSiteMode.Strict,
                                Expires = DateTime.UtcNow.AddDays(7)
                            });
                        }
                        else
                        {
                            _logger.LogWarning("❌ Failed to refresh token. Logging user out.");

                            context.Response.Cookies.Delete("refreshToken");
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            await context.Response.WriteAsync("Session expired. Please log in again.");
                            return;
                        }
                    }
                }

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "⚠️ Error occurred in TokenAutoRefreshMiddleware.");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.Response.WriteAsync("Unexpected error during token validation.");
            }
        }

        private static bool IsTokenExpired(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return true;

            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token)) return true;

            var jwtToken = handler.ReadJwtToken(token);
            return jwtToken.ValidTo < DateTime.UtcNow;
        }
    }
}