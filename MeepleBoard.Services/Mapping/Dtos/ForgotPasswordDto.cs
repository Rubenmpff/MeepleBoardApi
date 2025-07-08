using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    public class ForgotPasswordDto
    {
        [Required(ErrorMessage = "O e-mail é obrigatório.")]
        [EmailAddress(ErrorMessage = "Formato de e-mail inválido.")]
        public string Email { get; set; } = string.Empty;

        public bool IsMobile { get; set; } = false;
    }
}