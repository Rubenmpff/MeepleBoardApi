// Serviço responsável pela autenticação de utilizadores (registo, login, recuperação de palavra-passe, etc)
using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using System.Web;

namespace MeepleBoard.Services.Implementations
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<User> _userManager;
        private readonly IEmailService _emailService;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AuthService> _logger;
        private readonly IEmailResendLogRepository _emailResendLogRepository;
        private readonly IMapper _mapper;


        // Limites para envio de emails de confirmação/redefinição
        private const int DailyLimit = 3;

        private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
        private const string ConfirmationReason = "confirmation";
        private const string ResetReason = "reset";

        public AuthService(
            UserManager<User> userManager,
            IEmailService emailService,
            ITokenService tokenService,
            ILogger<AuthService> logger,
            IEmailResendLogRepository emailResendLogRepository,
            IMapper mapper)
        {
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emailResendLogRepository = emailResendLogRepository;
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        // Registo de novo utilizador
        public async Task<AuthenticationResultDto> RegisterAsync(RegisterDto registerDto, bool isMobile)
        {
            if (registerDto == null)
                return AuthenticationFailed("Registration data cannot be null.");

            var existingUser = await _userManager.FindByEmailAsync(registerDto.Email);
            if (existingUser != null)
                return AuthenticationFailed("Email is already registered.");

            var user = new User(registerDto.UserName, registerDto.Email, "Local");

            try
            {
                var result = await _userManager.CreateAsync(user, registerDto.Password);
                if (!result.Succeeded)
                    return AuthenticationFailed(result.Errors.Select(e => e.Description).ToArray());

                var roleResult = await _userManager.AddToRoleAsync(user, UserRole.User.ToString());
                if (!roleResult.Succeeded)
                {
                    await _userManager.DeleteAsync(user); // Elimina o utilizador se não for possível adicionar o papel
                    return AuthenticationFailed("Failed to assign role to user.");
                }

                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var email = user.Email ?? throw new InvalidOperationException("User email cannot be null.");
                var userName = user.UserName ?? "User";

                var confirmationLink = GenerateConfirmationLink(email, token, isMobile);
                await _emailService.SendConfirmationEmailAsync(email, userName, confirmationLink);

                return AuthenticationSuccess("Registration successful. Please confirm your email before logging in.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error registering user {user.Email}: {ex.Message}");
                return AuthenticationFailed("An error occurred during registration. Please try again later.");
            }
        }

        // Login do utilizador
        public async Task<AuthenticationResultDto> LoginAsync(LoginDto loginDto, string deviceInfo)
        {
            if (string.IsNullOrWhiteSpace(loginDto.Email) || string.IsNullOrWhiteSpace(loginDto.Password))
                return AuthenticationFailed("Email and password are required.");

            var user = await _userManager.FindByEmailAsync(loginDto.Email);
            if (user == null || !await _userManager.CheckPasswordAsync(user, loginDto.Password))
                return AuthenticationFailed("Invalid credentials.");

            if (!user.EmailConfirmed)
                return AuthenticationFailed("Please confirm your email before logging in.");

            _logger.LogInformation($"User {user.Id} ({user.Email}) logging in from device: {deviceInfo}");

            var tokenResult = await _tokenService.GenerateTokensAsync(user, deviceInfo); 
            var userDto = _mapper.Map<UserDto>(user);

            return AuthenticationSuccess("Login successful.", tokenResult.Token, tokenResult.RefreshToken, userDto);
        }

        // Confirmação de email do utilizador
        public async Task<AuthenticationResultDto> ConfirmEmailAsync(string token, string email)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
                return AuthenticationFailed("Invalid token or email.");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return AuthenticationFailed("User not found.");

            if (user.EmailConfirmed)
                return AuthenticationSuccess("Email already confirmed. You can now log in.");

            var decodedToken = HttpUtility.UrlDecode(token).Replace(" ", "+");
            var result = await _userManager.ConfirmEmailAsync(user, decodedToken);

            return result.Succeeded
                ? AuthenticationSuccess("Email confirmed successfully. You can now log in.")
                : AuthenticationFailed("Invalid or expired token.");
        }

        // Pedido de redefinição de palavra-passe
        public async Task<AuthenticationResultDto> ForgotPasswordAsync(ForgotPasswordDto forgotPasswordDto)
        {
            if (string.IsNullOrWhiteSpace(forgotPasswordDto.Email))
                return AuthenticationFailed("Email is required.");

            var user = await _userManager.FindByEmailAsync(forgotPasswordDto.Email);
            if (user == null)
                return AuthenticationFailed("User not found.");

            if (!user.EmailConfirmed)
                return AuthenticationFailed("Please confirm your email before requesting a password reset.");

            var userId = user.Id;

            var resendCount = await _emailResendLogRepository.CountResendsTodayAsync(userId, ResetReason);
            if (resendCount >= DailyLimit)
                return AuthenticationFailed("You’ve reached the daily limit for password reset requests. Please try again tomorrow.");

            var lastSent = await _emailResendLogRepository.GetLastResendTimeAsync(userId, ResetReason);
            if (lastSent.HasValue && DateTime.UtcNow - lastSent.Value < ResendCooldown)
            {
                var waitTime = (int)(ResendCooldown - (DateTime.UtcNow - lastSent.Value)).TotalSeconds;
                return AuthenticationFailed($"Please wait {waitTime} seconds before requesting again.");
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var email = user.Email ?? throw new InvalidOperationException("User email cannot be null.");
            var userName = user.UserName ?? "User";

            var resetLink = GenerateResetPasswordLink(email, token, forgotPasswordDto.IsMobile);
            await _emailService.SendPasswordResetEmailAsync(email, userName, resetLink);

            await _emailResendLogRepository.AddAsync(new EmailResendLog
            {
                UserId = userId,
                Reason = ResetReason,
                SentAt = DateTime.UtcNow
            });

            return AuthenticationSuccess("Password reset link sent. Please check your inbox.");
        }

        // Redefinir palavra-passe através de token
        public async Task<AuthenticationResultDto> ResetPasswordAsync(ResetPasswordDto resetPasswordDto)
        {
            if (string.IsNullOrWhiteSpace(resetPasswordDto.Email) ||
                string.IsNullOrWhiteSpace(resetPasswordDto.Token) ||
                string.IsNullOrWhiteSpace(resetPasswordDto.Password))
            {
                return AuthenticationFailed("All fields are required.");
            }

            var user = await _userManager.FindByEmailAsync(resetPasswordDto.Email);
            if (user == null)
                return AuthenticationFailed("User not found.");

            var decodedToken = HttpUtility.UrlDecode(resetPasswordDto.Token).Replace(" ", "+");

            var result = await _userManager.ResetPasswordAsync(user, decodedToken, resetPasswordDto.Password);

            if (!result.Succeeded)
            {
                var tokenError = result.Errors.FirstOrDefault(e => e.Code == "InvalidToken");
                if (tokenError != null)
                {
                    return AuthenticationFailed("This password reset link is no longer valid. Please request a new one.");
                }

                var errorMessages = result.Errors.Select(e => e.Description).ToArray();
                return AuthenticationFailed(errorMessages);
            }

            await _tokenService.RevokeTokenAsync(decodedToken);

            return AuthenticationSuccess("Password reset successfully.");
        }

        // Reenvio de email de confirmação
        public async Task<AuthenticationResultDto> ResendConfirmationEmailAsync(string email, bool isMobile)
        {
            if (string.IsNullOrWhiteSpace(email))
                return AuthenticationFailed("Email is required.");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return AuthenticationFailed("User not found.");

            if (user.EmailConfirmed)
                return AuthenticationFailed("This email has already been confirmed.");

            var userId = user.Id;
            var resendCount = await _emailResendLogRepository.CountResendsTodayAsync(userId, ConfirmationReason);
            if (resendCount >= DailyLimit)
                return AuthenticationFailed("Daily resend limit reached. Please try again tomorrow.");

            var lastSent = await _emailResendLogRepository.GetLastResendTimeAsync(userId, ConfirmationReason);
            if (lastSent.HasValue && DateTime.UtcNow - lastSent.Value < ResendCooldown)
            {
                var waitTime = (int)(ResendCooldown - (DateTime.UtcNow - lastSent.Value)).TotalSeconds;
                return AuthenticationFailed($"Please wait {waitTime} seconds before trying again.");
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var link = GenerateConfirmationLink(user.Email!, token, isMobile);
            await _emailService.SendConfirmationEmailAsync(user.Email!, user.UserName ?? "User", link);

            await _emailResendLogRepository.AddAsync(new EmailResendLog
            {
                UserId = userId,
                Reason = ConfirmationReason,
                SentAt = DateTime.UtcNow
            });

            return AuthenticationSuccess("Confirmation email resent.");
        }

        // Login externo (ex: Google, Facebook)
        public async Task<AuthenticationResultDto> ExternalLoginAsync(ExternalLoginDto externalLoginDto)
        {
            var user = await _userManager.FindByEmailAsync(externalLoginDto.Email);
            if (user == null)
            {
                user = new User(externalLoginDto.Name, externalLoginDto.Email, externalLoginDto.Provider);
                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return AuthenticationFailed(createResult.Errors.Select(e => e.Description).ToArray());
            }

            string deviceInfo = externalLoginDto.Provider; // Usa o nome do provedor como identificador do dispositivo

            var tokenResult = await _tokenService.GenerateTokensAsync(user, deviceInfo);
            return AuthenticationSuccess("External login successful.", tokenResult.Token, tokenResult.RefreshToken);
        }

        #region Private Methods

        // Geração de link para confirmação de email
        private static string GenerateConfirmationLink(string email, string token, bool isMobile)
        {
            string baseUrl = isMobile
                ? "meepleboard://confirm-email"
                : "https://meepleboard.com/auth/confirmemail";

            return $"{baseUrl}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        }

        // Geração de link para redefinição de palavra-passe
        private static string GenerateResetPasswordLink(string email, string token, bool isMobile)
        {
            string baseUrl = isMobile
                ? "meepleboard://reset-password"
                : "https://meepleboard.com/auth/reset-password";

            return $"{baseUrl}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        }

        // Construção de objeto de sucesso na autenticação
        private static AuthenticationResultDto AuthenticationSuccess(string message, string? token = null, string? refreshToken = null, UserDto? user = null) =>
            new() { IsSuccess = true, Message = message, Token = token, RefreshToken = refreshToken, User = user };

        // Construção de objeto de falha na autenticação
        private static AuthenticationResultDto AuthenticationFailed(params string[] errors) =>
            new() { IsSuccess = false, Errors = errors };

        #endregion Private Methods
    }
}