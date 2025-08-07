using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    public class Match
    {
        private Match()
        {
            MatchPlayers = new HashSet<MatchPlayer>();
        }

        public Match(Guid gameId, DateTime matchDate, Guid? gameSessionId = null)
        {
            if (gameId == Guid.Empty)
                throw new ArgumentException("ID do jogo inválido.");

            if (matchDate > DateTime.UtcNow)
                throw new ArgumentException("A data da partida não pode estar no futuro.");

            Id = Guid.NewGuid();
            GameId = gameId;
            GameSessionId = gameSessionId;
            MatchDate = matchDate;
            CreatedAt = DateTime.UtcNow;
            MatchPlayers = new HashSet<MatchPlayer>();
        }

        [Key]
        public Guid Id { get; private set; }

        [Required]
        public DateTime MatchDate { get; private set; }

        [Required]
        public Guid GameId { get; private set; }

        [ForeignKey("GameId")]
        public virtual Game? Game { get; private set; }

        /// <summary>
        /// Se esta partida pertence a uma sessão de jogo
        /// </summary>
        public Guid? GameSessionId { get; private set; }

        [ForeignKey("GameSessionId")]
        public virtual GameSession? GameSession { get; private set; }

        public bool IsSoloGame { get; private set; }

        public Guid? WinnerId { get; private set; }

        [ForeignKey("WinnerId")]
        public virtual User? Winner { get; private set; }

        public virtual ICollection<MatchPlayer> MatchPlayers { get; private set; }

        private int? _durationInMinutes;
        public int? DurationInMinutes
        {
            get => _durationInMinutes;
            private set
            {
                if (value < 0)
                    throw new ArgumentException("A duração da partida não pode ser negativa.");
                _durationInMinutes = value;
                UpdateTimestamp();
            }
        }

        [MaxLength(200)]
        public string? Location { get; private set; }

        [MaxLength(500)]
        public string? ScoreSummary { get; private set; }

        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }

        // --- Métodos de atualização ---
        public void UpdateMatchDetails(string? location, string? scoreSummary, int? duration)
        {
            if (Location != location || ScoreSummary != scoreSummary || DurationInMinutes != duration)
            {
                Location = location;
                ScoreSummary = scoreSummary;
                DurationInMinutes = duration;
                UpdateTimestamp();
            }
        }

        public void SetWinner(Guid? winnerId)
        {
            if (winnerId == Guid.Empty)
                throw new ArgumentException("ID do vencedor inválido.");

            if (WinnerId != winnerId)
            {
                WinnerId = winnerId;
                UpdateTimestamp();
            }
        }

        public void SetSoloGame(bool isSolo)
        {
            if (IsSoloGame != isSolo)
            {
                IsSoloGame = isSolo;
                UpdateTimestamp();
            }
        }

        public void SetMatchDate(DateTime matchDate)
        {
            if (matchDate > DateTime.UtcNow)
                throw new ArgumentException("A data da partida não pode estar no futuro.");

            if (MatchDate != matchDate)
            {
                MatchDate = matchDate;
                UpdateTimestamp();
            }
        }

        public void SetDuration(int? duration) => DurationInMinutes = duration;

        public void SetLocation(string? location)
        {
            if (Location != location)
            {
                Location = location;
                UpdateTimestamp();
            }
        }

        public void SetGameId(Guid gameId)
        {
            if (gameId == Guid.Empty)
                throw new ArgumentException("ID do jogo inválido.");

            if (GameId != gameId)
            {
                GameId = gameId;
                UpdateTimestamp();
            }
        }

        public void SetGameSession(Guid? sessionId)
        {
            if (sessionId.HasValue && sessionId == Guid.Empty)
                throw new ArgumentException("ID da sessão inválido.");

            if (GameSessionId != sessionId)
            {
                GameSessionId = sessionId;
                UpdateTimestamp();
            }
        }

        private void UpdateTimestamp() => UpdatedAt = DateTime.UtcNow;
    }
}
