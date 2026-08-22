using MeepleBoard.Domain.Entities;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MeepleBoardApi.Controllers
{
    [ApiController]
    [Route("MeepleBoard/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ITokenService _tokenService;
        private readonly UserManager<User> _userManager;
        private readonly IEmailService _emailService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IAuthService authService,
            ITokenService tokenService,
            UserManager<User> userManager,
            IEmailService emailService,
            ILogger<AuthController> logger)
        {
            _authService =
                authService
                ?? throw new ArgumentNullException(nameof(authService));

            _tokenService =
                tokenService
                ?? throw new ArgumentNullException(nameof(tokenService));

            _userManager =
                userManager
                ?? throw new ArgumentNullException(nameof(userManager));

            _emailService =
                emailService
                ?? throw new ArgumentNullException(nameof(emailService));

            _logger =
                logger
                ?? throw new ArgumentNullException(nameof(logger));
        }

        // ======================================================
        // REGISTER
        // ======================================================

        [HttpPost("register")]
        public async Task<IActionResult> Register(
            [FromBody] RegisterDto registerDto)
        {
            if (registerDto == null)
            {
                return BadRequest(
                    "Os dados de registro não podem ser nulos.");
            }

            var result =
                await _authService.RegisterAsync(
                    registerDto,
                    registerDto.IsMobile);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(new
            {
                Message = result.Message
            });
        }

        // ======================================================
        // LOGIN
        // ======================================================

        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromBody] LoginDto loginDto)
        {
            if (loginDto == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Os dados de login são obrigatórios."
                });
            }

            var deviceInfo =
                Request.Headers["User-Agent"]
                    .ToString();

            if (string.IsNullOrWhiteSpace(deviceInfo))
            {
                deviceInfo = "Unknown Device";
            }

            var result =
                await _authService.LoginAsync(
                    loginDto,
                    deviceInfo);

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Falha no login para o utilizador {Email}: {Message}",
                    loginDto.Email,
                    result.Message);

                return BadRequest(new
                {
                    success = false,

                    message =
                        result.Message
                        ?? "Login falhou.",

                    errors =
                        result.Errors
                });
            }

            if (string.IsNullOrWhiteSpace(result.Token))
            {
                _logger.LogError(
                    "Login falhou porque o Access Token não foi gerado.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "Erro ao gerar token de autenticação."
                    });
            }

            if (string.IsNullOrWhiteSpace(result.RefreshToken))
            {
                _logger.LogError(
                    "Login falhou porque o Refresh Token não foi gerado.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "Erro ao gerar token de atualização."
                    });
            }

            _logger.LogInformation(
                "Login bem-sucedido para o utilizador {Email}.",
                loginDto.Email);

            Response.Cookies.Append(
                "refreshToken",
                result.RefreshToken,
                CreateRefreshTokenCookieOptions());

            return Ok(new
            {
                success = true,
                token = result.Token,
                refreshToken = result.RefreshToken,
                user = result.User
            });
        }

        // ======================================================
        // CONFIRM EMAIL
        // ======================================================

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(
            [FromQuery] string token,
            [FromQuery] string email)
        {
            if (string.IsNullOrWhiteSpace(token)
                || string.IsNullOrWhiteSpace(email))
            {
                return BadRequest(
                    "Token e email são obrigatórios.");
            }

            var result =
                await _authService.ConfirmEmailAsync(
                    token,
                    email);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(new
            {
                Message = result.Message
            });
        }

        // ======================================================
        // FORGOT PASSWORD
        // ======================================================

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(
            [FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            if (forgotPasswordDto == null)
            {
                return BadRequest(
                    "Os dados são obrigatórios.");
            }

            var result =
                await _authService.ForgotPasswordAsync(
                    forgotPasswordDto);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(new
            {
                Message = result.Message
            });
        }

        // ======================================================
        // RESET PASSWORD
        // ======================================================

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(
            [FromBody] ResetPasswordDto resetPasswordDto)
        {
            if (resetPasswordDto == null)
            {
                return BadRequest(
                    "Os dados são obrigatórios.");
            }

            var result =
                await _authService.ResetPasswordAsync(
                    resetPasswordDto);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(new
            {
                Message = result.Message
            });
        }

        // ======================================================
        // RESEND EMAIL CONFIRMATION
        // ======================================================

        [HttpPost("resend-confirmation")]
        public async Task<IActionResult> ResendEmailConfirmation(
            [FromBody] ResendConfirmationDto dto)
        {
            if (dto == null
                || string.IsNullOrWhiteSpace(dto.Email))
            {
                return BadRequest(
                    "O e-mail é obrigatório.");
            }

            var result =
                await _authService
                    .ResendConfirmationEmailAsync(
                        dto.Email,
                        dto.IsMobile);

            if (!result.IsSuccess)
            {
                return BadRequest(result);
            }

            return Ok(new
            {
                Message = result.Message
            });
        }

        // ======================================================
        // REFRESH TOKEN
        // ======================================================

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken(
            [FromBody] RefreshTokenRequestDto? request)
        {
            var refreshToken =
                request?.RefreshToken;

            // ==================================================
            // WEB FALLBACK
            // ==================================================
            //
            // Se não vier no body, tentamos usar o cookie.
            // Isto deixa o backend preparado para um futuro
            // frontend web.

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                Request.Cookies.TryGetValue(
                    "refreshToken",
                    out refreshToken);
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Nenhum Refresh Token foi fornecido."
                });
            }

            var result =
                await _tokenService.RefreshTokenAsync(
                    refreshToken);

            if (!result.IsSuccess
                || string.IsNullOrWhiteSpace(result.Token)
                || string.IsNullOrWhiteSpace(result.RefreshToken))
            {
                return Unauthorized(new
                {
                    success = false,

                    message =
                        "Refresh Token inválido ou expirado.",

                    errors =
                        result.Errors
                });
            }

            // Atualiza o cookie para clientes web.
            Response.Cookies.Append(
                "refreshToken",
                result.RefreshToken,
                CreateRefreshTokenCookieOptions());

            // Mobile recebe os novos tokens no body.
            return Ok(new
            {
                success = true,
                token = result.Token,
                refreshToken = result.RefreshToken
            });
        }

        // ======================================================
        // REVOKE TOKEN
        // ======================================================

        [Authorize]
        [HttpPost("revoke-token")]
        public async Task<IActionResult> RevokeToken(
            [FromBody] RefreshTokenRequestDto revokeRequest)
        {
            if (revokeRequest == null
                || string.IsNullOrWhiteSpace(
                    revokeRequest.RefreshToken))
            {
                return BadRequest(
                    "O Refresh Token é obrigatório.");
            }

            var revoked =
                await _tokenService.RevokeTokenAsync(
                    revokeRequest.RefreshToken);

            if (!revoked)
            {
                return NotFound(
                    "O Refresh Token não foi encontrado ou já foi revogado.");
            }

            return Ok(new
            {
                Message =
                    "Refresh Token revogado com sucesso."
            });
        }

        // ======================================================
        // LOGOUT
        // ======================================================

        [Authorize]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete(
                "refreshToken");

            return Ok(new
            {
                Message =
                    "Logout realizado com sucesso."
            });
        }

        // ======================================================
        // LOGOUT ALL DEVICES
        // ======================================================

        [Authorize]
        [HttpPost("logout-all")]
        public async Task<IActionResult> LogoutAll()
        {
            var userIdString =
                User.FindFirst(
                    ClaimTypes.NameIdentifier)
                    ?.Value;

            if (string.IsNullOrWhiteSpace(userIdString)
                || !Guid.TryParse(
                    userIdString,
                    out var userId))
            {
                return Unauthorized(
                    "Usuário não autenticado.");
            }

            var result =
                await _tokenService
                    .RevokeAllTokensForUserAsync(
                        userId);

            if (!result)
            {
                return BadRequest(new
                {
                    Message =
                        "Não foi possível revogar as sessões do utilizador."
                });
            }

            Response.Cookies.Delete(
                "refreshToken");

            return Ok(new
            {
                Message =
                    "Todos os Refresh Tokens foram revogados."
            });
        }

        // ======================================================
        // ADMIN TEST
        // ======================================================

        [Authorize(Roles = "Admin")]
        [HttpGet("admin")]
        public IActionResult IsAdmin()
        {
            return Ok(new
            {
                Message =
                    "Usuário é administrador"
            });
        }

        // ======================================================
        // COOKIE OPTIONS
        // ======================================================

        private static CookieOptions
            CreateRefreshTokenCookieOptions()
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,

                Expires =
                    DateTimeOffset.UtcNow.AddDays(7),

                Path = "/"
            };
        }
    }
}