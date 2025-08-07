using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        public DateTime JoinedAt { get; private set; }
        public DateTime? LeftAt { get; private set; }

        // Construtor privado para o EF
        private GameSessionPlayer() { }

        // Construtor público
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
            JoinedAt = DateTime.UtcNow;
        }

        public void MarkAsLeft()
        {
            if (!LeftAt.HasValue)
                LeftAt = DateTime.UtcNow;
        }
    }
}
