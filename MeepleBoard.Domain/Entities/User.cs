using MeepleBoard.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MeepleBoard.Domain.Entities
{
    public class User : IdentityUser<Guid>
    {
        private User()
        {
            Matches = new HashSet<MatchPlayer>();
            UserGameLibraries = new HashSet<UserGameLibrary>();
            RefreshTokens = new HashSet<RefreshToken>();
        }

        public User(string userName, string email, string provider)
        {
            Id = Guid.NewGuid();
            UserName = string.IsNullOrWhiteSpace(userName)
                ? throw new ArgumentException("O nome de usuário é obrigatório.")
                : userName;
            Email = string.IsNullOrWhiteSpace(email)
                ? throw new ArgumentException("O e-mail é obrigatório.")
                : email;
            Provider = string.IsNullOrWhiteSpace(provider)
                ? throw new ArgumentException("O provedor de autenticação é obrigatório.")
                : provider;
            CreatedAt = DateTime.UtcNow;
            Matches = new HashSet<MatchPlayer>();
            UserGameLibraries = new HashSet<UserGameLibrary>();
            RefreshTokens = new HashSet<RefreshToken>();
        }

        [Required]
        public string Provider { get; private set; } = string.Empty;

        public string? ProfilePictureUrl { get; private set; }

        /// <summary>
        /// Token de push do Expo para enviar notificações ao dispositivo do utilizador.
        /// Formato: ExponentPushToken[xxxxxxxxxxxxxxxxxxxxxx]
        /// Atualizado sempre que o utilizador abre a app num dispositivo novo.
        /// </summary>
        [MaxLength(200)]
        public string? ExpoPushToken { get; private set; }

        /// <summary>
        /// Quem pode ver a coleção (biblioteca) de jogos deste utilizador.
        /// Default = Public para preservar o comportamento atual (endpoint
        /// já era acessível sem restrições antes desta funcionalidade existir).
        /// </summary>
        public LibraryPrivacy LibraryPrivacy { get; private set; } = LibraryPrivacy.Public;

        /// <summary>
        /// Última vez que este utilizador fez um pedido autenticado à API.
        /// Usado só para o indicador "online" nos ecrãs de Amigos — não é uma
        /// presença em tempo real, é uma aproximação por pedido HTTP.
        /// </summary>
        public DateTime? LastActiveAt { get; private set; }

        [JsonIgnore]
        public virtual ICollection<MatchPlayer> Matches { get; private set; }

        [JsonIgnore]
        public virtual ICollection<UserGameLibrary> UserGameLibraries { get; private set; }

        [JsonIgnore]
        public virtual ICollection<RefreshToken> RefreshTokens { get; private set; }

        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }

        // ── Métodos ───────────────────────────────────────────────────────────

        public void SetCreatedAt(DateTime createdAt)
        {
            if (createdAt > DateTime.UtcNow)
                throw new ArgumentException("A data de criação não pode estar no futuro.");
            CreatedAt = createdAt;
        }

        public void SetUpdatedAt()
        {
            UpdatedAt = DateTime.UtcNow;
        }

        public void UpdateUser(string userName, string email, string? profilePictureUrl = null)
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("O nome de usuário é obrigatório.");
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("O e-mail é obrigatório.");

            UserName = userName;
            Email = email;
            ProfilePictureUrl = profilePictureUrl;
            SetUpdatedAt();
        }

        /// <summary>
        /// Atualiza o token de push do Expo.
        /// Chamado quando o utilizador abre a app e o token é gerado/renovado.
        /// </summary>
        public void SetExpoPushToken(string? token)
        {
            if (ExpoPushToken != token)
            {
                ExpoPushToken = token?.Trim();
                SetUpdatedAt();
            }
        }

        /// <summary>
        /// Define quem pode ver a coleção de jogos deste utilizador.
        /// </summary>
        public void SetLibraryPrivacy(LibraryPrivacy privacy)
        {
            if (LibraryPrivacy != privacy)
            {
                LibraryPrivacy = privacy;
                SetUpdatedAt();
            }
        }

        /// <summary>
        /// Marca agora como a última atividade do utilizador. Normalmente não é
        /// preciso chamar isto diretamente — o <c>UserRepository.TouchLastActiveAsync</c>
        /// atualiza a coluna sem carregar a entidade inteira, por eficiência.
        /// </summary>
        public void SetLastActiveAt(DateTime lastActiveAt)
        {
            LastActiveAt = lastActiveAt;
        }

        public async Task<bool> HasRoleAsync(UserManager<User> userManager, UserRole role)
        {
            var roles = await userManager.GetRolesAsync(this);
            return roles.Any(r => string.Equals(r, role.ToString(), StringComparison.OrdinalIgnoreCase));
        }
    }
}