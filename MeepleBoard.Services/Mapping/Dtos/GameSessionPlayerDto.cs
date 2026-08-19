using MeepleBoard.Domain.Enums;

namespace MeepleBoard.Application.DTOs
{
    public class GameSessionPlayerDto
    {
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;

        public bool IsOrganizer { get; set; }

        // ✅ NOVO: Pending / Accepted / Declined
        public GameSessionInviteStatus Status { get; set; }

        // ✅ Datas úteis para UI
        public DateTime InvitedAt { get; set; }
        public DateTime? RespondedAt { get; set; }

        // Mantém (compat)
        public DateTime JoinedAt { get; set; }
        public DateTime? LeftAt { get; set; }
    }
}