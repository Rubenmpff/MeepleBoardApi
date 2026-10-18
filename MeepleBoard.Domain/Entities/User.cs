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
        /// Whether this person lets the durations of their matches count towards the community duration
        /// statistics. Off by default; a match counts only when every participant has it on.
        /// </summary>
        public bool ContributesDurationStats { get; private set; }

        /// <summary>
        /// Última vez que este utilizador fez um pedido autenticado à API.
        /// Usado só para o indicador "online" nos ecrãs de Amigos — não é uma
        /// presença em tempo real, é uma aproximação por pedido HTTP.
        /// </summary>
        public DateTime? LastActiveAt { get; private set; }

        /// <summary>When the account was deleted. The row stays, anonymised, so shared matches keep their players.</summary>
        public DateTime? DeletedAt { get; private set; }

        // ── Administrative suspension (separate from Identity's lockout after failed sign-ins) ──────────────────
        /// <summary>Set while suspended (since when). Null: not suspended.</summary>
        public DateTime? SuspendedAt { get; private set; }
        /// <summary>End of the suspension; null while suspended means «until review».</summary>
        public DateTime? SuspendedUntil { get; private set; }
        [System.ComponentModel.DataAnnotations.MaxLength(300)]
        public string? SuspensionReason { get; private set; }
        public Guid? SuspendedById { get; private set; }
        public DateTime? SuspensionLiftedAt { get; private set; }
        public Guid? SuspensionLiftedById { get; private set; }
        /// <summary>Access tokens issued before this moment are refused (set at a suspension, kept after it ends).</summary>
        public DateTime? TokensRevokedAt { get; private set; }

        public bool IsSuspended(DateTime nowUtc) => SuspendedAt != null && (SuspendedUntil == null || nowUtc < SuspendedUntil);

        /// <summary>Suspends until a date or, with null, until review. Every session ends: earlier tokens never work again.</summary>
        public void Suspend(DateTime? untilUtc, string reason, Guid administratorId, DateTime nowUtc)
        {
            if (DeletedAt != null) throw new InvalidOperationException("A conta foi eliminada.");
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Indica o motivo da suspensão.");
            if (untilUtc != null && untilUtc <= nowUtc) throw new ArgumentException("O fim da suspensão tem de ser no futuro.");
            SuspendedAt = nowUtc; SuspendedUntil = untilUtc; SuspensionReason = reason.Trim(); SuspendedById = administratorId;
            SuspensionLiftedAt = null; SuspensionLiftedById = null; TokensRevokedAt = nowUtc;
            SecurityStamp = Guid.NewGuid().ToString();
        }

        /// <summary>Lifts it before its end. The person signs in again (earlier tokens stay refused).</summary>
        public void LiftSuspension(Guid administratorId, DateTime nowUtc)
        {
            if (SuspendedAt == null) throw new InvalidOperationException("A conta não está suspensa.");
            SuspendedAt = null; SuspendedUntil = null; SuspensionReason = null; SuspendedById = null;
            SuspensionLiftedAt = nowUtc; SuspensionLiftedById = administratorId;
        }

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
        public void SetContributesDurationStats(bool enabled)
        {
            if (ContributesDurationStats != enabled)
            {
                ContributesDurationStats = enabled;
                SetUpdatedAt();
            }
        }

        /// <summary>
        /// Account deletion: everything that identifies or reaches the person goes (name, email, phone, password,
        /// picture, push token), sign-in is closed for good, and the row keeps a neutral unique name so shared
        /// matches, sessions and campaigns keep their players and results.
        /// </summary>
        /// <summary>
        /// The name a deleted account keeps: a neutral marker, never text in one language (the app shows it translated).
        /// ':' is not accepted in names chosen at registration, so no account can take it.
        /// </summary>
        public static string DeletedUserName(Guid id) => "deleted:" + id.ToString("N")[..8];
        // Neutral titles for text the person wrote in shared records: markers in no language, translated by the app.
        public static string DeletedSessionName(DateTime scheduledUtc) => "deleted:session:" + scheduledUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        public const string DeletedCampaignName = "deleted:campaign";

        /// <summary>True for any of the neutral markers above (deleted account, session or campaign).</summary>
        public static bool IsDeletedMarker(string? text) => text != null && text.StartsWith("deleted:", StringComparison.Ordinal);

        /// <summary>Last guard for text sent outside the app (push notifications): markers become neutral Portuguese words.</summary>
        public static string WithoutDeletedMarkers(string text) => System.Text.RegularExpressions.Regex.Replace(text,
            @"deleted:(session:\d{4}-\d{2}-\d{2}|campaign|[0-9a-f]{8})", m => m.Groups[1].Value switch
            {
                "campaign" => "campanha sem título",
                var v when v.StartsWith("session:") => "sessão",
                _ => "Conta eliminada",
            });

        public void Anonymize(string deletedUserName)
        {
            if (string.IsNullOrWhiteSpace(deletedUserName)) throw new ArgumentException("O nome da conta eliminada é obrigatório.");
            if (DeletedAt.HasValue) throw new InvalidOperationException("A conta já foi eliminada.");
            UserName = deletedUserName;
            NormalizedUserName = deletedUserName.ToUpperInvariant();
            Email = null;
            NormalizedEmail = null;
            EmailConfirmed = false;
            PhoneNumber = null;
            PhoneNumberConfirmed = false;
            PasswordHash = null;
            TwoFactorEnabled = false;
            SecurityStamp = Guid.NewGuid().ToString();
            ConcurrencyStamp = Guid.NewGuid().ToString();
            LockoutEnabled = true;
            LockoutEnd = DateTimeOffset.MaxValue;
            ProfilePictureUrl = null;
            ExpoPushToken = null;
            LastActiveAt = null;
            LibraryPrivacy = LibraryPrivacy.Private;
            ContributesDurationStats = false;
            DeletedAt = DateTime.UtcNow;
            SetUpdatedAt();
        }

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