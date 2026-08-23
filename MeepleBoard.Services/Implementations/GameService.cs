using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Serviço de aplicação responsável por coordenar operações de leitura, escrita,
    /// importação e sincronização de jogos. Faz a ponte entre o repositório e serviços externos como o BGG.
    /// </summary>
    public class GameService : IGameService
    {
        private readonly IGameRepository _gameRepository;
        private readonly IBGGService _bggService;
        private readonly IGameSearchCatalogService _gameSearchCatalogService;
        private readonly IMapper _mapper;
        private readonly ILogger<GameService> _logger;

        public GameService(
            IGameRepository gameRepository,
            IBGGService bggService,
            IGameSearchCatalogService gameSearchCatalogService,
            IMapper mapper,
            ILogger<GameService> logger)
        {
            _gameRepository = gameRepository;
            _bggService = bggService;
            _gameSearchCatalogService = gameSearchCatalogService;
            _mapper = mapper;
            _logger = logger;
        }


        // -----------------------------------------------------------------------------
        // MÉTODO PÚBLICO CENTRAL – GetOrCreateMinimalGameAsync
        // Garante que o jogo existe na BD com o mínimo necessário.
        // Usado por MatchService, UserGameLibraryService, e qualquer outro serviço
        // que precise de uma referência local ao jogo sem importar tudo do BGG.
        // REGRA: só guarda quando há interação real (partida, biblioteca, comentário).
        // Pesquisas NUNCA chamam este método — apenas interações reais.
        // -----------------------------------------------------------------------------

        public async Task<Game> GetOrCreateMinimalGameAsync(
            int bggId,
            CancellationToken ct = default)
        {
            var existing =
                await _gameRepository.GetGameByBggIdAsync(
                    bggId,
                    ct);

            if (existing != null)
            {
                _logger.LogInformation(
                    "✅ Jogo já existe na BD (BggId: {BggId})",
                    bggId);

                return existing;
            }

            var bggGame =
                await _bggService.GetGameByIdAsync(
                    bggId.ToString(),
                    ct);

            if (bggGame == null)
            {
                throw new KeyNotFoundException(
                    $"Jogo com BggId {bggId} não encontrado no BGG.");
            }

            var game = new Game(
                bggGame.Name,
                "",
                bggGame.ImageUrl,
                bggGame.SupportsSoloMode);

            game.SetBggId(bggGame.BggId);
            game.ApproveGame();

            game.SetPlayerCount(
                bggGame.MinPlayers,
                bggGame.MaxPlayers);

            game.SetCooperative(
                bggGame.IsCooperative);

            game.SetSupportsCampaign(
                bggGame.SupportsCampaign);

            game.SetAverageRating(
                bggGame.AverageRating);

            game.SetUsersRatedCount(
                bggGame.UsersRatedCount);

            if (bggGame.IsExpansion &&
                bggGame.BaseGameBggId.HasValue)
            {
                game.SetBaseGameBggId(
                    bggGame.BaseGameBggId);
            }

            await _gameRepository.AddAsync(
                game,
                ct);

            await _gameRepository.CommitAsync(ct);

            _logger.LogInformation(
                "💾 Jogo guardado na BD: {Name} (BggId: {BggId}) | Solo: {Solo} | Coop: {Coop} | {Min}-{Max} jogadores",
                game.Name,
                bggId,
                game.SupportsSoloMode,
                game.IsCooperative,
                game.MinPlayers,
                game.MaxPlayers);

            return game;
        }


        public async Task<PagedResponse<GameDto>> GetAllAsync(
            int pageIndex,
            int pageSize,
            CancellationToken ct = default)
        {
            var totalGames =
                await _gameRepository.GetAllAsync(
                    0,
                    int.MaxValue,
                    ct);

            var pagedGames =
                await _gameRepository.GetAllAsync(
                    pageIndex,
                    pageSize,
                    ct);

            return new PagedResponse<GameDto>(
                _mapper.Map<IReadOnlyList<GameDto>>(pagedGames),
                totalGames.Count,
                pageSize,
                pageIndex);
        }


        public async Task<int> RecomputeAllMeepleBoardScoresAsync(
            CancellationToken ct = default)
        {
            var allGames =
                await _gameRepository.GetAllAsync(
                    0,
                    int.MaxValue,
                    ct);

            var updated = 0;

            foreach (var game in allGames)
            {
                var avg =
                    await _gameRepository
                        .GetAveragePersonalRatingAsync(
                            game.Id,
                            ct);

                var newScore =
                    avg.HasValue
                        ? (int?)Math.Round(avg.Value * 10)
                        : null;

                if (game.MeepleBoardScore != newScore)
                {
                    game.SetMeepleBoardScore(newScore);

                    await _gameRepository.UpdateAsync(
                        game,
                        ct);

                    updated++;
                }
            }

            _logger.LogInformation(
                "🏆 Recompute de MeepleBoardScore: {Updated}/{Total} jogos atualizados.",
                updated,
                allGames.Count);

            return updated;
        }


        public async Task<PagedResponse<GameDto>> GetRankingsAsync(
            int pageIndex,
            int pageSize,
            CancellationToken ct = default)
        {
            return await GetRankingsAsync(
                pageIndex,
                pageSize,
                "meepleboard",
                ct);
        }


        public async Task<PagedResponse<GameDto>> GetRankingsAsync(
            int pageIndex,
            int pageSize,
            string source,
            CancellationToken ct = default)
        {
            var (items, total) =
                source == "bgg"
                    ? await _gameRepository.GetRankedByBggRatingAsync(
                        pageIndex,
                        pageSize,
                        ct)
                    : await _gameRepository.GetRankedByMeepleBoardScoreAsync(
                        pageIndex,
                        pageSize,
                        ct);

            return new PagedResponse<GameDto>(
                _mapper.Map<IReadOnlyList<GameDto>>(items),
                total,
                pageSize,
                pageIndex);
        }


        public async Task<PagedResponse<GameDto>> GetPersonalRankingsAsync(
            Guid userId,
            int pageIndex,
            int pageSize,
            CancellationToken ct = default)
        {
            var (items, total) =
                await _gameRepository.GetPersonalRankingsAsync(
                    userId,
                    pageIndex,
                    pageSize,
                    ct);

            var dtos = items
                .Select(x =>
                {
                    var dto =
                        _mapper.Map<GameDto>(x.Game);

                    dto.PersonalAverageRating =
                        x.Rating;

                    return dto;
                })
                .ToList();

            return new PagedResponse<GameDto>(
                dtos,
                total,
                pageSize,
                pageIndex);
        }


        public async Task<GameDto?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var game =
                await _gameRepository.GetByIdAsync(
                    id,
                    cancellationToken);

            if (game == null)
                return null;

            var dto =
                _mapper.Map<GameDto>(game);

            if (game.IsExpansion &&
                game.BaseGameId.HasValue)
            {
                dto.BaseGameId =
                    game.BaseGameId;
            }
            else
            {
                var expansions =
                    await _gameRepository
                        .GetExpansionsForBaseGameAsync(
                            id,
                            cancellationToken);

                dto.Expansions =
                    _mapper.Map<List<GameDto>>(
                        expansions);
            }

            return dto;
        }


        public async Task<GameDto?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "O nome do jogo não pode estar vazio.");
            }

            var trimmedName =
                name.Trim();

            _logger.LogInformation(
                "🔎 Buscando jogo por nome: {Name}",
                trimmedName);

            var game =
                await _gameRepository.GetByNameAsync(
                    trimmedName,
                    cancellationToken);

            return game != null
                ? _mapper.Map<GameDto>(game)
                : null;
        }


        public async Task<GameDto?> GetOrImportByNameAsync(
            string name,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "O nome do jogo não pode estar vazio.");
            }

            var trimmed =
                name.Trim();

            var local =
                await _gameRepository.GetByNameAsync(
                    trimmed,
                    ct);

            if (local != null)
            {
                return _mapper.Map<GameDto>(local);
            }

            var bgg =
                await _bggService.GetGameByNameAsync(
                    trimmed,
                    ct);

            if (bgg?.BggId is null)
                return null;

            var entity =
                await ImportGameRecursiveAsync(
                    bgg.BggId.Value,
                    ct);

            return entity == null
                ? null
                : _mapper.Map<GameDto>(entity);
        }


        private async Task TryLinkExpansionsAsync(
            Game baseGame,
            CancellationToken cancellationToken)
        {
            if (!baseGame.BGGId.HasValue)
                return;

            var expansions =
                await _gameRepository
                    .GetExpansionsWithBaseGameBggIdAsync(
                        baseGame.BGGId.Value,
                        cancellationToken);

            foreach (var expansion in expansions)
            {
                if (expansion.BaseGameId ==
                    baseGame.Id)
                {
                    continue;
                }

                expansion.SetBaseGame(baseGame);

                await _gameRepository.UpdateAsync(
                    expansion,
                    cancellationToken);
            }

            await _gameRepository.CommitAsync(
                cancellationToken);
        }


        public async Task<GameDto?> ImportByBggIdAsync(
            int bggId,
            CancellationToken ct = default)
        {
            var entity =
                await ImportGameRecursiveAsync(
                    bggId,
                    ct);

            return entity == null
                ? null
                : _mapper.Map<GameDto>(entity);
        }


        // ============================================================================
        // SEARCH
        // ============================================================================

        public async Task<List<GameSuggestionDto>> SearchSuggestionsAsync(
            string query,
            int offset = 0,
            int limit = 10,
            CancellationToken ct = default)
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

            var trimmedQuery =
                query.Trim();

            var safeOffset =
                Math.Max(0, offset);

            var safeLimit =
                Math.Clamp(limit, 1, 50);

            var candidateLimit =
                Math.Clamp(
                    Math.Max(
                        40,
                        safeOffset + safeLimit + 20),
                    40,
                    80);

            // 1) Jogos reais já existentes na MeepleBoard.
            var localResults =
                await _gameRepository.SearchByNameAsync(
                    trimmedQuery,
                    0,
                    candidateLimit,
                    ct);

            var candidates =
                localResults
                    .Where(g => g.BGGId.HasValue)
                    .Select(MapLocalGameToSuggestion)
                    .ToList();

            // 2) Catálogo persistente completo para pesquisa.
            var catalogResults =
                await _gameSearchCatalogService.SearchAsync(
                    trimmedQuery,
                    offset: 0,
                    limit: candidateLimit,
                    isExpansion: null,
                    cancellationToken: ct);

            candidates.AddRange(catalogResults);

            // Pesquisa normal nunca chama o BGG em tempo real.
            var result =
                RankAndPageSuggestions(
                    candidates,
                    normalizedQuery,
                    safeOffset,
                    safeLimit,
                    expansionMode: null);

            _logger.LogInformation(
                "🔎 SearchSuggestions '{Query}': " +
                "{LocalCount} locais + {CatalogCount} catálogo -> " +
                "{ResultCount} resultados.",
                query,
                localResults.Count,
                catalogResults.Count,
                result.Count);

            return result;
        }


        public async Task<List<GameSuggestionDto>> SearchExpansionSuggestionsAsync(
            string query,
            int offset = 0,
            int limit = 10,
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

            var trimmedQuery =
                query.Trim();

            var safeOffset =
                Math.Max(0, offset);

            var safeLimit =
                Math.Clamp(limit, 1, 50);

            var candidateLimit =
                Math.Clamp(
                    Math.Max(
                        40,
                        safeOffset + safeLimit + 20),
                    40,
                    80);

            // 1) Expansões reais já existentes localmente.
            var localExpansions =
                await _gameRepository.SearchExpansionsByNameAsync(
                    trimmedQuery,
                    0,
                    candidateLimit,
                    cancellationToken);

            var candidates =
                localExpansions
                    .Where(g => g.BGGId.HasValue)
                    .Select(MapLocalGameToSuggestion)
                    .ToList();

            // 2) Expansões do catálogo persistente.
            var catalogResults =
                await _gameSearchCatalogService.SearchAsync(
                    trimmedQuery,
                    offset: 0,
                    limit: candidateLimit,
                    isExpansion: true,
                    cancellationToken: cancellationToken);

            candidates.AddRange(catalogResults);

            return RankAndPageSuggestions(
                candidates,
                normalizedQuery,
                safeOffset,
                safeLimit,
                expansionMode: true);
        }


        public async Task<List<GameSuggestionDto>> GetExpansionSuggestionsForBaseAsync(
            Guid baseGameId,
            CancellationToken cancellationToken = default)
        {
            var baseGame =
                await _gameRepository.GetByIdAsync(
                    baseGameId,
                    cancellationToken);

            if (baseGame == null)
            {
                throw new KeyNotFoundException(
                    "Jogo base não encontrado.");
            }

            var localExpansions =
                await _gameRepository
                    .GetExpansionsForBaseGameAsync(
                        baseGameId,
                        cancellationToken);

            var suggestions =
                localExpansions
                    .Where(g =>
                        g.BGGId.HasValue)
                    .Select(g =>
                        new GameSuggestionDto
                        {
                            Id =
                                g.Id.ToString(),

                            BggId =
                                g.BGGId!.Value,

                            Name =
                                g.Name,

                            YearPublished =
                                g.YearPublished,

                            ImageUrl =
                                g.ImageUrl,

                            IsExpansion =
                                true,

                            MinPlayers =
                                g.MinPlayers,

                            MaxPlayers =
                                g.MaxPlayers,

                            SupportsSoloMode =
                                g.SupportsSoloMode,

                            IsCooperative =
                                g.IsCooperative,

                            SupportsCampaign =
                                g.SupportsCampaign,

                            AverageRating =
                                g.AverageRating,

                            MeepleBoardScore =
                                g.MeepleBoardScore,

                            RatingsCount =
                                g.UsersRatedCount
                        })
                    .ToList();

            if (baseGame.BGGId.HasValue)
            {
                try
                {
                    var bggSuggestions =
                        await _bggService
                            .SearchGameSuggestionsAsync(
                                baseGame.Name,
                                offset: 0,
                                limit: 10,
                                cancellationToken);

                    var bggExpansions =
                        bggSuggestions
                            .Where(b =>
                                b.IsExpansion &&
                                !suggestions.Any(s =>
                                    s.BggId ==
                                    b.BggId))
                            .Take(
                                Math.Max(
                                    0,
                                    10 - suggestions.Count))
                            .ToList();

                    suggestions.AddRange(
                        bggExpansions);

                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "⚠️ Não foi possível buscar expansões no BGG.");
                }
            }

            return suggestions;
        }


        public async Task<List<GameSuggestionDto>> SearchBaseGameSuggestionsAsync(
            string query,
            int offset = 0,
            int limit = 10,
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

            var trimmedQuery =
                query.Trim();

            var safeOffset =
                Math.Max(0, offset);

            var safeLimit =
                Math.Clamp(limit, 1, 50);

            var candidateLimit =
                Math.Clamp(
                    Math.Max(
                        40,
                        safeOffset + safeLimit + 20),
                    40,
                    80);

            // 1) Jogos base reais já existentes localmente.
            var localGames =
                await _gameRepository.SearchBaseGamesByNameAsync(
                    trimmedQuery,
                    0,
                    candidateLimit,
                    cancellationToken);

            var candidates =
                localGames
                    .Where(g => g.BGGId.HasValue)
                    .Select(MapLocalGameToSuggestion)
                    .ToList();

            // 2) Jogos base do catálogo persistente.
            var catalogResults =
                await _gameSearchCatalogService.SearchAsync(
                    trimmedQuery,
                    offset: 0,
                    limit: candidateLimit,
                    isExpansion: false,
                    cancellationToken: cancellationToken);

            candidates.AddRange(catalogResults);

            return RankAndPageSuggestions(
                candidates,
                normalizedQuery,
                safeOffset,
                safeLimit,
                expansionMode: false);
        }


        // ============================================================================
        // SEARCH HELPERS
        // ============================================================================

        private static GameSuggestionDto MapLocalGameToSuggestion(
            Game game)
        {
            return new GameSuggestionDto
            {
                Id =
                    game.Id.ToString(),

                BggId =
                    game.BGGId ?? 0,

                Name =
                    game.Name,

                YearPublished =
                    game.YearPublished,

                ImageUrl =
                    game.ImageUrl,

                IsExpansion =
                    game.IsExpansion,

                MinPlayers =
                    game.MinPlayers,

                MaxPlayers =
                    game.MaxPlayers,

                SupportsSoloMode =
                    game.SupportsSoloMode,

                IsCooperative =
                    game.IsCooperative,

                SupportsCampaign =
                    game.SupportsCampaign,

                AverageRating =
                    game.AverageRating,

                MeepleBoardScore =
                    game.MeepleBoardScore,

                RatingsCount =
                    game.UsersRatedCount
            };
        }


        private static List<GameSuggestionDto> RankAndPageSuggestions(
            IEnumerable<GameSuggestionDto> source,
            string normalizedQuery,
            int offset,
            int limit,
            bool? expansionMode)
        {
            var filtered =
                source
                    .Where(x =>
                        x != null)
                    .Where(x =>
                        expansionMode == null ||
                        x.IsExpansion ==
                        expansionMode.Value)
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.Name))
                    .ToList();

            /*
             * Se o mesmo jogo estiver:
             *
             * - em Game
             * - no GameSearchCatalog
             * - e no resultado BGG
             *
             * fica apenas um.
             *
             * A versão local tem prioridade porque contém:
             *
             * - Id GUID
             * - MeepleBoardScore
             */
            var deduplicated =
                filtered
                    .GroupBy(
                        GetSuggestionDeduplicationKey)
                    .Select(group =>
                        group
                            .OrderByDescending(
                                HasLocalId)
                            .ThenByDescending(x =>
                                x.RatingsCount ?? 0)
                            .First())
                    .ToList();

            return deduplicated
                .Select(game =>
                    new
                    {
                        Game = game,

                        Score =
                            CalculateSearchScore(
                                game,
                                normalizedQuery)
                    })
                .Where(x =>
                    x.Score >
                    double.MinValue)
                .OrderByDescending(x =>
                    x.Score)
                .ThenByDescending(x =>
                    HasLocalId(x.Game))
                .ThenByDescending(x =>
                    x.Game.RatingsCount ?? 0)
                .ThenBy(x =>
                    x.Game.IsExpansion)
                .ThenBy(x =>
                    x.Game.Name)
                .Skip(offset)
                .Take(limit)
                .Select(x =>
                    x.Game)
                .ToList();
        }


        private static string GetSuggestionDeduplicationKey(
            GameSuggestionDto suggestion)
        {
            if (suggestion.BggId > 0)
            {
                return
                    $"bgg:{suggestion.BggId}";
            }

            var normalizedName =
                NormalizeSearchText(
                    suggestion.Name);

            return
                $"name:{normalizedName}:{suggestion.YearPublished?.ToString() ?? "unknown"}";
        }


        private static int HasLocalId(
            GameSuggestionDto suggestion)
        {
            return
                !string.IsNullOrWhiteSpace(
                    suggestion.Id)
                    ? 1
                    : 0;
        }


        /// <summary>
        /// Conta apenas resultados fortes e distintos para decidir se vale a pena
        /// recorrer ao BGG como fallback.
        ///
        /// Um resultado é considerado forte quando:
        /// - o nome é exatamente igual à pesquisa; ou
        /// - o nome começa pelo termo pesquisado.
        ///
        /// Resultados onde o termo aparece apenas a meio do nome não impedem
        /// uma chamada ao BGG. Exemplo: pesquisar "ro" não deve considerar
        /// "Thunder Road" forte apenas porque "Road" começa por "ro".
        /// </summary>






        private static double CalculateSearchScore(
            GameSuggestionDto game,
            string normalizedQuery)
        {
            var normalizedName =
                NormalizeSearchText(
                    game.Name);

            if (string.IsNullOrWhiteSpace(
                normalizedName))
            {
                return double.MinValue;
            }

            double score = 0;

            /*
             * MATCH EXATO
             *
             * "root" -> Root
             */
            if (normalizedName ==
                normalizedQuery)
            {
                score += 100_000;
            }

            /*
             * PREFIX MATCH
             *
             * "ro" -> Root
             * "ro" -> Robinson Crusoe
             * "ro" -> Roll Player
             */
            else if (
                normalizedName.StartsWith(
                    normalizedQuery,
                    StringComparison.Ordinal))
            {
                /*
                 * Todos os prefix matches pertencem ao mesmo nível textual.
                 *
                 * Não penalizamos o comprimento total do nome:
                 * títulos legítimos e populares como
                 * "Robinson Crusoe: Adventures on the Cursed Island"
                 * não devem perder relevância apenas por serem maiores.
                 *
                 * A ordenação dentro deste nível fica a cargo da
                 * popularidade, rating e restantes critérios abaixo.
                 */
                score += 50_000;
            }

            /*
             * INÍCIO DE UMA PALAVRA
             *
             * "gal" -> Roll for the Galaxy
             */
            else if (
                StartsAnyWordWith(
                    normalizedName,
                    normalizedQuery))
            {
                score += 25_000;
            }

            /*
             * CONTAINS
             */
            else if (
                normalizedName.Contains(
                    normalizedQuery,
                    StringComparison.Ordinal))
            {
                score += 10_000;

                var position =
                    normalizedName.IndexOf(
                        normalizedQuery,
                        StringComparison.Ordinal);

                score -=
                    Math.Max(
                        0,
                        position)
                    * 50;
            }
            else
            {
                return double.MinValue;
            }

            // Jogos base recebem pequena preferência.
            if (game.IsExpansion)
            {
                score -= 2_500;
            }
            else
            {
                score += 1_000;
            }

            // Popularidade BGG.
            if (game.RatingsCount is > 0)
            {
                score +=
                    Math.Log10(
                        game.RatingsCount.Value + 1)
                    * 600;
            }

            // Rating apenas como desempate.
            if (game.AverageRating is > 0)
            {
                score +=
                    game.AverageRating.Value
                    * 25;
            }

            // Pequeno bónus para Game real local.
            if (HasLocalId(game) == 1)
            {
                score += 100;
            }

            return score;
        }


        private static bool StartsAnyWordWith(
            string normalizedName,
            string normalizedQuery)
        {
            return normalizedName
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(word =>
                    word.StartsWith(
                        normalizedQuery,
                        StringComparison.Ordinal));
        }


        private static string NormalizeSearchText(
            string? value)
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

            foreach (var character in normalized)
            {
                var category =
                    CharUnicodeInfo
                        .GetUnicodeCategory(
                            character);

                if (category !=
                    UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(
                        character);
                }
            }

            return string.Join(
                " ",
                builder
                    .ToString()
                    .Normalize(
                        NormalizationForm.FormC)
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries));
        }


        public async Task<IReadOnlyList<GameDto>> GetPendingApprovalAsync(
            CancellationToken cancellationToken = default)
        {
            var games =
                await _gameRepository
                    .GetPendingApprovalAsync(
                        cancellationToken);

            return _mapper.Map<
                IReadOnlyList<GameDto>>(
                    games);
        }


        public async Task<bool> ExistsByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return
                await _gameRepository.GetByIdAsync(
                    id,
                    cancellationToken) != null;
        }


        public async Task<bool> ExistsByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return await _gameRepository.ExistsByNameAsync(
                name.Trim(),
                cancellationToken);
        }


        public async Task<Guid> AddAsync(
            GameDto gameDto,
            CancellationToken cancellationToken = default)
        {
            if (await _gameRepository.ExistsByNameAsync(
                gameDto.Name,
                cancellationToken))
            {
                throw new ArgumentException(
                    "Já existe um jogo com este nome.");
            }

            var newGame =
                new Game(
                    gameDto.Name,
                    gameDto.Description,
                    gameDto.ImageUrl,
                    gameDto.SupportsSoloMode);

            if (gameDto.BggId.HasValue)
            {
                newGame.SetBggId(
                    gameDto.BggId);
            }

            newGame.ApproveGame();

            await _gameRepository.AddAsync(
                newGame,
                cancellationToken);

            await _gameRepository.CommitAsync(
                cancellationToken);

            return newGame.Id;
        }


        public async Task<int> UpdateAsync(
            GameDto gameDto,
            CancellationToken cancellationToken = default)
        {
            var existingGame =
                await _gameRepository.GetByIdAsync(
                    gameDto.Id,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    "Jogo não encontrado.");

            existingGame.UpdateDetails(
                gameDto.Name,
                gameDto.Description,
                gameDto.ImageUrl,
                gameDto.SupportsSoloMode);

            await _gameRepository.UpdateAsync(
                existingGame,
                cancellationToken);

            return await _gameRepository.CommitAsync(
                cancellationToken);
        }


        public async Task<int> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var game =
                await _gameRepository.GetByIdAsync(
                    id,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    "Jogo não encontrado.");

            await _gameRepository.DeleteAsync(
                game,
                cancellationToken);

            return await _gameRepository.CommitAsync(
                cancellationToken);
        }


        public async Task<int> ApproveGameAsync(
            Guid gameId,
            CancellationToken cancellationToken = default)
        {
            var game =
                await _gameRepository.GetByIdAsync(
                    gameId,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    "Jogo não encontrado.");

            game.ApproveGame();

            await _gameRepository.UpdateAsync(
                game,
                cancellationToken);

            return await _gameRepository.CommitAsync(
                cancellationToken);
        }


        public async Task<IReadOnlyList<GameDto>> GetRecentlyPlayedAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            var games =
                await _gameRepository
                    .GetRecentlyPlayedAsync(
                        limit,
                        cancellationToken);

            return _mapper.Map<
                IReadOnlyList<GameDto>>(
                    games);
        }


        public async Task<IReadOnlyList<GameDto>> GetMostSearchedAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            var games =
                await _gameRepository
                    .GetMostSearchedAsync(
                        limit,
                        cancellationToken);

            return _mapper.Map<
                IReadOnlyList<GameDto>>(
                    games);
        }


        // -----------------------------------------------------------------------------
        // MÉTODO PRIVADO CENTRAL – Importação explícita
        // -----------------------------------------------------------------------------

        private async Task<Game?> ImportGameRecursiveAsync(
            int bggId,
            HashSet<int> visited,
            CancellationToken ct)
        {
            if (visited.Contains(bggId))
            {
                _logger.LogInformation(
                    "🔁 BGG ID {BggId} já visitado. Evitando loop.",
                    bggId);

                return await _gameRepository
                    .GetGameByBggIdAsync(
                        bggId,
                        ct);
            }

            visited.Add(bggId);

            var existing =
                await _gameRepository
                    .GetGameByBggIdAsync(
                        bggId,
                        ct);

            if (existing != null)
            {
                _logger.LogInformation(
                    "✅ Jogo já existente no DB: {GameName} (BGG ID: {BggId})",
                    existing.Name,
                    bggId);

                return existing;
            }

            var bgg =
                await _bggService.GetGameByIdAsync(
                    bggId.ToString(),
                    ct);

            if (bgg == null)
            {
                _logger.LogWarning(
                    "❌ Nenhum jogo encontrado no BGG com o ID {BggId}",
                    bggId);

                return null;
            }

            _logger.LogInformation(
                "📦 Jogo encontrado no BGG: {Name} (ID: {BggId}) - Expansão: {IsExpansion}",
                bgg.Name,
                bgg.BggId,
                bgg.IsExpansion);

            var game =
                new Game(
                    bgg.Name,
                    bgg.Description,
                    bgg.ImageUrl,
                    bgg.SupportsSoloMode);

            game.SetBggId(
                bgg.BggId);

            game.ApproveGame();

            game.SetAverageRating(
                bgg.AverageRating);

            game.SetBggRanking(
                bgg.BggRanking);

            game.SetUsersRatedCount(
                bgg.UsersRatedCount);

            game.SetPlayerCount(
                bgg.MinPlayers,
                bgg.MaxPlayers);

            game.SetCooperative(
                bgg.IsCooperative);

            game.SetSupportsCampaign(
                bgg.SupportsCampaign);

            game.UpdateBggStats(
                bgg.Description,
                bgg.ImageUrl,
                bgg.BggRanking,
                bgg.AverageRating,
                bgg.YearPublished,
                usersRatedCount:
                    bgg.UsersRatedCount);

            if (bgg.IsExpansion &&
                bgg.BaseGameBggId.HasValue)
            {
                _logger.LogInformation(
                    "🔧 Importando jogo base para expansão '{Name}' (BaseGameBggId: {BaseId})",
                    bgg.Name,
                    bgg.BaseGameBggId.Value);

                var baseGame =
                    await ImportGameRecursiveAsync(
                        bgg.BaseGameBggId.Value,
                        visited,
                        ct);

                if (baseGame != null)
                {
                    game.SetBaseGame(
                        baseGame);
                }
                else
                {
                    game.SetBaseGameBggId(
                        bgg.BaseGameBggId);

                    _logger.LogWarning(
                        "⚠️ Jogo base não foi importado para expansão '{Name}'",
                        bgg.Name);
                }
            }

            await _gameRepository.AddAsync(
                game,
                ct);

            await _gameRepository.CommitAsync(
                ct);

            _logger.LogInformation(
                "💾 Jogo salvo no banco de dados: {Name} (BGG ID: {BggId})",
                game.Name,
                game.BGGId);

            if (!bgg.IsExpansion)
            {
                _logger.LogInformation(
                    "🔍 Tentando ligar expansões órfãs ao jogo base '{Name}'",
                    game.Name);

                await TryLinkExpansionsAsync(
                    game,
                    ct);
            }

            return game;
        }


        private Task<Game?> ImportGameRecursiveAsync(
            int bggId,
            CancellationToken ct)
        {
            return ImportGameRecursiveAsync(
                bggId,
                new HashSet<int>(),
                ct);
        }


        public async Task<bool> UpdateFromBggAsync(
            GameDto game,
            CancellationToken cancellationToken = default)
        {
            if (!game.BggId.HasValue)
            {
                _logger.LogWarning(
                    "Jogo '{GameId}' sem BGG ID. Atualização não possível.",
                    game.Id);

                return false;
            }

            var existingGame =
                await _gameRepository.GetGameByBggIdAsync(
                    game.BggId.Value,
                    cancellationToken);

            if (existingGame == null)
            {
                _logger.LogWarning(
                    "Jogo local com BGG ID '{BggId}' não encontrado.",
                    game.BggId.Value);

                return false;
            }

            existingGame.UpdateDetails(
                game.Name,
                game.Description,
                game.ImageUrl,
                game.SupportsSoloMode);

            existingGame.SetPlayerCount(
                game.MinPlayers,
                game.MaxPlayers);

            existingGame.SetCooperative(
                game.IsCooperative);

            existingGame.SetSupportsCampaign(
                game.SupportsCampaign);

            existingGame.SetBggRanking(
                game.BggRanking);

            existingGame.SetAverageRating(
                game.AverageRating);

            existingGame.SetUsersRatedCount(
                game.UsersRatedCount);

            await _gameRepository.UpdateAsync(
                existingGame,
                cancellationToken);

            await _gameRepository.CommitAsync(
                cancellationToken);

            _logger.LogInformation(
                "Jogo '{Name}' sincronizado com sucesso com o BGG.",
                existingGame.Name);

            return true;
        }
    }
}