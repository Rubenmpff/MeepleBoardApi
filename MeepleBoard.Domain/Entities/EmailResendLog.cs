using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    public class EmailResendLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        [Required]
        public DateTime SentAt { get; set; }

        [Required]
        [MaxLength(50)]
        public string Reason { get; set; } = "confirmation"; // ex: "confirmation", "reset"
    }
}