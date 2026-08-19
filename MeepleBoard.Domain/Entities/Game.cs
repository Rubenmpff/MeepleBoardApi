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

        // ✅ imageUrl agora opcional — alguns jogos no BGG não têm imagem
        public Game(string name, string description, string? imageUrl, bool supportsSoloMode = false)
        {
            Id = Guid.NewGuid();
            Name = ValidateNotEmpty(name, "O nome do jogo é obrigatório.");
            Description = description ?? string.Empty; // descrição pode ser vazia
            ImageUrl = imageUrl ?? string.Empty;        // imagem pode ser nula
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

        // ✅ Não é mais [Required] — pode estar vazio se o BGG não tiver imagem
        public string ImageUrl { get; private set; }

        [Range(1000, 2100)]
        public int? YearPublished { get; private set; }

        public int? BGGId { get; private set; }

        public int? BggRanking { get; private set; }

        public bool IsApproved { get; private set; } = false;

        public bool SupportsSoloMode { get; private set; } = false;

        public double? AverageRating { get; private set; }

        /// <summary>
        /// Número de pessoas que avaliaram o jogo no BGG ("usersrated") — a melhor
        /// medida de "quão conhecido" o jogo é (melhor que a nota média, que pode
        /// favorecer jogos obscuros com poucos votos muito altos).
        /// </summary>
        public int? UsersRatedCount { get; private set; }

        public int? MeepleBoardScore { get; private set; }

        public int? MinPlayers { get; private set; }

        public int? MaxPlayers { get; private set; }

        public bool IsCooperative { get; private set; } = false;

        // ✅ Detetado a partir do BGG (mecânica "Legacy Game"/"Campaign / Battle Card Driven"
        // ou categoria "Campaign Games"). Usado para avisar o utilizador ao criar uma
        // campanha com um jogo que normalmente não é jogado em campanha.
        public bool SupportsCampaign { get; private set; } = false;

        public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; private set; }

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

        // ─── Métodos de atualização ───────────────────────────────────────────

        // ✅ imageUrl agora opcional
        public void UpdateDetails(string name, string description, string? imageUrl, bool supportsSoloMode)
        {
            bool hasChanges = Name != name || Description != description
                || ImageUrl != (imageUrl ?? string.Empty) || SupportsSoloMode != supportsSoloMode;

            if (hasChanges)
            {
                Name = ValidateNotEmpty(name, "O nome do jogo não pode estar vazio.");
                Description = description ?? string.Empty;
                ImageUrl = imageUrl ?? string.Empty;
                SupportsSoloMode = supportsSoloMode;
                SetUpdatedAt();
            }
        }

        public void SetPlayerCount(int? minPlayers, int? maxPlayers)
        {
            if (minPlayers.HasValue && minPlayers < 0)
                throw new ArgumentException("O número mínimo de jogadores não pode ser negativo.");

            if (maxPlayers.HasValue && maxPlayers < 0)
                throw new ArgumentException("O número máximo de jogadores não pode ser negativo.");

            if (minPlayers.HasValue && maxPlayers.HasValue && minPlayers > maxPlayers)
                throw new ArgumentException("O número mínimo de jogadores não pode ser maior que o máximo.");

            bool changed = MinPlayers != minPlayers || MaxPlayers != maxPlayers;
            if (changed)
            {
                MinPlayers = minPlayers;
                MaxPlayers = maxPlayers;

                if (minPlayers.HasValue && minPlayers == 1)
                    SupportsSoloMode = true;

                SetUpdatedAt();
            }
        }

        public void SetCooperative(bool isCooperative)
        {
            if (IsCooperative != isCooperative)
            {
                IsCooperative = isCooperative;
                SetUpdatedAt();
            }
        }

        public void SetSupportsCampaign(bool supportsCampaign)
        {
            if (SupportsCampaign != supportsCampaign)
            {
                SupportsCampaign = supportsCampaign;
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

        public void SetUsersRatedCount(int? count)
        {
            if (count.HasValue && count < 0)
                throw new ArgumentException("O número de avaliações não pode ser negativo.");

            if (UsersRatedCount != count)
            {
                UsersRatedCount = count;
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

        public void UpdateBggStats(
            string? description,
            string? imageUrl,
            int? bggRanking,
            double? averageRating,
            int? yearPublished = null,
            int? minPlayers = null,
            int? maxPlayers = null,
            bool? isCooperative = null,
            bool? supportsCampaign = null,
            int? usersRatedCount = null)
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

            if (usersRatedCount.HasValue && UsersRatedCount != usersRatedCount)
            {
                UsersRatedCount = usersRatedCount;
                updated = true;
            }

            if (YearPublished != yearPublished)
            {
                YearPublished = yearPublished;
                updated = true;
            }

            if (MinPlayers != minPlayers)
            {
                MinPlayers = minPlayers;
                updated = true;
            }

            if (MaxPlayers != maxPlayers)
            {
                MaxPlayers = maxPlayers;
                updated = true;
            }

            if (isCooperative.HasValue && IsCooperative != isCooperative.Value)
            {
                IsCooperative = isCooperative.Value;
                updated = true;
            }

            if (supportsCampaign.HasValue && SupportsCampaign != supportsCampaign.Value)
            {
                SupportsCampaign = supportsCampaign.Value;
                updated = true;
            }

            if (minPlayers.HasValue && minPlayers == 1 && !SupportsSoloMode)
            {
                SupportsSoloMode = true;
                updated = true;
            }

            if (updated)
                SetUpdatedAt();
        }

        private void SetUpdatedAt() => UpdatedAt = DateTime.UtcNow;

        // ✅ Só valida que não é nulo/vazio — usado apenas para campos obrigatórios (Name)
        private static string ValidateNotEmpty(string value, string errorMessage)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException(errorMessage)
                : value;
        }
    }
}