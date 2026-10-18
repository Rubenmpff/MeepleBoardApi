using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Mapping.Dtos
{
    public class AuthenticationResultDto
    {
        /// <summary>
        /// Indica se a operação foi bem-sucedida.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Token JWT gerado para autenticação do usuário.
        /// </summary>
        public string? Token { get; set; }

        /// <summary>
        /// Refresh Token usado para renovar o acesso.
        /// </summary>
        public string? RefreshToken { get; set; }

        public UserDto? User { get; set; }  


        /// <summary>
        /// Mensagem de retorno para feedback ao usuário.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Lista de erros, se houver falha na autenticação.
        /// </summary>
        public IEnumerable<string>? Errors { get; set; }

        /// <summary>A stable code for the app (e.g. «account_suspended»), when the text alone is not enough.</summary>
        public string? Code { get; set; }

        /// <summary>End of a suspension; null with «account_suspended» means «until review».</summary>
        public DateTime? SuspendedUntil { get; set; }
    }
}