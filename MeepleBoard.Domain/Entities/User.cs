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
            RefreshTokens = new HashSet<RefreshToken>(); // ✅ Agora temos a coleção de RefreshTokens
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
            RefreshTokens = new HashSet<RefreshToken>(); // ✅ Garantindo a inicialização
        }

        [Required]
        public string Provider { get; private set; } = string.Empty;

        public string? ProfilePictureUrl { get; private set; }

        [JsonIgnore]
        public virtual ICollection<MatchPlayer> Matches { get; private set; }

        [JsonIgnore]
        public virtual ICollection<UserGameLibrary> UserGameLibraries { get; private set; }

        [JsonIgnore]
        public virtual ICollection<RefreshToken> RefreshTokens { get; private set; } // ✅ Relação com RefreshToken

        public DateTime CreatedAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

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

        public async Task<bool> HasRoleAsync(UserManager<User> userManager, UserRole role)
        {
            var roles = await userManager.GetRolesAsync(this);
            return roles.Any(r => string.Equals(r, role.ToString(), StringComparison.OrdinalIgnoreCase));
        }
    }
}