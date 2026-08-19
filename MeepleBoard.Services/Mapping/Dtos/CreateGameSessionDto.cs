using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Application.DTOs
{
    public class CreateGameSessionDto
    {
        [Required]
        [MinLength(3)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Location { get; set; }

        /// <summary>
        /// Data/hora planeada para começar (UTC).
        /// </summary>
        public DateTime? ScheduledStartDate { get; set; }

        /// <summary>
        /// Data limite para os convidados responderem (UTC).
        /// Tem de ser anterior a ScheduledStartDate.
        /// Se null, usa ScheduledStartDate como limite.
        /// </summary>
        public DateTime? ResponseDeadline { get; set; }

        /// <summary>
        /// IDs dos utilizadores a convidar inicialmente (Pending).
        /// </summary>
        public List<Guid> PlayerIds { get; set; } = new();
    }
}