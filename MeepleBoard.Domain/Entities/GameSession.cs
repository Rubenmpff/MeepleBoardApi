using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    public class GameSession
    {
        [Key]
        public Guid Id { get; private set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; private set; } = string.Empty;

        [Required]
        public Guid OrganizerId { get; private set; }   // <- Guid em vez de string

        [MaxLength(200)]
        public string? Location { get; private set; }

        public DateTime StartDate { get; private set; }
        public DateTime? EndDate { get; private set; }
        public bool IsActive { get; private set; }

        public virtual ICollection<GameSessionPlayer> Players { get; private set; } = new HashSet<GameSessionPlayer>();
        public virtual ICollection<Match> Matches { get; private set; } = new HashSet<Match>();

        // Construtor privado usado pelo EF
        private GameSession()
        {
            Id = Guid.NewGuid();
            StartDate = DateTime.UtcNow;
            IsActive = true;
        }

        // Construtor público para criar novas sessões
        public GameSession(string name, Guid organizerId, string? location = null)
            : this()
        {
            Name = ValidateNotEmpty(name, "O nome da sessão é obrigatório.");
            OrganizerId = organizerId != Guid.Empty ? organizerId : throw new ArgumentException("OrganizerId inválido.");
            Location = location;
        }

        public void CloseSession()
        {
            if (!IsActive) return;

            IsActive = false;
            EndDate = DateTime.UtcNow;
        }

        private static string ValidateNotEmpty(string value, string errorMessage)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException(errorMessage)
                : value;
        }
    }
}
