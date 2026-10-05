using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Serviço responsável pela pesquisa e enriquecimento do catálogo leve de jogos.
    ///
    /// A pesquisa é feita exclusivamente sobre GameSearchCatalog.
    /// Esta operação não cria entidades Game e não altera a base
    /// de dados principal de jogos utilizados pelos utilizadores.
    ///
    /// O enriquecimento através do BGG /thing também atualiza apenas
    /// GameSearchCatalog e nunca cria entidades Game reais.
    /// </summary>
    public class GameSearchCatalogService : IGameSearchCatalogService
    {
        private static readonly TimeSpan SearchCacheDuration =
            TimeSpan.FromMinutes(5);

        /*
         * Single-flight global por chave de pesquisa.
         *
         * Se dois requests iguais chegarem antes de a cache ser preenchida,
         * apenas o primeiro executa SQL. Os restantes aguardam a mesma Task.
         *
         * Lazy + ExecutionAndPublication garante que a factory é executada
         * uma única vez mesmo sob concorrência real.
         */
        private static readonly ConcurrentDictionary<
            string,
            Lazy<Task<List<GameSuggestionDto>>>> InFlightSearches =
            new();

        private readonly IGameSearchCatalogRepository _catalogRepository;
        private readonly IBGGService _bggService;
        private readonly IMemoryCache _cache;

        public GameSearchCatalogService(
            IGameSearchCatalogRepository catalogRepository,
            IBGGService bggService,
            IMemoryCache cache)
        {
            _catalogRepository =
                catalogRepository ??
                throw new ArgumentNullException(
                    nameof(catalogRepository));

            _bggService =
                bggService ??
                throw new ArgumentNullException(
                    nameof(bggService));

            _cache =
                cache ??
                throw new ArgumentNullException(
                    nameof(cache));
        }

        /// <summary>
        /// Pesquisa jogos no catálogo.
        ///
        /// Fluxo:
        /// 1. Normaliza o texto introduzido pelo utilizador.
        /// 2. Obtém os resultados já ordenados/paginados pelo repositório.
        /// 3. Converte para GameSuggestionDto.
        ///
        /// IMPORTANTE:
        /// A pesquisa não chama o BGG /thing.
        /// O autocomplete deve continuar rápido e independente da disponibilidade
        /// momentânea do BoardGameGeek.
        /// </summary>
        public async Task<List<GameSuggestionDto>> SearchAsync(
            string query,
            int offset = 0,
            int limit = 10,
            bool? isExpansion = null,
            int? playerCount = null,
            double? minBggRating = null,
            string sort = "relevance",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<GameSuggestionDto>();
            }

            var normalizedQuery =
                NormalizeSearchText(query);

            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return new List<GameSuggestionDto>();
            }

            var safeOffset =
                Math.Max(
                    0,
                    offset);

            var safeLimit =
                Math.Clamp(
                    limit,
                    1,
                    50);

            var normalizedSort =
                NormalizeSort(sort);

            var normalizedPlayerCount =
                NormalizePlayerCount(playerCount);

            var normalizedMinimumRating =
                NormalizeMinimumRating(minBggRating);

            var cacheKey = BuildSearchCacheKey(
                normalizedQuery,
                safeOffset,
                safeLimit,
                isExpansion,
                normalizedPlayerCount,
                normalizedMinimumRating,
                normalizedSort);

            Console.WriteLine(
                $"[SEARCH] Service START | query={normalizedQuery} | offset={safeOffset} | limit={safeLimit} | sort={normalizedSort}");

            if (_cache.TryGetValue(
                    cacheKey,
                    out List<GameSuggestionDto>? cachedResults) &&
                cachedResults != null)
            {
                Console.WriteLine(
                    $"[SEARCH] Service CACHE HIT | query={normalizedQuery} | offset={safeOffset} | count={cachedResults.Count}");

                return cachedResults.ToList();
            }

            /*
             * O trabalho partilhado não usa o CancellationToken de um request
             * individual. Se o iPhone abandonar um request aos 15 s, não queremos
             * cancelar a pesquisa para outro caller que esteja a aguardar a mesma
             * chave. Cada caller pode, no entanto, deixar de aguardar via WaitAsync.
             */
            var lazySearch =
                InFlightSearches.GetOrAdd(
                    cacheKey,
                    _ =>
                    {
                        Console.WriteLine(
                            $"[SEARCH] SINGLE-FLIGHT OWNER | query={normalizedQuery} | offset={safeOffset}");

                        return new Lazy<Task<List<GameSuggestionDto>>>(
                            () => ExecuteSearchAndCacheAsync(
                                cacheKey,
                                normalizedQuery,
                                safeOffset,
                                safeLimit,
                                isExpansion,
                                normalizedPlayerCount,
                                normalizedMinimumRating,
                                normalizedSort),
                            LazyThreadSafetyMode.ExecutionAndPublication);
                    });

            if (lazySearch.IsValueCreated)
            {
                Console.WriteLine(
                    $"[SEARCH] SINGLE-FLIGHT JOIN | query={normalizedQuery} | offset={safeOffset}");
            }

            try
            {
                return await lazySearch
                    .Value
                    .WaitAsync(cancellationToken);
            }
            finally
            {
                /*
                 * Só o owner concluído remove a entrada correspondente.
                 * TryRemove com KeyValuePair evita remover uma entrada nova
                 * que, por acaso, tenha sido criada para a mesma chave.
                 */
                if (lazySearch.IsValueCreated &&
                    lazySearch.Value.IsCompleted)
                {
                    InFlightSearches.TryRemove(
                        new KeyValuePair<
                            string,
                            Lazy<Task<List<GameSuggestionDto>>>>(
                            cacheKey,
                            lazySearch));
                }
            }
        }

        private async Task<List<GameSuggestionDto>> ExecuteSearchAndCacheAsync(
            string cacheKey,
            string normalizedQuery,
            int safeOffset,
            int safeLimit,
            bool? isExpansion,
            int? normalizedPlayerCount,
            double? normalizedMinimumRating,
            string normalizedSort)
        {
            Console.WriteLine(
                $"[SEARCH] Service CACHE MISS | query={normalizedQuery} | offset={safeOffset}");

            try
            {
                var results =
                    await _catalogRepository.SearchAsync(
                        normalizedQuery,
                        offset: safeOffset,
                        limit: safeLimit,
                        isExpansion: isExpansion,
                        playerCount: normalizedPlayerCount,
                        minBggRating: normalizedMinimumRating,
                        sort: normalizedSort,
                        cancellationToken: CancellationToken.None);

                var suggestions =
                    results
                        .Select(MapToSuggestion)
                        .ToList();

                _cache.Set(
                    cacheKey,
                    suggestions,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow =
                            SearchCacheDuration,
                        Priority = CacheItemPriority.Normal
                    });

                Console.WriteLine(
                    $"[SEARCH] Service END | query={normalizedQuery} | offset={safeOffset} | count={suggestions.Count}");

                return suggestions;
            }
            finally
            {
                /*
                 * A remoção definitiva também é feita no finally de SearchAsync.
                 * Este bloco existe apenas para deixar claro que nenhuma exceção
                 * é convertida em resultado/cache.
                 */
            }
        }

        /// <summary>
        /// Enriquece registos já existentes no GameSearchCatalog através
        /// do endpoint /thing do BoardGameGeek.
        ///
        /// Esta operação:
        /// - nunca cria entidades Game;
        /// - não adiciona jogos novos ao catálogo;
        /// - apenas atualiza registos que já existem no GameSearchCatalog;
        /// - delega no IBGGService o batching do /thing (máximo 20 IDs/pedido);
        /// - grava todos os enriquecimentos válidos num único CommitAsync.
        /// </summary>
        public async Task<int> EnrichDetailsAsync(
            IReadOnlyCollection<int> bggIds,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(
                bggIds);

            var cleanIds =
                bggIds
                    .Where(id => id > 0)
                    .Distinct()
                    .ToArray();

            if (cleanIds.Length == 0)
            {
                return 0;
            }

            cancellationToken.ThrowIfCancellationRequested();

            /*
             * GetByBggIdsAsync devolve entidades tracked de propósito.
             *
             * Assim podemos aplicar UpdateFromBgg diretamente e efetuar
             * apenas um SaveChanges no fim.
             */
            var catalogGames =
                await _catalogRepository.GetByBggIdsAsync(
                    cleanIds,
                    cancellationToken);

            if (catalogGames.Count == 0)
            {
                return 0;
            }

            /*
             * Pedimos detalhes apenas para IDs que realmente existem
             * no nosso catálogo.
             *
             * Isto impede que esta operação seja utilizada, mesmo por engano,
             * para transformar um ID externo num novo registo local.
             */
            var idsToFetch =
                catalogGames.Keys
                    .Select(id =>
                        id.ToString(
                            CultureInfo.InvariantCulture))
                    .ToList();

            var bggGames =
                await _bggService.GetGamesByIdsAsync(
                    idsToFetch,
                    cancellationToken);

            if (bggGames.Count == 0)
            {
                return 0;
            }

            var enrichedCount = 0;

            foreach (var bggGame in bggGames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!bggGame.BggId.HasValue ||
                    !catalogGames.TryGetValue(
                        bggGame.BggId.Value,
                        out var catalogGame))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(
                        bggGame.Name))
                {
                    continue;
                }

                var normalizedName =
                    NormalizeSearchText(
                        bggGame.Name);

                if (string.IsNullOrWhiteSpace(
                        normalizedName))
                {
                    continue;
                }

                /*
                 * Preservamos os dados básicos já conhecidos quando o /thing
                 * não devolver determinado valor.
                 *
                 * O dump oficial é especialmente útil para rating/popularidade,
                 * portanto uma resposta parcial do /thing não deve apagar esses
                 * dados do catálogo.
                 */
                var yearPublished =
                    bggGame.YearPublished ??
                    catalogGame.YearPublished;

                var imageUrl =
                    !string.IsNullOrWhiteSpace(
                        bggGame.ImageUrl)
                        ? bggGame.ImageUrl
                        : catalogGame.ThumbnailUrl;

                var averageRating =
                    bggGame.AverageRating ??
                    catalogGame.AverageRating;

                var ratingsCount =
                    bggGame.UsersRatedCount ??
                    catalogGame.RatingsCount;

                var bggRank =
                    bggGame.BggRanking ??
                    catalogGame.BggRank;

                catalogGame.UpdateFromBgg(
                    name: bggGame.Name,
                    normalizedName: normalizedName,
                    yearPublished: yearPublished,
                    thumbnailUrl: imageUrl,
                    isExpansion: bggGame.IsExpansion,
                    minPlayers:
                        bggGame.MinPlayers ??
                        catalogGame.MinPlayers,
                    maxPlayers:
                        bggGame.MaxPlayers ??
                        catalogGame.MaxPlayers,
                    isCooperative:
                        bggGame.IsCooperative,
                    supportsCampaign:
                        bggGame.SupportsCampaign,
                    averageRating: averageRating,
                    ratingsCount: ratingsCount,
                    bggRank: bggRank);

                enrichedCount++;
            }

            if (enrichedCount == 0)
            {
                return 0;
            }

            await _catalogRepository.CommitAsync(
                cancellationToken);

            return enrichedCount;
        }

        private static string BuildSearchCacheKey(
            string normalizedQuery,
            int offset,
            int limit,
            bool? isExpansion,
            int? playerCount,
            double? minBggRating,
            string sort)
        {
            return string.Join(
                ":",
                "game-search",
                normalizedQuery,
                offset,
                limit,
                isExpansion?.ToString() ?? "all",
                playerCount?.ToString(CultureInfo.InvariantCulture) ?? "all",
                minBggRating?.ToString(CultureInfo.InvariantCulture) ?? "all",
                sort);
        }


        private static int? NormalizePlayerCount(
            int? playerCount)
        {
            if (!playerCount.HasValue)
            {
                return null;
            }

            return playerCount.Value is >= 1 and <= 5
                ? playerCount.Value
                : null;
        }

        private static double? NormalizeMinimumRating(
            double? minBggRating)
        {
            if (!minBggRating.HasValue)
            {
                return null;
            }

            return minBggRating.Value is >= 0 and <= 10
                ? minBggRating.Value
                : null;
        }

        private static string NormalizeSort(
            string? sort)
        {
            return sort?
                .Trim()
                .ToLowerInvariant() switch
            {
                "most_known" => "most_known",
                "bgg_rating" => "bgg_rating",
                "year_desc" => "year_desc",
                "name_asc" => "name_asc",
                _ => "relevance"
            };
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
        private static string NormalizeSearchText(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized =
                value
                    .Trim()
                    .ToLowerInvariant()
                    .Normalize(
                        NormalizationForm.FormD);

            var builder =
                new StringBuilder(
                    normalized.Length);

            var previousWasSpace = false;

            foreach (var character in normalized)
            {
                var category =
                    CharUnicodeInfo.GetUnicodeCategory(
                        character);

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
                .Normalize(
                    NormalizationForm.FormC);
        }
    }
}
