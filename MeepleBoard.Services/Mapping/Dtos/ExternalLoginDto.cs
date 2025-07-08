using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    public class ExternalLoginDto
    {
        [Required(ErrorMessage = "O nome do usuário é obrigatório.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "O e-mail é obrigatório.")]
        [EmailAddress(ErrorMessage = "Formato de e-mail inválido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "O provedor de autenticação é obrigatório.")]
        public string Provider { get; set; } = string.Empty;

        [Required(ErrorMessage = "O token do provedor é obrigatório.")]
        public string ProviderToken { get; set; } = string.Empty;
    }
}