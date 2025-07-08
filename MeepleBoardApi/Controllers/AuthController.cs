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
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            if (registerDto == null)
                return BadRequest("Os dados de registro não podem ser nulos.");

            var result = await _authService.RegisterAsync(registerDto, registerDto.IsMobile);
            if (!result.IsSuccess)
                return BadRequest(result);

            return Ok(new { Message = result.Message });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            if (loginDto == null)
                return BadRequest(new { success = false, message = "Os dados de login são obrigatórios." });

            string deviceInfo = Request.Headers["User-Agent"].ToString();

            var result = await _authService.LoginAsync(loginDto, deviceInfo);

            // 🧠 Se falhou (ex: credenciais inválidas, email não confirmado, etc)
            if (!result.IsSuccess)
            {
                _logger.LogWarning($"❌ Falha no login para {loginDto.Email}: {result.Message}");
                return BadRequest(new
                {
                    success = false,
                    message = result.Message ?? "Login falhou.",
                    errors = result.Errors
                });
            }

            // 🛡️ Segurança extra – esse check agora raramente será atingido
            if (string.IsNullOrEmpty(result.RefreshToken))
            {
                _logger.LogError("❌ Login falhou: Refresh Token não foi gerado.");
                return StatusCode(500, "Erro ao gerar token de atualização.");
            }

            _logger.LogInformation($"✅ Login bem-sucedido para {loginDto.Email}");
            _logger.LogDebug($"🔑 JWT: {result.Token?.Substring(0, 20)}...");
            _logger.LogDebug($"🔁 Refresh Token: {result.RefreshToken}");

            // 🍪 Salva o refreshToken com segurança no cookie (caso use no frontend web)
            Response.Cookies.Append("refreshToken", result.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(7)
            });

            // 🎯 Retorna os tokens no body para uso mobile
            return Ok(new
            {
                success = true,
                token = result.Token,
                refreshToken = result.RefreshToken,
                user = result.User
            });
        }

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail([FromQuery] string token, [FromQuery] string email)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
                return BadRequest("Token e email são obrigatórios.");

            var result = await _authService.ConfirmEmailAsync(token, email);
            if (!result.IsSuccess)
                return BadRequest(result);

            return Ok(new { Message = result.Message });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            if (forgotPasswordDto == null)
                return BadRequest("Os dados são obrigatórios.");

            var result = await _authService.ForgotPasswordAsync(forgotPasswordDto);
            if (!result.IsSuccess)
                return BadRequest(result);

            return Ok(new { Message = result.Message });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetPasswordDto)
        {
            if (resetPasswordDto == null)
                return BadRequest("Os dados são obrigatórios.");

            var result = await _authService.ResetPasswordAsync(resetPasswordDto);
            if (!result.IsSuccess)
                return BadRequest(result);

            return Ok(new { Message = result.Message });
        }

        [HttpPost("resend-confirmation")]
        public async Task<IActionResult> ResendEmailConfirmation([FromBody] ResendConfirmationDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest("O e-mail é obrigatório.");

            var result = await _authService.ResendConfirmationEmailAsync(dto.Email, dto.IsMobile);
            if (!result.IsSuccess)
                return BadRequest(result);

            return Ok(new { Message = result.Message });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken()
        {
            if (!Request.Cookies.TryGetValue("refreshToken", out string? refreshToken))
                return BadRequest("Nenhum Refresh Token encontrado.");

            var result = await _tokenService.RefreshTokenAsync(refreshToken);
            if (!result.IsSuccess)
                return Unauthorized("Refresh Token inválido ou expirado.");

            return Ok(new { Token = result.Token });
        }

        [Authorize]
        [HttpPost("revoke-token")]
        public async Task<IActionResult> RevokeToken([FromBody] RefreshTokenRequestDto revokeRequest)
        {
            if (revokeRequest == null || string.IsNullOrWhiteSpace(revokeRequest.RefreshToken))
                return BadRequest("O Refresh Token é obrigatório.");

            bool revoked = await _tokenService.RevokeTokenAsync(revokeRequest.RefreshToken);
            if (!revoked)
                return NotFound("O Refresh Token não foi encontrado ou já foi revogado.");

            return Ok(new { Message = "Refresh Token revogado com sucesso." });
        }

        [Authorize]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("refreshToken");
            return Ok(new { Message = "Logout realizado com sucesso." });
        }

        [Authorize]
        [HttpPost("logout-all")]
        public async Task<IActionResult> LogoutAll()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized("Usuário não autenticado.");

            await _tokenService.RevokeAllTokensForUserAsync(userId);
            return Ok(new { Message = "Todos os Refresh Tokens foram revogados." });
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("admin")]
        public async Task<IActionResult> IsAdmin()
        {
            await Task.Delay(1);
            return Ok(new { Message = "Usuário é administrador" });
        }
    }
}