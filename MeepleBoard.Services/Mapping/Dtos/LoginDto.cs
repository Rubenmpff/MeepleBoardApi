using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    public class LoginDto
    {
        [Required(ErrorMessage = "O e-mail é obrigatório.")]
        [EmailAddress(ErrorMessage = "E-mail inválido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "A senha é obrigatória.")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Informação do dispositivo do usuário (ex: "Chrome - Windows 10", "iPhone 13 - Safari")
        /// </summary>
        public string DeviceInfo { get; set; } = "Unknown Device"; // 🔹 Fallback para evitar valores nulos
    }
}