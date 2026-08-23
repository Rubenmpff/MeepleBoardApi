using System.Globalization;
using System.Text;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Serviço responsável pela pesquisa no catálogo leve de jogos.
    ///
    /// A pesquisa é feita exclusivamente sobre GameSearchCatalog.
    /// Esta operação não cria entidades Game e não altera a base
    /// de dados principal de jogos utilizados pelos utilizadores.
    /// </summary>
    public class GameSearchCatalogService : IGameSearchCatalogService
    {
        private const int MaximumCandidatePool = 100;

        private readonly IGameSearchCatalogRepository _catalogRepository;

        public GameSearchCatalogService(
            IGameSearchCatalogRepository catalogRepository)
        {
            _catalogRepository = catalogRepository;
        }

        /// <summary>
        /// Pesquisa jogos no catálogo.
        ///
        /// Fluxo:
        /// 1. Normaliza o texto introduzido pelo utilizador.
        /// 2. Obtém um conjunto de candidatos da base de dados.
        /// 3. Aplica ranking final em memória.
        /// 4. Aplica paginação.
        /// 5. Converte para GameSuggestionDto.
        /// </summary>
        public async Task<List<GameSuggestionDto>> SearchAsync(
            string query,
            int offset = 0,
            int limit = 10,
            bool? isExpansion = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<GameSuggestionDto>();
            }

            var normalizedQuery = NormalizeSearchText(query);

            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return new List<GameSuggestionDto>();
            }

            var safeOffset = Math.Max(0, offset);
            var safeLimit = Math.Clamp(limit, 1, 50);

            /*
             * Não pedimos apenas 10 resultados ao SQL.
             *
             * Primeiro recolhemos um conjunto maior de candidatos
             * para podermos aplicar um ranking final mais inteligente.
             *
             * Exemplo:
             *
             * "ro"
             *
             * queremos conseguir comparar:
             *
             * Root
             * Robinson Crusoe
             * Roll for the Galaxy
             * Roll Player
             * Rococo
             * RoboRally
             *
             * em vez de simplesmente aceitar a ordem em que aparecem
             * na pesquisa externa ou alfabeticamente.
             */
            var requiredCandidates =
                Math.Max(
                    50,
                    (safeOffset + safeLimit) * 5);

            var candidateLimit =
                Math.Min(
                    requiredCandidates,
                    MaximumCandidatePool);

            var candidates =
                await _catalogRepository.SearchAsync(
                    normalizedQuery,
                    offset: 0,
                    limit: candidateLimit,
                    isExpansion: isExpansion,
                    cancellationToken: cancellationToken);

            if (candidates.Count == 0)
            {
                return new List<GameSuggestionDto>();
            }

            /*
             * O ranking é deliberadamente baseado em níveis de
             * relevância textual, em vez de um score gigante difícil
             * de compreender e manter.
             *
             * Dentro do mesmo nível textual, damos prioridade:
             *
             * 1. Jogos base
             * 2. Popularidade
             * 3. Ranking BGG
             * 4. Rating
             *
             * Isto torna os resultados mais previsvisíveis.
             */
            var rankedCandidates = candidates
                .Select(game => new
                {
                    Game = game,

                    TextTier = GetTextMatchTier(
                        game.NormalizedName,
                        normalizedQuery)
                })
                .Where(x => x.TextTier > 0)
                .OrderByDescending(x => x.TextTier)

                /*
                 * Quando estamos a pesquisar jogos e expansões
                 * simultaneamente, jogos base recebem prioridade.
                 *
                 * Se isExpansion == true, esta ordenação não interfere,
                 * porque todos os candidatos já são expansões.
                 */
                .ThenBy(x => x.Game.IsExpansion)

                /*
                 * UsersRated/RatingsCount é utilizado como principal
                 * indicador de popularidade.
                 *
                 * Para autocomplete isto é mais útil do que simplesmente
                 * ordenar pela nota média.
                 */
                .ThenByDescending(
                    x => x.Game.RatingsCount ?? 0)

                /*
                 * Se dois jogos tiverem popularidade semelhante,
                 * usamos o ranking geral BGG como desempate.
                 *
                 * Jogos sem ranking vão para o fim.
                 */
                .ThenBy(
                    x => x.Game.BggRank ?? int.MaxValue)

                /*
                 * Rating é apenas um critério secundário.
                 *
                 * Um jogo com 9.0 e 20 avaliações não deve normalmente
                 * aparecer antes de um jogo conhecido com milhares
                 * de avaliações.
                 */
                .ThenByDescending(
                    x => x.Game.AverageRating ?? 0)

                /*
                 * Desempates finais para manter resultados estáveis.
                 */
                .ThenBy(x => x.Game.Name.Length)
                .ThenBy(x => x.Game.Name)
                .Select(x => x.Game)
                .Skip(safeOffset)
                .Take(safeLimit)
                .ToList();

            return rankedCandidates
                .Select(MapToSuggestion)
                .ToList();
        }

        /// <summary>
        /// Define o nível de correspondência textual.
        ///
        /// Quanto maior o valor, melhor o resultado.
        /// </summary>
        private static int GetTextMatchTier(
            string normalizedName,
            string normalizedQuery)
        {
            if (string.IsNullOrWhiteSpace(normalizedName) ||
                string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return 0;
            }

            /*
             * Tier 4:
             *
             * Pesquisa exata.
             *
             * "root" -> "root"
             */
            if (string.Equals(
                normalizedName,
                normalizedQuery,
                StringComparison.Ordinal))
            {
                return 4;
            }

            /*
             * Tier 3:
             *
             * O nome começa pela pesquisa.
             *
             * "ro" -> "robinson crusoe"
             * "ro" -> "roll player"
             * "ro" -> "rococo"
             */
            if (normalizedName.StartsWith(
                normalizedQuery,
                StringComparison.Ordinal))
            {
                return 3;
            }

            /*
             * Tier 2:
             *
             * Uma palavra posterior começa pela pesquisa.
             *
             * Exemplo:
             *
             * pesquisa "gal"
             * "roll for the galaxy"
             *
             * pesquisa "cru"
             * "robinson crusoe"
             */
            if (ContainsWordStartingWith(
                normalizedName,
                normalizedQuery))
            {
                return 2;
            }

            /*
             * Tier 1:
             *
             * Correspondência parcial algures no nome.
             */
            if (normalizedName.Contains(
                normalizedQuery,
                StringComparison.Ordinal))
            {
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Verifica se alguma palavra do nome começa pelo termo pesquisado.
        /// </summary>
        private static bool ContainsWordStartingWith(
            string normalizedName,
            string normalizedQuery)
        {
            var words = normalizedName.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            foreach (var word in words)
            {
                if (word.StartsWith(
                    normalizedQuery,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Converte a entidade leve do catálogo para o DTO que o frontend
        /// já utiliza atualmente.
        ///
        /// Id e MeepleBoardScore ficam null porque a existência no catálogo
        /// não significa que exista uma entidade Game real na aplicação.
        /// </summary>
        private static GameSuggestionDto MapToSuggestion(
            GameSearchCatalog game)
        {
            return new GameSuggestionDto
            {
                Id = null,

                BggId = game.BggId,

                Name = game.Name,

                YearPublished = game.YearPublished,

                ImageUrl =
                    string.IsNullOrWhiteSpace(game.ThumbnailUrl)
                        ? null
                        : game.ThumbnailUrl,

                IsExpansion = game.IsExpansion,

                MinPlayers = game.MinPlayers,

                MaxPlayers = game.MaxPlayers,

                SupportsSoloMode =
                    game.SupportsSoloMode,

                IsCooperative =
                    game.IsCooperative,

                SupportsCampaign =
                    game.SupportsCampaign,

                AverageRating =
                    game.AverageRating,

                MeepleBoardScore = null,

                RatingsCount =
                    game.RatingsCount
            };
        }

        /// <summary>
        /// Normaliza texto para pesquisa.
        ///
        /// Exemplo:
        ///
        /// "  Pokémon   Café  "
        ///
        /// torna-se:
        ///
        /// "pokemon cafe"
        ///
        /// Remove:
        /// - diferenças entre maiúsculas/minúsculas;
        /// - acentos;
        /// - espaços repetidos.
        /// </summary>
        private static string NormalizeSearchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized =
                value
                    .Trim()
                    .ToLowerInvariant()
                    .Normalize(NormalizationForm.FormD);

            var builder =
                new StringBuilder(normalized.Length);

            var previousWasSpace = false;

            foreach (var character in normalized)
            {
                var category =
                    CharUnicodeInfo.GetUnicodeCategory(character);

                /*
                 * Ignora marcas utilizadas nos acentos.
                 *
                 * Exemplo:
                 * é -> e
                 * á -> a
                 */
                if (category ==
                    UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    if (!previousWasSpace &&
                        builder.Length > 0)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }

                    continue;
                }

                builder.Append(character);

                previousWasSpace = false;
            }

            return builder
                .ToString()
                .Trim()
                .Normalize(NormalizationForm.FormC);
        }
    }
}