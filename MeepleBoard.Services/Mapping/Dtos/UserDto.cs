using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    /// <summary>
    /// DTO para representar um usuário no MeepleBoard.
    /// </summary>
    public class UserDto
    {
        /// <summary>
        /// Identificador único do usuário.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Nome de usuário (Obrigatório, no máximo 100 caracteres).
        /// </summary>
        [Required(ErrorMessage = "O nome de usuário é obrigatório.")]
        [MaxLength(100, ErrorMessage = "O nome de usuário deve ter no máximo 100 caracteres.")]
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// Email do usuário (Obrigatório e válido).
        /// </summary>
        [Required(ErrorMessage = "O email é obrigatório.")]
        [EmailAddress(ErrorMessage = "O email deve ser válido.")]
        [MaxLength(150, ErrorMessage = "O email deve ter no máximo 150 caracteres.")]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// URL da imagem de perfil do usuário (Opcional).
        /// </summary>
        [Url(ErrorMessage = "A URL da imagem de perfil não é válida.")]
        public string? ProfilePictureUrl { get; set; }

        /// <summary>
        /// Total de partidas jogadas pelo usuário.
        /// </summary>
        [Range(0, int.MaxValue, ErrorMessage = "O total de jogos jogados não pode ser negativo.")]
        public int TotalGamesPlayed { get; set; } = 0;

        /// <summary>
        /// Total de vitórias do usuário.
        /// </summary>
        [Range(0, int.MaxValue, ErrorMessage = "O total de vitórias não pode ser negativo.")]
        public int TotalWins { get; set; } = 0;
    }
}