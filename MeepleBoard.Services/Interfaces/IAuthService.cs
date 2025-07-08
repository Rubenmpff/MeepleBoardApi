using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Interfaces
{
    public interface IAuthService
    {
        /// <summary>
        /// Registra um novo usuário.
        /// </summary>
        Task<AuthenticationResultDto> RegisterAsync(RegisterDto registerDto, bool isMobile);

        /// <summary>
        /// Realiza o login do usuário.
        /// </summary>
        Task<AuthenticationResultDto> LoginAsync(LoginDto loginDto, string deviceInfo);

        /// <summary>
        /// Confirma o e-mail do usuário com um token.
        /// </summary>
        Task<AuthenticationResultDto> ConfirmEmailAsync(string token, string email);

        /// <summary>
        /// Gera um link de redefinição de senha e envia por e-mail.
        /// </summary>
        Task<AuthenticationResultDto> ForgotPasswordAsync(ForgotPasswordDto forgotPasswordDto);

        /// <summary>
        /// Permite redefinir a senha usando um token de redefinição.
        /// </summary>
        Task<AuthenticationResultDto> ResetPasswordAsync(ResetPasswordDto resetPasswordDto);

        Task<AuthenticationResultDto> ResendConfirmationEmailAsync(string email, bool isMobile);

        /// <summary>
        /// Permite login através de provedores externos (Google, Apple, etc.).
        /// </summary>
        Task<AuthenticationResultDto> ExternalLoginAsync(ExternalLoginDto externalLoginDto);
    }
}