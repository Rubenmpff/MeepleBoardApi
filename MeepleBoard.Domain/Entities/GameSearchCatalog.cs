using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Representa um jogo no catálogo leve utilizado apenas para pesquisa.
    ///
    /// Esta entidade NÃO representa um jogo efetivamente utilizado na MeepleBoard.
    /// Não possui partidas, bibliotecas, campanhas ou outras relações de domínio.
    ///
    /// Pode ser apagada e reconstruída a partir do BGG sem afetar os dados
    /// reais dos utilizadores.
    /// </summary>
    public class GameSearchCatalog
    {
        // Necessário para o Entity Framework.
        private GameSearchCatalog()
        {
            Name = string.Empty;
            NormalizedName = string.Empty;
            ThumbnailUrl = string.Empty;
        }

        public GameSearchCatalog(
            int bggId,
            string name,
            string normalizedName,
            int? yearPublished,
            string? thumbnailUrl,
            bool isExpansion,
            int? minPlayers,
            int? maxPlayers,
            bool isCooperative,
            bool supportsCampaign,
            double? averageRating,
            int? ratingsCount,
            int? bggRank)
        {
            Id = Guid.NewGuid();

            Name = string.Empty;
            NormalizedName = string.Empty;
            ThumbnailUrl = string.Empty;

            SetBggId(bggId);
            SetName(name);
            SetNormalizedName(normalizedName);

            YearPublished = yearPublished;
            ThumbnailUrl = thumbnailUrl ?? string.Empty;

            IsExpansion = isExpansion;

            MinPlayers = minPlayers;
            MaxPlayers = maxPlayers;

            IsCooperative = isCooperative;
            SupportsCampaign = supportsCampaign;

            AverageRating = averageRating;
            RatingsCount = ratingsCount;
            BggRank = bggRank;

            CreatedAt = DateTime.UtcNow;
            LastSyncedAt = DateTime.UtcNow;

            // O registo pode ter sido criado apenas com dados básicos,
            // por exemplo através do CSV oficial do BGG.
            DetailsSyncedAt = null;
        }

        [Key]
        public Guid Id { get; private set; }

        /// <summary>
        /// ID oficial do jogo no BoardGameGeek.
        /// </summary>
        public int BggId { get; private set; }

        /// <summary>
        /// Nome original do jogo.
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string Name { get; private set; }

        /// <summary>
        /// Nome normalizado utilizado para pesquisa.
        ///
        /// Exemplo:
        /// "Pokémon Café" -> "pokemon cafe"
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string NormalizedName { get; private set; }

        public int? YearPublished { get; private set; }

        /// <summary>
        /// URL da thumbnail.
        /// A imagem não é armazenada na base de dados.
        /// </summary>
        [MaxLength(1000)]
        public string ThumbnailUrl { get; private set; }

        /// <summary>
        /// Indica se este registo representa uma expansão.
        /// </summary>
        public bool IsExpansion { get; private set; }

        /// <summary>
        /// Número mínimo de jogadores.
        /// </summary>
        public int? MinPlayers { get; private set; }

        /// <summary>
        /// Número máximo de jogadores.
        /// </summary>
        public int? MaxPlayers { get; private set; }

        /// <summary>
        /// O suporte a solo pode ser determinado diretamente
        /// através do número mínimo de jogadores.
        /// </summary>
        public bool SupportsSoloMode =>
            MinPlayers.HasValue &&
            MinPlayers.Value == 1;

        /// <summary>
        /// Indica se o jogo é cooperativo.
        /// Este valor é obtido a partir dos dados do BGG.
        /// </summary>
        public bool IsCooperative { get; private set; }

        /// <summary>
        /// Indica se o jogo suporta campanha.
        /// Este valor é obtido através da lógica já utilizada
        /// pela integração com o BGG.
        /// </summary>
        public bool SupportsCampaign { get; private set; }

        /// <summary>
        /// Nota média atual do BGG.
        /// </summary>
        public double? AverageRating { get; private set; }

        /// <summary>
        /// Número de avaliações no BGG.
        /// Utilizado como indicador de popularidade.
        /// </summary>
        public int? RatingsCount { get; private set; }

        /// <summary>
        /// Ranking geral do jogo no BGG, quando disponível.
        /// </summary>
        public int? BggRank { get; private set; }

        /// <summary>
        /// Momento em que este registo foi criado no catálogo.
        /// </summary>
        public DateTime CreatedAt { get; private set; }

        /// <summary>
        /// Última sincronização em que os dados básicos
        /// deste registo sofreram uma alteração.
        ///
        /// Importar novamente exatamente os mesmos dados
        /// não altera este valor.
        /// </summary>
        public DateTime LastSyncedAt { get; private set; }

        /// <summary>
        /// Momento em que os detalhes completos deste jogo foram
        /// enriquecidos pela última vez através do BGG /thing.
        ///
        /// Null significa que o registo pode existir apenas com
        /// os dados básicos do catálogo.
        /// </summary>
        public DateTime? DetailsSyncedAt { get; private set; }

        /// <summary>
        /// Atualiza os dados básicos provenientes do catálogo BGG.
        ///
        /// Só modifica a entidade quando pelo menos um dos valores
        /// realmente mudou. Isto evita milhares de UPDATEs
        /// desnecessários ao importar novamente o mesmo CSV.
        ///
        /// Não altera DetailsSyncedAt porque uma importação do CSV
        /// não corresponde a um refresh dos detalhes via /thing.
        /// </summary>
        public void UpdateCatalogData(
            string name,
            string normalizedName,
            int? yearPublished,
            bool isExpansion,
            double? averageRating,
            int? ratingsCount,
            int? bggRank)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "O nome do jogo é obrigatório.",
                    nameof(name));
            }

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                throw new ArgumentException(
                    "O nome normalizado do jogo é obrigatório.",
                    nameof(normalizedName));
            }

            var cleanName =
                name.Trim();

            var cleanNormalizedName =
                normalizedName.Trim();

            var changed =
                !string.Equals(
                    Name,
                    cleanName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    NormalizedName,
                    cleanNormalizedName,
                    StringComparison.Ordinal) ||
                YearPublished != yearPublished ||
                IsExpansion != isExpansion ||
                AverageRating != averageRating ||
                RatingsCount != ratingsCount ||
                BggRank != bggRank;

            if (!changed)
            {
                return;
            }

            Name = cleanName;
            NormalizedName = cleanNormalizedName;

            YearPublished = yearPublished;
            IsExpansion = isExpansion;

            AverageRating = averageRating;
            RatingsCount = ratingsCount;
            BggRank = bggRank;

            LastSyncedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Atualiza os dados detalhados do catálogo com informação
        /// obtida através do BGG /thing.
        ///
        /// Esta operação também marca DetailsSyncedAt.
        /// </summary>
        public void UpdateFromBgg(
            string name,
            string normalizedName,
            int? yearPublished,
            string? thumbnailUrl,
            bool isExpansion,
            int? minPlayers,
            int? maxPlayers,
            bool isCooperative,
            bool supportsCampaign,
            double? averageRating,
            int? ratingsCount,
            int? bggRank)
        {
            SetName(name);
            SetNormalizedName(normalizedName);

            YearPublished = yearPublished;
            ThumbnailUrl = thumbnailUrl ?? string.Empty;

            IsExpansion = isExpansion;

            MinPlayers = minPlayers;
            MaxPlayers = maxPlayers;

            IsCooperative = isCooperative;
            SupportsCampaign = supportsCampaign;

            AverageRating = averageRating;
            RatingsCount = ratingsCount;
            BggRank = bggRank;

            var now = DateTime.UtcNow;

            LastSyncedAt = now;
            DetailsSyncedAt = now;
        }

        private void SetBggId(int bggId)
        {
            if (bggId <= 0)
            {
                throw new ArgumentException(
                    "O BGG ID tem de ser maior que zero.",
                    nameof(bggId));
            }

            BggId = bggId;
        }

        private void SetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "O nome do jogo é obrigatório.",
                    nameof(name));
            }

            Name = name.Trim();
        }

        private void SetNormalizedName(string normalizedName)
        {
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                throw new ArgumentException(
                    "O nome normalizado do jogo é obrigatório.",
                    nameof(normalizedName));
            }

            NormalizedName = normalizedName.Trim();
        }
    }
}