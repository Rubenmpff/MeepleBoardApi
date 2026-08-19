using MeepleBoard.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    /// <summary>
    /// DTO para representar um usuário no MeepleBoard.
    /// </summary>
    public class UserDto
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "O nome de usuário é obrigatório.")]
        [MaxLength(100, ErrorMessage = "O nome de usuário deve ter no máximo 100 caracteres.")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "O email é obrigatório.")]
        [EmailAddress(ErrorMessage = "O email deve ser válido.")]
        [MaxLength(150, ErrorMessage = "O email deve ter no máximo 150 caracteres.")]
        public string Email { get; set; } = string.Empty;

        [Url(ErrorMessage = "A URL da imagem de perfil não é válida.")]
        public string? ProfilePictureUrl { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "O total de jogos jogados não pode ser negativo.")]
        public int TotalGamesPlayed { get; set; } = 0;

        [Range(0, int.MaxValue, ErrorMessage = "O total de vitórias não pode ser negativo.")]
        public int TotalWins { get; set; } = 0;

        /// <summary>
        /// Quem pode ver a coleção de jogos deste utilizador (Private, FriendsOnly, Public).
        /// </summary>
        public LibraryPrivacy LibraryPrivacy { get; set; } = LibraryPrivacy.Public;
    }

    /// <summary>DTO usado para o utilizador autenticado alterar a privacidade da sua coleção.</summary>
    public class UpdateLibraryPrivacyDto
    {
        public LibraryPrivacy LibraryPrivacy { get; set; }
    }
}