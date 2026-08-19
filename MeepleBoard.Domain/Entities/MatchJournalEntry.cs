using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Entrada de diário de um jogador para uma partida específica.
    ///
    /// Cada jogador que participou numa partida pode adicionar
    /// a sua perspetiva: notas, rating pessoal, tags e fotos.
    ///
    /// Exemplo:
    ///   Partida: Gloomhaven Cenário 3
    ///   ├── Ruben:  "Épico! Ganhei no último turno" 9/10
    ///   ├── Miguel: "Quase morri 3 vezes"           7/10
    ///   └── João:   "Da próxima trago melhor build"  6/10
    ///
    /// Regras:
    ///   - Um jogador só pode ter uma entrada por partida
    ///   - Só jogadores que participaram na partida podem adicionar entrada
    ///   - Fotos são premium (PhotoUrls fica vazio no plano gratuito)
    ///   - Escala do rating: 0–10, igual à do Match.PersonalRating e ao
    ///     resto da app (RegisterMatchForm, MatchJournalScreen,
    ///     CreateCampaignEncounterScreen mostram todos "Rating (0–10)").
    /// </summary>
    public class MatchJournalEntry
    {
        private MatchJournalEntry() { }

        public MatchJournalEntry(Guid matchId, Guid userId)
        {
            if (matchId == Guid.Empty)
                throw new ArgumentException("ID da partida inválido.");
            if (userId == Guid.Empty)
                throw new ArgumentException("ID do utilizador inválido.");

            Id = Guid.NewGuid();
            MatchId = matchId;
            UserId = userId;
            CreatedAt = DateTime.UtcNow;
        }

        [Key]
        public Guid Id { get; private set; }

        [Required]
        public Guid MatchId { get; private set; }

        [ForeignKey("MatchId")]
        public virtual Match? Match { get; private set; }

        [Required]
        public Guid UserId { get; private set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; private set; }

        /// <summary>Avaliação pessoal desta partida (0–10, igual ao resto da app).</summary>
        [Range(0, 10)]
        public int? PersonalRating { get; private set; }

        /// <summary>Notas livres — momentos épicos, estratégias, história.</summary>
        [MaxLength(3000)]
        public string? Notes { get; private set; }

        /// <summary>Tags separadas por vírgula (ex: "épico,reviravolta,campanha").</summary>
        [MaxLength(500)]
        public string? Tags { get; private set; }

        /// <summary>
        /// URLs das fotos (premium).
        /// Guardado como JSON serializado ou separado por pipe.
        /// No plano gratuito fica vazio.
        /// </summary>
        [MaxLength(5000)]
        public string? PhotoUrlsJson { get; private set; }

        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }

        // ── Propriedade calculada ─────────────────────────────────────────────

        [NotMapped]
        public List<string> PhotoUrls
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PhotoUrlsJson)) return new();
                try
                {
                    return System.Text.Json.JsonSerializer
                        .Deserialize<List<string>>(PhotoUrlsJson) ?? new();
                }
                catch { return new(); }
            }
        }

        // ── Métodos ───────────────────────────────────────────────────────────

        public void Update(int? personalRating, string? notes, string? tags)
        {
            if (personalRating.HasValue && (personalRating < 0 || personalRating > 10))
                throw new ArgumentException("O rating deve estar entre 0 e 10.");

            PersonalRating = personalRating;
            Notes = notes?.Trim();
            Tags = tags?.Trim();
            Touch();
        }

        public void SetPhotoUrls(List<string> urls)
        {
            PhotoUrlsJson = urls?.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(urls)
                : null;
            Touch();
        }

        public void AddPhotoUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var current = PhotoUrls;
            current.Add(url.Trim());
            SetPhotoUrls(current);
        }

        public void RemovePhotoUrl(string url)
        {
            var current = PhotoUrls;
            current.Remove(url);
            SetPhotoUrls(current);
        }

        private void Touch() => UpdatedAt = DateTime.UtcNow;
    }
}