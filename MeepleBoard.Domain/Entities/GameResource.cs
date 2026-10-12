using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// External link about a game: rules, videos, 3D models, unboxings or other material.
    /// Files are never hosted here; the URL points at the original page.
    ///
    /// Suggestions start Pending and only Approved resources are public.
    /// Language is an ISO 639-1 code, or null when the resource does not depend on a language (e.g. a 3D model).
    /// IsFree is null unless the price was confirmed.
    /// </summary>
    public class GameResource
    {
        public static readonly string[] Kinds = ["Rules", "HowToPlay", "Playthrough", "PrintModel", "Unboxing", "Material"];
        public static readonly string[] PrintCategories = ["Organizer", "Accessory", "ReplacementPart", "Decorative"];
        public const string Pending = "Pending", Approved = "Approved", Rejected = "Rejected";

        [Key]
        public Guid Id { get; private set; }
        public Guid GameId { get; private set; }
        public virtual Game Game { get; private set; } = null!;

        [Required, MaxLength(16)] public string Kind { get; private set; } = string.Empty;
        [MaxLength(16)] public string? PrintCategory { get; private set; }
        [MaxLength(2)] public string? Language { get; private set; }
        [Required, MaxLength(200)] public string Title { get; private set; } = string.Empty;
        [MaxLength(120)] public string? Author { get; private set; }
        [Required, MaxLength(60)] public string Platform { get; private set; } = string.Empty;
        [Required, MaxLength(500)] public string Url { get; private set; } = string.Empty;
        /// <summary>Edition or expansions it fits, when known (free text).</summary>
        [MaxLength(200)] public string? Compatibility { get; private set; }
        /// <summary>Provided by the publisher (rules, official videos).</summary>
        public bool IsOfficial { get; private set; }
        /// <summary>True free, false paid, null not confirmed.</summary>
        public bool? IsFree { get; private set; }

        [Required, MaxLength(16)] public string Status { get; private set; } = Pending;
        public Guid? SuggestedById { get; private set; }
        public virtual User? SuggestedBy { get; private set; }
        public Guid? ReviewedById { get; private set; }
        public DateTime? ReviewedAt { get; private set; }
        [MaxLength(300)] public string? ReviewNote { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }

        public virtual ICollection<GameResourceReport> Reports { get; private set; } = new HashSet<GameResourceReport>();

        private GameResource() { }

        /// <summary>A user suggestion (Pending) or an administrator entry (Approved immediately).</summary>
        public GameResource(Guid gameId, GameResourceDetails details, Guid authorId, bool byAdministrator)
        {
            if (gameId == Guid.Empty) throw new ArgumentException("Jogo inválido.");
            if (authorId == Guid.Empty) throw new ArgumentException("Utilizador inválido.");
            Id = Guid.NewGuid();
            GameId = gameId;
            CreatedAt = DateTime.UtcNow;
            Apply(details);
            if (byAdministrator) { Status = Approved; ReviewedById = authorId; ReviewedAt = CreatedAt; }
            else SuggestedById = authorId;
        }

        public void Update(GameResourceDetails details) { Apply(details); UpdatedAt = DateTime.UtcNow; }

        public void Approve(Guid administratorId) => Review(administratorId, Approved, null);

        public void Reject(Guid administratorId, string? note) => Review(administratorId, Rejected, Optional(note, 300));

        private void Review(Guid administratorId, string status, string? note)
        {
            if (administratorId == Guid.Empty) throw new ArgumentException("Administrador inválido.");
            Status = status; ReviewNote = note; ReviewedById = administratorId; ReviewedAt = DateTime.UtcNow;
        }

        private void Apply(GameResourceDetails d)
        {
            if (!Kinds.Contains(d.Kind)) throw new ArgumentException("Tipo de recurso inválido.");
            if (d.Kind == "PrintModel" ? !PrintCategories.Contains(d.PrintCategory ?? "") : d.PrintCategory != null)
                throw new ArgumentException("Os modelos 3D precisam de uma categoria; os outros recursos não.");
            var language = string.IsNullOrWhiteSpace(d.Language) ? null : d.Language.Trim().ToLowerInvariant();
            if (language != null && !Regex.IsMatch(language, "^[a-z]{2}$")) throw new ArgumentException("Idioma inválido: usa o código de duas letras (pt, en…).");
            Kind = d.Kind;
            PrintCategory = d.PrintCategory;
            Language = language;
            Title = Required(d.Title, 200, "título");
            Author = Optional(d.Author, 120);
            Platform = Required(d.Platform, 60, "plataforma");
            Url = ValidUrl(d.Url);
            Compatibility = Optional(d.Compatibility, 200);
            IsOfficial = d.IsOfficial;
            IsFree = d.IsFree;
        }

        public static string ValidUrl(string? value)
        {
            var text = value?.Trim() ?? "";
            if (text.Length > 500 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host))
                throw new ArgumentException("Indica um endereço https completo, até 500 caracteres.");
            return uri.ToString();
        }

        private static string Required(string? value, int max, string field)
        {
            var text = value?.Trim() ?? "";
            if (text.Length == 0 || text.Length > max) throw new ArgumentException($"O {field} é obrigatório (até {max} caracteres).");
            return text;
        }

        private static string? Optional(string? value, int max)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (text.Length > max) throw new ArgumentException($"Usa no máximo {max} caracteres.");
            return text;
        }
    }

    public sealed record GameResourceDetails(string Kind, string? PrintCategory, string? Language, string Title, string? Author,
        string Platform, string Url, string? Compatibility, bool IsOfficial, bool? IsFree);
}
