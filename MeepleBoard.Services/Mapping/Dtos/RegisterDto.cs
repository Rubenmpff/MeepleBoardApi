using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "O nome de usuário é obrigatório.")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "O e-mail é obrigatório.")]
        [EmailAddress(ErrorMessage = "E-mail inválido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "A senha é obrigatória.")]
        [MinLength(6, ErrorMessage = "A senha deve ter pelo menos 6 caracteres.")]
        public string Password { get; set; } = string.Empty;

        // 🔹 Novo campo para definir se o registro vem de um mobile app
        public bool IsMobile { get; set; } = false;
    }
}