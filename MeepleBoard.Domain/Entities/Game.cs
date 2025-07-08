using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MeepleBoard.Domain.Entities
{
    public class Game
    {
        private Game()
        {
            Name = string.Empty;
            Description = string.Empty;
            ImageUrl = string.Empty;
            Matches = new HashSet<Match>();
            Expansions = new HashSet<Game>();
            UserGameLibraries = new HashSet<UserGameLibrary>();
        }

        public Game(string name, string description, string imageUrl, bool supportsSoloMode = false)
        {
            Id = Guid.NewGuid();
            Name = ValidateNotEmpty(name, "O nome do jogo é obrigatório.");
            Description = ValidateNotEmpty(description, "A descrição do jogo é obrigatória.");
            ImageUrl = ValidateNotEmpty(imageUrl, "A URL da imagem é obrigatória.");
            SupportsSoloMode = supportsSoloMode;
            CreatedAt = DateTime.UtcNow;
            Matches = new HashSet<Match>();
            Expansions = new HashSet<Game>();
            UserGameLibraries = new HashSet<UserGameLibrary>();
        }

        [Key]
        public Guid Id { get; private set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; private set; }

        [MaxLength(150)]
        public string? AlternateName { get; private set; }

        [Required]
        [MaxLength(10000)]
        public string Description { get; private set; }

        [Required]
        public string ImageUrl { get; private set; }

        /// <summary>
        /// Ano de publicação original do jogo (caso disponível no BGG).
        /// </summary>
        [Range(1000, 2100)]
        public int? YearPublished { get; private set; }

        public int? BGGId { get; private set; }

        public int? BggRanking { get; private set; }

        public bool IsApproved { get; private set; } = false;

        public bool SupportsSoloMode { get; private set; } = false;

        public double? AverageRating { get; private set; }

        public int? MeepleBoardScore { get; private set; }

        public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; private set; }

        // Expansões vinculadas (EF irá preencher com jogos que referenciem este como base)
        public virtual ICollection<Game> Expansions { get; private set; }

        public virtual ICollection<Match> Matches { get; private set; }

        [JsonIgnore]
        public virtual ICollection<UserGameLibrary> UserGameLibraries { get; private set; }

        public Guid? BaseGameId { get; private set; }

        [ForeignKey("BaseGameId")]
        public virtual Game? BaseGame { get; private set; }

        public int? BaseGameBggId { get; private set; }

        [NotMapped]
        public bool IsExpansion => BaseGameId.HasValue || BaseGameBggId.HasValue;

        public void UpdateDetails(string name, string description, string imageUrl, bool supportsSoloMode)
        {
            bool hasChanges = Name != name || Description != description || ImageUrl != imageUrl || SupportsSoloMode != supportsSoloMode;

            if (hasChanges)
            {
                Name = ValidateNotEmpty(name, "O nome do jogo não pode estar vazio.");
                Description = ValidateNotEmpty(description, "A descrição do jogo não pode estar vazia.");
                ImageUrl = ValidateNotEmpty(imageUrl, "A URL da imagem não pode estar vazia.");
                SupportsSoloMode = supportsSoloMode;
                SetUpdatedAt();
            }
        }

        public void ApproveGame()
        {
            if (!IsApproved)
            {
                IsApproved = true;
                SetUpdatedAt();
            }
        }

        public void SetBggId(int? bggId)
        {
            if (bggId.HasValue && bggId < 0)
                throw new ArgumentException("BGGId não pode ser negativo.");

            if (BGGId != bggId)
            {
                BGGId = bggId;
                SetUpdatedAt();
            }
        }

        public void SetBggRanking(int? ranking)
        {
            if (ranking.HasValue && ranking < 0)
                throw new ArgumentException("Ranking do BGG não pode ser negativo.");

            if (BggRanking != ranking)
            {
                BggRanking = ranking;
                SetUpdatedAt();
            }
        }

        public void SetAverageRating(double? rating)
        {
            if (rating.HasValue && (rating < 0 || rating > 10))
                throw new ArgumentException("A nota deve estar entre 0 e 10.");

            if (AverageRating != rating)
            {
                AverageRating = rating;
                SetUpdatedAt();
            }
        }

        public void SetMeepleBoardScore(int? score)
        {
            if (score.HasValue && (score < 0 || score > 100))
                throw new ArgumentException("O score deve estar entre 0 e 100.");

            if (MeepleBoardScore != score)
            {
                MeepleBoardScore = score;
                SetUpdatedAt();
            }
        }

        public void SetBaseGame(Game baseGame)
        {
            if (baseGame == null)
                throw new ArgumentNullException(nameof(baseGame));

            if (baseGame.Id == Id)
                throw new ArgumentException("O jogo base não pode ser o próprio jogo.");

            BaseGame = baseGame;
            BaseGameId = baseGame.Id;
            SetUpdatedAt();
        }

        public void SetBaseGameBggId(int? baseGameBggId)
        {
            if (baseGameBggId.HasValue && baseGameBggId <= 0)
                throw new ArgumentException("O BGG ID do jogo base deve ser positivo.");

            if (BaseGameBggId != baseGameBggId)
            {
                BaseGameBggId = baseGameBggId;
                SetUpdatedAt();
            }
        }

        public void UpdateBggStats(string? description, string? imageUrl, int? bggRanking, double? averageRating, int? yearPublished = null)
        {
            bool updated = false;

            if (!string.IsNullOrWhiteSpace(description) && Description != description)
            {
                Description = description;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(imageUrl) && ImageUrl != imageUrl)
            {
                ImageUrl = imageUrl;
                updated = true;
            }

            if (BggRanking != bggRanking)
            {
                BggRanking = bggRanking;
                updated = true;
            }

            if (AverageRating != averageRating)
            {
                AverageRating = averageRating;
                updated = true;
            }

            if (YearPublished != yearPublished)
            {
                YearPublished = yearPublished;
                updated = true;
            }

            if (updated)
                SetUpdatedAt();
        }

        private void SetUpdatedAt() => UpdatedAt = DateTime.UtcNow;

        private static string ValidateNotEmpty(string value, string errorMessage)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException(errorMessage)
                : value;
        }
    }
}