using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    public class GameSession
    {
        [Key]
        public Guid Id { get; private set; }

        [Required, MaxLength(200)]
        public string Name { get; private set; } = string.Empty;

        [Required]
        public Guid OrganizerId { get; private set; }
        public virtual User Organizer { get; private set; } = null!;


        [MaxLength(200)]
        public string? Location { get; private set; }

        public DateTime StartDate { get; private set; } = DateTime.UtcNow;
        public DateTime? EndDate { get; private set; }
        public bool IsActive { get; private set; } = true;

        public virtual ICollection<GameSessionPlayer> Players { get; private set; } = new HashSet<GameSessionPlayer>();
        public virtual ICollection<Match> Matches { get; private set; } = new HashSet<Match>();

        // 🔒 EF Core usa este construtor privado para materialização
        private GameSession() { }

        // 🧩 Construtor público para criar novas sessões
        public GameSession(string name, Guid organizerId, string? location = null)
        {
            if (organizerId == Guid.Empty)
                throw new ArgumentException("OrganizerId inválido.", nameof(organizerId));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("O nome da sessão é obrigatório.", nameof(name));

            Id = Guid.NewGuid();
            Name = name.Trim();
            OrganizerId = organizerId;
            Location = location?.Trim();
            StartDate = DateTime.UtcNow;
            IsActive = true;
        }

        // ✅ Método para fechar a sessão
        public void CloseSession()
        {
            if (!IsActive)
                return;

            IsActive = false;
            EndDate = DateTime.UtcNow;
        }

        // 🧼 Método auxiliar para alterar dados (futuro)
        public void UpdateLocation(string? location)
        {
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        }
    }
}
