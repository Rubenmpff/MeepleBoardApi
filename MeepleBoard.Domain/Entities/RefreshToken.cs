using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string HashedToken { get; set; } = null!; // 🔹 Armazena apenas o hash

        [Required]
        public Guid UserId { get; set; } // 🔹 Alterado para Guid, pois User.Id é Guid

        [ForeignKey(nameof(UserId))] // Garante que o EF entende a relação corretamente
        public User User { get; set; } = null!;

        public DateTime ExpiryDate { get; set; }

        public bool IsRevoked { get; set; } = false;

        public string DeviceInfo { get; set; } = string.Empty; // 🔹 Identifica o dispositivo
    }
}