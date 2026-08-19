using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Estado do diário de uma partida.
    /// Open  → ainda à espera que todos os jogadores avaliem
    /// Closed → fechado (manualmente, automaticamente após 1 dia, ou todos avaliaram)
    /// </summary>
    public enum MatchJournalStatus
    {
        Open = 0,
        Closed = 1
    }

    public class Match
    {
        private Match()
        {
            MatchPlayers = new HashSet<MatchPlayer>();
            CampaignMatches = new HashSet<CampaignMatch>();
            JournalEntries = new HashSet<MatchJournalEntry>();
        }

        public Match(Guid gameId, DateTime matchDate, Guid? gameSessionId = null)
        {
            if (gameId == Guid.Empty)
                throw new ArgumentException("ID do jogo inválido.");

            // ✅ Normaliza para UTC independentemente do fuso do cliente
            var utcDate = NormalizeToUtc(matchDate);

            // ✅ Margem de 1 minuto para latência de rede
            if (utcDate > DateTime.UtcNow.AddMinutes(1))
                throw new ArgumentException("A data da partida não pode estar no futuro.");

            Id = Guid.NewGuid();
            GameId = gameId;
            GameSessionId = gameSessionId;
            MatchDate = utcDate;
            IsOfficialMode = true;
            JournalStatus = MatchJournalStatus.Open;
            AutoCloseAt = DateTime.UtcNow.AddDays(1); // fecha automaticamente após 1 dia
            CreatedAt = DateTime.UtcNow;
            MatchPlayers = new HashSet<MatchPlayer>();
            CampaignMatches = new HashSet<CampaignMatch>();
            JournalEntries = new HashSet<MatchJournalEntry>();
        }

        [Key]
        public Guid Id { get; private set; }

        [Required]
        public DateTime MatchDate { get; private set; }

        [Required]
        public Guid GameId { get; private set; }

        [ForeignKey("GameId")]
        public virtual Game? Game { get; private set; }

        /// <summary>Se esta partida pertence a uma sessão de jogo.</summary>
        public Guid? GameSessionId { get; private set; }

        [ForeignKey("GameSessionId")]
        public virtual GameSession? GameSession { get; private set; }

        public bool IsSoloGame { get; private set; }

        public Guid? WinnerId { get; private set; }

        [ForeignKey("WinnerId")]
        public virtual User? Winner { get; private set; }

        public virtual ICollection<MatchPlayer> MatchPlayers { get; private set; }

        /// <summary>Associações desta partida a campanhas.</summary>
        public virtual ICollection<CampaignMatch> CampaignMatches { get; private set; }

        /// <summary>Entradas de diário dos jogadores para esta partida.</summary>
        public virtual ICollection<MatchJournalEntry> JournalEntries { get; private set; }

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

        // ── Diário de partida ────────────────────────────────────────────────

        /// <summary>
        /// Avaliação pessoal do utilizador que registou esta partida (0–10).
        /// A média de todas as partidas oficiais dá a avaliação pessoal do jogo.
        /// </summary>
        [Range(0, 10)]
        public double? PersonalRating { get; private set; }

        /// <summary>Notas livres — momentos épicos, estratégias, história.</summary>
        [MaxLength(2000)]
        public string? Notes { get; private set; }

        /// <summary>Tags separadas por vírgula (ex: "épico,reviravolta,campanha").</summary>
        [MaxLength(500)]
        public string? Tags { get; private set; }

        // ── Estado do diário ─────────────────────────────────────────────────

        /// <summary>
        /// Estado do diário desta partida.
        /// Open   → à espera que todos os jogadores avaliem
        /// Closed → fechado (manual, automático após 1 dia, ou todos avaliaram)
        ///
        /// Após fechar, jogadores pendentes ainda podem avaliar —
        /// as suas entradas ficam visíveis quando submetem.
        /// </summary>
        public MatchJournalStatus JournalStatus { get; private set; } = MatchJournalStatus.Open;

        /// <summary>Data em que o diário foi fechado.</summary>
        public DateTime? ClosedAt { get; private set; }

        /// <summary>
        /// Data em que o sistema fecha automaticamente o diário.
        /// Default: 1 dia após a criação da partida.
        /// </summary>
        public DateTime AutoCloseAt { get; private set; }

        // ── Modo oficial / não oficial ───────────────────────────────────────

        /// <summary>
        /// True se o modo de jogo é oficial (confirmado pelo BGG).
        /// Partidas não oficiais não contam para rankings nem ratings.
        /// </summary>
        public bool IsOfficialMode { get; private set; } = true;

        /// <summary>
        /// Justificação para usar um modo não oficial.
        /// Apenas preenchido quando IsOfficialMode = false.
        /// </summary>
        [MaxLength(500)]
        public string? UnofficialModeJustification { get; private set; }

        // ────────────────────────────────────────────────────────────────────

        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }

        // ── Propriedade calculada ────────────────────────────────────────────

        /// <summary>
        /// True se o sistema deve fechar o diário automaticamente.
        /// Usado pelo MatchCleanupJob.
        /// </summary>
        [NotMapped]
        public bool ShouldAutoCloseNow =>
            JournalStatus == MatchJournalStatus.Open &&
            DateTime.UtcNow >= AutoCloseAt;

        // ── Métodos ───────────────────────────────────────────────────────────

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

        /// <summary>
        /// Define os campos do diário do jogador que criou a partida.
        /// Rating de 0 a 10 (compatível com escala BGG).
        /// </summary>
        public void SetJournalData(double? personalRating, string? notes, string? tags)
        {
            if (personalRating.HasValue && (personalRating < 0 || personalRating > 10))
                throw new ArgumentException("O rating pessoal deve estar entre 0 e 10.");

            PersonalRating = personalRating;
            Notes = notes?.Trim();
            Tags = tags?.Trim();
            UpdateTimestamp();
        }

        /// <summary>
        /// Fecha o diário da partida manualmente (pelo criador ou sistema).
        /// Após fechar, jogadores pendentes ainda podem avaliar.
        /// </summary>
        public void CloseJournal()
        {
            if (JournalStatus == MatchJournalStatus.Closed) return;
            JournalStatus = MatchJournalStatus.Closed;
            ClosedAt = DateTime.UtcNow;
            UpdateTimestamp();
        }

        /// <summary>
        /// Verifica se todos os jogadores avaliaram e fecha automaticamente se sim.
        /// Deve ser chamado após cada nova entrada de diário.
        /// </summary>
        public void TryAutoCloseIfAllEvaluated()
        {
            if (JournalStatus == MatchJournalStatus.Closed) return;

            var playerIds = MatchPlayers.Select(p => p.UserId).ToHashSet();
            var evaluatedIds = JournalEntries
                .Where(e => e.PersonalRating.HasValue)
                .Select(e => e.UserId)
                .ToHashSet();

            if (playerIds.Count > 0 && playerIds.All(id => evaluatedIds.Contains(id)))
                CloseJournal();
        }

        /// <summary>
        /// Define se o modo de jogo é oficial ou não.
        /// Partidas não oficiais não contam para rankings.
        /// </summary>
        public void SetOfficialMode(bool isOfficial, string? justification = null)
        {
            if (!isOfficial && string.IsNullOrWhiteSpace(justification))
                throw new ArgumentException("É necessária uma justificação para modo não oficial.");

            IsOfficialMode = isOfficial;
            UnofficialModeJustification = isOfficial ? null : justification?.Trim();
            UpdateTimestamp();
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
            // ✅ Normaliza para UTC e permite 1 minuto de margem para latência
            var utcDate = NormalizeToUtc(matchDate);
            if (utcDate > DateTime.UtcNow.AddMinutes(1))
                throw new ArgumentException("A data da partida não pode estar no futuro.");

            if (MatchDate != utcDate)
            {
                MatchDate = utcDate;
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

        // ── Helpers privados ─────────────────────────────────────────────────

        /// <summary>
        /// Garante que qualquer DateTime recebido do cliente é tratado como UTC.
        ///
        /// Regras:
        ///   - Kind=Utc       → devolve sem alteração
        ///   - Kind=Local     → converte para UTC com ToUniversalTime()
        ///   - Kind=Unspecified → assume UTC (padrão do JSON/ISO 8601 com sufixo Z)
        ///
        /// Isto torna a app timezone-safe para qualquer país ou fuso horário.
        /// </summary>
        private static DateTime NormalizeToUtc(DateTime dt) => dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            _ => dt
        };

        private void UpdateTimestamp() => UpdatedAt = DateTime.UtcNow;
    }
}