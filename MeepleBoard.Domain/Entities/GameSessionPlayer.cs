using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MeepleBoard.Domain.Enums;

namespace MeepleBoard.Domain.Entities
{
    public class GameSessionPlayer
    {
        [Key]
        public Guid Id { get; private set; }

        [Required]
        public Guid SessionId { get; private set; }

        [ForeignKey(nameof(SessionId))]
        public virtual GameSession Session { get; private set; } = null!;

        [Required]
        public Guid UserId { get; private set; }

        [ForeignKey(nameof(UserId))]
        public virtual User User { get; private set; } = null!;

        public bool IsOrganizer { get; private set; }

        /// <summary>
        /// Estado do convite/participação nesta sessão.
        /// Organizer entra sempre como Accepted.
        /// </summary>
        public GameSessionInviteStatus Status { get; private set; } = GameSessionInviteStatus.Pending;

        public DateTime InvitedAt { get; private set; } = DateTime.UtcNow;

        public DateTime? RespondedAt { get; private set; }

        // Mantém (pode ser útil para auditoria/compat)
        public DateTime JoinedAt { get; private set; } = DateTime.UtcNow;

        // Se no futuro quiseres permitir "sair" (por agora não usas)
        public DateTime? LeftAt { get; private set; }

        private GameSessionPlayer() { }

        /// <summary>
        /// Cria um vínculo de convite/participação.
        /// Organizer: Accepted.
        /// Convidados: Pending.
        /// </summary>
        public GameSessionPlayer(Guid sessionId, Guid userId, bool isOrganizer = false)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("ID da sessão inválido.", nameof(sessionId));

            if (userId == Guid.Empty)
                throw new ArgumentException("ID do jogador inválido.", nameof(userId));

            Id = Guid.NewGuid();
            SessionId = sessionId;
            UserId = userId;

            IsOrganizer = isOrganizer;

            InvitedAt = DateTime.UtcNow;
            JoinedAt = DateTime.UtcNow;

            Status = isOrganizer ? GameSessionInviteStatus.Accepted : GameSessionInviteStatus.Pending;
            RespondedAt = isOrganizer ? DateTime.UtcNow : null;
        }

        public void Accept()
        {
            if (Status == GameSessionInviteStatus.Accepted) return;
            Status = GameSessionInviteStatus.Accepted;
            RespondedAt = DateTime.UtcNow;
        }

        public void Decline()
        {
            if (Status == GameSessionInviteStatus.Declined) return;
            Status = GameSessionInviteStatus.Declined;
            RespondedAt = DateTime.UtcNow;
        }

        public void MarkAsLeft()
        {
            if (!LeftAt.HasValue)
                LeftAt = DateTime.UtcNow;
        }
    }
}