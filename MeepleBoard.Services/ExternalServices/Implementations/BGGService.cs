using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public class BGGService : IBGGService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BGGService> _logger;
    private readonly IMemoryCache _cache;

    // Cache PARTILHADA por todos os utilizadores — se a Ana pesquisar "Wingspan"
    // agora, o Bruno que pesquisar "Wingspan" daqui a 1 hora recebe a resposta
    // guardada, sem ires ao BGG outra vez. Isto é o que protege o BGG quando há
    // muita gente a usar a app ao mesmo tempo (o debounce no telemóvel de cada
    // pessoa só protege contra ELA PRÓPRIA pedir demasiado, não contra muita
    // gente diferente a pesquisar coisas diferentes ao mesmo tempo).
    private static readonly TimeSpan SuggestionsCacheTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan HotGamesCacheTtl = TimeSpan.FromHours(6);

    private const string CooperativeMechanic = "Cooperative Game";

    // Mecânicas/categoria do BGG que indicam que o jogo é (tipicamente) jogado em campanha
    private static readonly string[] CampaignMechanics = { "Legacy Game", "Campaign / Battle Card Driven" };
    private const string CampaignCategory = "Campaign Games";

    /// <summary>Deteta se o item do BGG (XML) tem mecânica/categoria de campanha/legacy.</summary>
    private static bool DetectSupportsCampaign(XElement item)
    {
        return item.Elements("link").Any(l =>
            (l.Attribute("type")?.Value == "boardgamemechanic" &&
             CampaignMechanics.Contains(l.Attribute("value")?.Value)) ||
            (l.Attribute("type")?.Value == "boardgamecategory" &&
             l.Attribute("value")?.Value == CampaignCategory));
    }

    public BGGService(HttpClient httpClient, ILogger<BGGService> logger, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
    }

    public async Task<GameDto?> GetGameByNameAsync(
        string gameName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameName))
            return null;

        try
        {
            _logger.LogInformation("🔍 Buscando no BGG por nome: {GameName}", gameName);

            var results = await SearchGamesAsync(gameName, cancellationToken);
            if (!results.Any())
                return null;

            var exact = results.FirstOrDefault(g =>
                string.Equals(
                    g.Name.Trim(),
                    gameName.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            return exact ?? results
                .OrderBy(g =>
                    LevenshteinDistance(
                        g.Name.ToLowerInvariant(),
                        gameName.ToLowerInvariant()))
                .FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Erro ao buscar jogo por nome no BGG: {GameName}",
                gameName);

            return null;
        }
    }

    public async Task<List<GameDto>> SearchGamesAsync(
        string gameName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameName))
            return new();

        try
        {
            // URL relativa (BaseAddress deve ser https://boardgamegeek.com/xmlapi2/)
            var url =
                $"search?query={Uri.EscapeDataString(gameName)}&type=boardgame,boardgameexpansion";

            var response =
                await GetWithRetryAsync(url, cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning(
                    "⚠️ Resposta inesperada ao buscar jogos: {Response}",
                    response);

                return new();
            }

            var xml = XDocument.Parse(response);
            var items = xml.Descendants("item").ToList();

            if (!items.Any())
                return new();

            var ids = items
                .Select(i => i.Attribute("id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct()
                .ToList();

            if (!ids.Any())
                return new();

            // ⚡ Otimização: em vez de chamar /thing 1 a 1, faz bateladas
            // (mantém a tua intenção de não "martelar" a API)
            var games = new List<GameDto>();
            const int batchSize = 20;

            for (int i = 0; i < ids.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = ids
                    .Skip(i)
                    .Take(batchSize)
                    .ToList();

                var batchGames =
                    await GetGamesByIdsAsync(batch, cancellationToken);

                games.AddRange(batchGames);

                // pequeno delay entre batches para ser "BGG-friendly"
                if (i + batchSize < ids.Count)
                    await Task.Delay(600, cancellationToken);
            }

            return games;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Erro ao buscar lista de jogos no BGG.");

            return new();
        }
    }

    public async Task<GameDto?> GetGameByIdAsync(
        string gameId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        try
        {
            var url = $"thing?id={gameId}&stats=1";
            var response =
                await GetWithRetryAsync(url, cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning(
                    "⚠️ Resposta inesperada do BGG para ID {GameId}. Não é XML: {Response}",
                    gameId,
                    response);

                return null;
            }

            var xml = XDocument.Parse(response, LoadOptions.None);

            if (xml.Descendants("message").Any())
            {
                _logger.LogWarning(
                    "⚠️ Jogo com ID {GameId} ainda não disponível no BGG (/thing).",
                    gameId);

                return null;
            }

            var item = xml.Descendants("item").FirstOrDefault();

            return item != null
                ? ParseGameItem(item, gameId)
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Erro ao buscar jogo por ID no BGG ({GameId})",
                gameId);

            return null;
        }
    }

    public async Task<List<GameDto>> GetHotGamesAsync(
        CancellationToken cancellationToken = default)
    {
        const string cacheKey = "bgg:hot-games";

        if (_cache.TryGetValue(
                cacheKey,
                out List<GameDto>? cached) &&
            cached != null)
        {
            _logger.LogInformation(
                "💾 Cache HIT para jogos em destaque — não foi preciso ir ao BGG.");

            return cached;
        }

        try
        {
            var response =
                await GetWithRetryAsync(
                    "hot?type=boardgame",
                    cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning(
                    "⚠️ Resposta inesperada da hot list do BGG: {Response}",
                    response);

                return new();
            }

            var xml = XDocument.Parse(response);

            var hotGames = xml.Descendants("item")
                .Select(item => new GameDto
                {
                    Name =
                        item.Element("name")
                            ?.Attribute("value")
                            ?.Value
                        ?? "Unknown",

                    ImageUrl =
                        item.Element("thumbnail")
                            ?.Attribute("value")
                            ?.Value
                        ?? "",

                    BggId =
                        int.TryParse(
                            item.Attribute("id")?.Value,
                            out var id)
                            ? id
                            : null,

                    Description =
                        "🔥 Popular game from BGG hot list",

                    IsExpansion =
                        item.Attribute("type")?.Value ==
                        "boardgameexpansion"
                })
                .Where(g => g.BggId.HasValue)
                .ToList();

            if (hotGames.Count > 0)
                _cache.Set(
                    cacheKey,
                    hotGames,
                    HotGamesCacheTtl);

            return hotGames;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Erro ao buscar jogos populares no BGG");

            return new();
        }
    }

    public async Task<List<GameDto>> GetGamesByIdsAsync(
        List<string> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Count == 0)
            return new();

        try
        {
            var cleanIds = ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (!cleanIds.Any())
                return new();

            var url =
                $"thing?id={string.Join(",", cleanIds)}&stats=1&type=boardgame,boardgameexpansion";

            var response =
                await GetWithRetryAsync(url, cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning(
                    "⚠️ Resposta inesperada do BGG para GetGamesByIds");

                return new();
            }

            var xml = XDocument.Parse(response);

            return xml.Descendants("item")
                .Select(item =>
                    ParseGameItem(
                        item,
                        item.Attribute("id")?.Value))
                .OfType<GameDto>()
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Erro ao buscar múltiplos jogos no BGG");

            return new();
        }
    }

    public async Task<List<GameSuggestionDto>> SearchGameSuggestionsAsync(
        string query,
        int offset = 0,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new();

        var normalizedQuery =
            NormalizeSearchText(query);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return new();

        // A cache inclui query + offset + limit, para não misturar páginas/resultados.
        var cacheKey =
            $"bgg:suggestions:{normalizedQuery}:{offset}:{limit}";

        if (_cache.TryGetValue(
                cacheKey,
                out List<GameSuggestionDto>? cached) &&
            cached != null)
        {
            _logger.LogInformation(
                "💾 Cache HIT para pesquisa \"{Query}\" — não foi preciso ir ao BGG.",
                normalizedQuery);

            return cached;
        }

        var result =
            await SearchGameSuggestionsFromBggAsync(
                query,
                offset,
                limit,
                cancellationToken);

        // Só guarda resultados válidos.
        // Uma resposta vazia causada por erro do BGG não deve "envenenar" a cache.
        if (result.Count > 0)
        {
            _cache.Set(
                cacheKey,
                result,
                SuggestionsCacheTtl);
        }

        return result;
    }

    private async Task<List<GameSuggestionDto>> SearchGameSuggestionsFromBggAsync(
        string query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedQuery = NormalizeSearchText(query);

            if (string.IsNullOrWhiteSpace(normalizedQuery))
                return new();

            var safeOffset = Math.Max(0, offset);
            var safeLimit = Math.Clamp(limit, 1, 50);

            // -----------------------------------------------------------------
            // 1) /search do BGG: descobrir TODOS os candidatos e os respetivos
            //    nomes. O próprio /search já traz o nome primário e o ano.
            //
            //    IMPORTANTE:
            //    Não confiamos na ordem bruta do BGG.
            //    Primeiro ordenamos os candidatos pelo texto da pesquisa.
            // -----------------------------------------------------------------
            var url =
                $"search?query={Uri.EscapeDataString(query.Trim())}&type=boardgame,boardgameexpansion";

            var response =
                await GetWithRetryAsync(url, cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning(
                    "⚠️ Resposta inesperada do BGG em SearchGameSuggestions: {Response}",
                    response);

                return new();
            }

            var xml = XDocument.Parse(response);

            var searchCandidates = xml
                .Descendants("item")
                .Select(item =>
                {
                    var idStr = item.Attribute("id")?.Value;

                    var primaryName = item
                        .Elements("name")
                        .FirstOrDefault(n =>
                            n.Attribute("type")?.Value == "primary")
                        ?.Attribute("value")
                        ?.Value;

                    // Em alguns resultados o BGG pode não marcar explicitamente
                    // o name como primary; usamos o primeiro nome como fallback.
                    primaryName ??= item
                        .Elements("name")
                        .FirstOrDefault()
                        ?.Attribute("value")
                        ?.Value;

                    if (string.IsNullOrWhiteSpace(idStr) ||
                        string.IsNullOrWhiteSpace(primaryName))
                    {
                        return null;
                    }

                    return new
                    {
                        Id = idStr,
                        Name = primaryName,
                        NormalizedName = NormalizeSearchText(primaryName),
                        TextScore = CalculateSearchCandidateTextScore(
                            primaryName,
                            normalizedQuery)
                    };
                })
                .Where(x => x != null)
                .GroupBy(x => x!.Id)
                .Select(g => g.First()!)
                .Where(x => x.TextScore > double.MinValue)
                .ToList();

            if (searchCandidates.Count == 0)
                return new();

            /*
             * Aqui está a diferença importante em relação à versão anterior:
             *
             * ANTES:
             *   pegávamos nos primeiros 40/80 IDs que o BGG devolvia.
             *
             * AGORA:
             *   ordenamos TODOS os resultados leves do /search pelo nome
             *   e só depois escolhemos os candidatos que merecem ir ao /thing.
             *
             * Assim "ro" favorece:
             *   Root
             *   Robinson Crusoe
             *   Roll for the Galaxy
             *   Roll Player
             *   Rococo
             *   RoboRally
             *
             * em vez de jogos onde "ro" aparece perdido no meio do nome.
             */
            var requiredResults = safeOffset + safeLimit;

            var candidatePoolSize = Math.Clamp(
                Math.Max(60, requiredResults + 40),
                60,
                100);

            var candidateIds = searchCandidates
                .OrderByDescending(x => x.TextScore)
                .ThenBy(x => x.NormalizedName.Length)
                .ThenBy(x => x.NormalizedName)
                .Take(candidatePoolSize)
                .Select(x => x.Id)
                .ToList();

            if (candidateIds.Count == 0)
                return new();

            // -----------------------------------------------------------------
            // 2) /thing: só agora vamos buscar stats/popularidade dos melhores
            //    candidatos textuais.
            // -----------------------------------------------------------------
            var suggestions = new List<GameSuggestionDto>();

            const int batchSize = 20;

            for (var i = 0;
                 i < candidateIds.Count;
                 i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = candidateIds
                    .Skip(i)
                    .Take(batchSize)
                    .ToList();

                var detailUrl =
                    $"thing?id={string.Join(",", batch)}&stats=1&type=boardgame,boardgameexpansion";

                var detailResponse =
                    await GetWithRetryAsync(
                        detailUrl,
                        cancellationToken);

                if (!IsXml(detailResponse))
                {
                    _logger.LogWarning(
                        "⚠️ Resposta inesperada do BGG em SearchGameSuggestions (/thing)");

                    continue;
                }

                var detailXml =
                    XDocument.Parse(detailResponse);

                foreach (var item in detailXml.Descendants("item"))
                {
                    var suggestion =
                        ParseGameSuggestionItem(item);

                    if (suggestion != null)
                        suggestions.Add(suggestion);
                }
            }

            if (suggestions.Count == 0)
                return new();

            // -----------------------------------------------------------------
            // 3) Ranking final MeepleBoard:
            //    texto + popularidade + jogo base/expansão + rating.
            // -----------------------------------------------------------------
            var rankedResults = suggestions
                .Select(game => new
                {
                    Game = game,
                    Score = CalculateSearchScore(
                        game,
                        normalizedQuery)
                })
                .Where(x => x.Score > double.MinValue)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Game.RatingsCount ?? 0)
                .ThenBy(x => x.Game.IsExpansion)
                .ThenBy(x => x.Game.Name)
                .Skip(safeOffset)
                .Take(safeLimit)
                .Select(x => x.Game)
                .ToList();

            _logger.LogInformation(
                "🔎 BGG Search '{Query}': {SearchCount} resultados leves -> {CandidateCount} candidatos detalhados -> {ResultCount} resultados finais.",
                query,
                searchCandidates.Count,
                suggestions.Count,
                rankedResults.Count);

            return rankedResults;
        }
        catch (OperationCanceledException)
        {
            // Se o utilizador continuar a escrever, o pedido anterior pode ser
            // cancelado pelo frontend. Isso é comportamento normal.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Erro em SearchGameSuggestionsFromBggAsync para '{Query}'",
                query);

            return new();
        }
    }

    /// <summary>
    /// Score textual BARATO usado antes de chamar /thing.
    /// Trabalha apenas com o nome que já vem no /search do BGG.
    ///
    /// O objetivo é evitar gastar pedidos de detalhes em resultados como:
    /// "1830: Railways & Robber Barons"
    /// quando a pesquisa é "ro" e existem candidatos como "Root",
    /// "Robinson Crusoe", "Roll Player" ou "Rococo".
    /// </summary>
    private static double CalculateSearchCandidateTextScore(
        string gameName,
        string normalizedQuery)
    {
        var normalizedName = NormalizeSearchText(gameName);

        if (string.IsNullOrWhiteSpace(normalizedName))
            return double.MinValue;

        // Match exato.
        if (normalizedName == normalizedQuery)
            return 1_000_000;

        // Começa pelo texto pesquisado.
        if (normalizedName.StartsWith(
                normalizedQuery,
                StringComparison.Ordinal))
        {
            // Nomes mais curtos/próximos recebem ligeira vantagem.
            return 500_000 -
                   Math.Max(
                       0,
                       normalizedName.Length -
                       normalizedQuery.Length) * 20;
        }

        // Alguma palavra começa pelo termo.
        if (StartsAnyWordWith(
                normalizedName,
                normalizedQuery))
        {
            var wordIndex = FindWordPrefixPosition(
                normalizedName,
                normalizedQuery);

            return 250_000 -
                   Math.Max(0, wordIndex) * 100;
        }

        // Contém o texto noutra posição.
        var containsIndex = normalizedName.IndexOf(
            normalizedQuery,
            StringComparison.Ordinal);

        if (containsIndex >= 0)
        {
            return 100_000 -
                   containsIndex * 500;
        }

        return double.MinValue;
    }

    private static int FindWordPrefixPosition(
        string normalizedName,
        string normalizedQuery)
    {
        var words = normalizedName.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].StartsWith(
                    normalizedQuery,
                    StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private GameSuggestionDto? ParseGameSuggestionItem(
        XElement item)
    {
        var idStr =
            item.Attribute("id")?.Value;

        var name = item.Elements("name")
            .FirstOrDefault(
                x => x.Attribute("type")?.Value ==
                     "primary")
            ?.Attribute("value")
            ?.Value;

        if (!int.TryParse(idStr, out var id) ||
            string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var yearStr =
            item.Element("yearpublished")
                ?.Attribute("value")
                ?.Value;

        var imageUrl =
            item.Element("thumbnail")?.Value;

        var type =
            item.Attribute("type")?.Value;

        var isExpansion =
            string.Equals(
                type,
                "boardgameexpansion",
                StringComparison.OrdinalIgnoreCase);

        int? minPlayers =
            int.TryParse(
                item.Element("minplayers")
                    ?.Attribute("value")
                    ?.Value,
                out var minP)
                ? minP
                : null;

        int? maxPlayers =
            int.TryParse(
                item.Element("maxplayers")
                    ?.Attribute("value")
                    ?.Value,
                out var maxP)
                ? maxP
                : null;

        var supportsSoloMode =
            minPlayers.HasValue &&
            minPlayers.Value == 1;

        var isCooperative =
            item.Elements("link")
                .Any(l =>
                    l.Attribute("type")?.Value ==
                    "boardgamemechanic" &&
                    l.Attribute("value")?.Value ==
                    CooperativeMechanic);

        var supportsCampaign =
            DetectSupportsCampaign(item);

        var avgStr =
            item.Descendants("average")
                .FirstOrDefault()
                ?.Attribute("value")
                ?.Value;

        double? averageRating =
            double.TryParse(
                avgStr,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var avg)
            && avg > 0
                ? avg
                : null;

        var usersRatedStr =
            item.Descendants("usersrated")
                .FirstOrDefault()
                ?.Attribute("value")
                ?.Value;

        int? ratingsCount =
            int.TryParse(
                usersRatedStr,
                out var usersRated)
                ? usersRated
                : null;

        return new GameSuggestionDto
        {
            BggId = id,
            Name = name,
            YearPublished =
                int.TryParse(yearStr, out var year)
                    ? year
                    : null,

            ImageUrl = imageUrl,
            IsExpansion = isExpansion,
            MinPlayers = minPlayers,
            MaxPlayers = maxPlayers,
            SupportsSoloMode = supportsSoloMode,
            IsCooperative = isCooperative,
            SupportsCampaign = supportsCampaign,
            AverageRating = averageRating,
            RatingsCount = ratingsCount
        };
    }

    private static double CalculateSearchScore(
        GameSuggestionDto game,
        string normalizedQuery)
    {
        var normalizedName =
            NormalizeSearchText(game.Name);

        if (string.IsNullOrWhiteSpace(normalizedName))
            return double.MinValue;

        double score = 0;

        // 1. Relevância textual
        if (normalizedName == normalizedQuery)
        {
            // Ex.: "root" -> Root
            score += 100_000;
        }
        else if (normalizedName.StartsWith(
                     normalizedQuery,
                     StringComparison.Ordinal))
        {
            // Ex.: "roo" -> Root
            score += 50_000;

            // Entre vários prefix matches, favorece o nome mais curto/próximo.
            score -= Math.Max(
                0,
                normalizedName.Length -
                normalizedQuery.Length) * 20;
        }
        else if (StartsAnyWordWith(
                     normalizedName,
                     normalizedQuery))
        {
            // Ex.: "galaxy" -> Roll for the Galaxy
            score += 25_000;
        }
        else if (normalizedName.Contains(
                     normalizedQuery,
                     StringComparison.Ordinal))
        {
            score += 10_000;

            var position =
                normalizedName.IndexOf(
                    normalizedQuery,
                    StringComparison.Ordinal);

            // Quanto mais cedo surgir o termo, melhor.
            score -= position * 50;
        }
        else
        {
            // Por agora não fazemos fuzzy agressivo nesta pesquisa.
            return double.MinValue;
        }

        // 2. Jogo base vs expansão
        if (game.IsExpansion)
            score -= 2_500;
        else
            score += 1_000;

        // 3. Popularidade
        if (game.RatingsCount is > 0)
        {
            /*
             * Crescimento logarítmico:
             * a popularidade ajuda bastante,
             * mas não deve ultrapassar um resultado textual claramente melhor.
             */
            score +=
                Math.Log10(
                    game.RatingsCount.Value + 1)
                * 600;
        }

        // 4. Rating apenas como pequeno desempate
        if (game.AverageRating is > 0)
        {
            score +=
                game.AverageRating.Value * 25;
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
            return string.Empty;

        /*
         * Normalização:
         * - trim
         * - lowercase
         * - remove acentos
         * - comprime espaços
         *
         * Ex.:
         * "  Pokémon   Café " -> "pokemon cafe"
         */
        var normalized =
            value.Trim()
                .ToLowerInvariant()
                .Normalize(
                    NormalizationForm.FormD);

        var builder =
            new StringBuilder(
                normalized.Length);

        foreach (var character in normalized)
        {
            var category =
                CharUnicodeInfo.GetUnicodeCategory(
                    character);

            if (category !=
                UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return Regex.Replace(
                builder
                    .ToString()
                    .Normalize(
                        NormalizationForm.FormC),
                @"\s+",
                " ")
            .Trim();
    }

    private GameDto? ParseGameItem(
        XElement item,
        string? gameId)
    {
        try
        {
            var name = item.Descendants("name")
                .FirstOrDefault(
                    n => n.Attribute("type")?.Value ==
                         "primary")
                ?.Attribute("value")
                ?.Value
                ?? "Unnamed";

            var rawDescription =
                item.Element("description")?.Value
                ?? "No description.";

            var cleanedDescription =
                WebUtility.HtmlDecode(
                    Regex.Replace(
                        rawDescription,
                        "<.*?>",
                        string.Empty));

            var imageUrl =
                item.Element("image")?.Value
                ?? "";

            var rankStr =
                item.Descendants("rank")
                    .FirstOrDefault(
                        r => r.Attribute("name")?.Value ==
                             "boardgame")
                    ?.Attribute("value")
                    ?.Value;

            int? bggRanking =
                !string.IsNullOrWhiteSpace(rankStr) &&
                rankStr != "Not Ranked"
                    ? int.TryParse(
                        rankStr,
                        out var parsedRank)
                        ? parsedRank
                        : null
                    : null;

            var avgStr =
                item.Descendants("average")
                    .FirstOrDefault()
                    ?.Attribute("value")
                    ?.Value;

            var avgRating =
                double.TryParse(
                    avgStr,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var avg)
                    ? avg
                    : (double?)null;

            var weightStr =
                item.Descendants("averageweight")
                    .FirstOrDefault()
                    ?.Attribute("value")
                    ?.Value;

            var avgWeight =
                double.TryParse(
                    weightStr,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var weight)
                    ? weight
                    : (double?)null;

            var usersRatedStr =
                item.Descendants("usersrated")
                    .FirstOrDefault()
                    ?.Attribute("value")
                    ?.Value;

            var usersRatedCount =
                int.TryParse(
                    usersRatedStr,
                    out var ur)
                    ? ur
                    : (int?)null;

            var yearStr =
                item.Element("yearpublished")
                    ?.Attribute("value")
                    ?.Value;

            int? yearPublished =
                int.TryParse(
                    yearStr,
                    out var parsedYear)
                    ? parsedYear
                    : null;

            int? minPlayers =
                int.TryParse(
                    item.Element("minplayers")
                        ?.Attribute("value")
                        ?.Value,
                    out var min)
                    ? min
                    : null;

            int? maxPlayers =
                int.TryParse(
                    item.Element("maxplayers")
                        ?.Attribute("value")
                        ?.Value,
                    out var max)
                    ? max
                    : null;

            bool supportsSoloMode =
                minPlayers.HasValue &&
                minPlayers.Value == 1;

            bool isCooperative =
                item.Elements("link")
                    .Any(l =>
                        l.Attribute("type")?.Value ==
                        "boardgamemechanic" &&
                        l.Attribute("value")?.Value ==
                        CooperativeMechanic);

            bool supportsCampaign =
                DetectSupportsCampaign(item);

            var type =
                item.Attribute("type")?.Value;

            bool isExpansion =
                type == "boardgameexpansion";

            var expansionLink =
                item.Elements("link")
                    .FirstOrDefault(l =>
                        l.Attribute("type")?.Value ==
                        "boardgameexpansion" &&
                        l.Attribute("inbound")?.Value ==
                        "true");

            int? baseGameBggId = null;

            if (expansionLink != null &&
                int.TryParse(
                    expansionLink.Attribute("id")?.Value,
                    out var parsedBaseId))
            {
                baseGameBggId = parsedBaseId;
                isExpansion = true;
            }

            if (!isExpansion &&
                IsPossiblyExpansion(cleanedDescription))
            {
                isExpansion = true;

                _logger.LogWarning(
                    "🟡 Heurística: '{Name}' parece ser expansão mas não está marcada no XML.",
                    name);
            }

            if (!int.TryParse(
                    gameId,
                    out var parsedBggId))
            {
                _logger.LogWarning(
                    "⚠️ ID inválido vindo do BGG: '{GameId}'",
                    gameId);

                return null;
            }

            return new GameDto
            {
                Id = Guid.Empty,
                Name = name,
                Description = cleanedDescription,
                ImageUrl = imageUrl,
                BggId = parsedBggId,
                BggRanking = bggRanking,
                AverageRating = avgRating,
                YearPublished = yearPublished,
                IsExpansion = isExpansion,
                BaseGameBggId = baseGameBggId,
                BaseGameId = null,
                MinPlayers = minPlayers,
                MaxPlayers = maxPlayers,
                AverageWeight = avgWeight,
                UsersRatedCount = usersRatedCount,
                SupportsSoloMode = supportsSoloMode,
                IsCooperative = isCooperative,
                SupportsCampaign = supportsCampaign,

                Categories =
                    item.Elements("link")
                        .Where(
                            x => x.Attribute("type")?.Value ==
                                 "boardgamecategory")
                        .Select(
                            x => x.Attribute("value")?.Value)
                        .Where(
                            x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .Distinct()
                        .ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Erro ao parsear item XML do BGG (ID: {GameId})",
                gameId);

            return null;
        }
    }

    private async Task<string> GetWithRetryAsync(
        string url,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 6;
        var delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1;
             attempt <= maxRetries;
             attempt++)
        {
            HttpResponseMessage? response = null;

            try
            {
                response =
                    await _httpClient.GetAsync(
                        url,
                        cancellationToken);

                // 401: token/config errado (não adianta retry infinito)
                if (response.StatusCode ==
                    HttpStatusCode.Unauthorized)
                {
                    var body401 =
                        await response.Content
                            .ReadAsStringAsync(
                                cancellationToken);

                    _logger.LogError(
                        "❌ BGG 401 Unauthorized. Confere o Bearer token e BaseAddress. Body: {Body}",
                        body401);

                    response.EnsureSuccessStatusCode();
                }

                // 429: too many requests
                if (response.StatusCode ==
                    (HttpStatusCode)429)
                {
                    _logger.LogWarning(
                        "⏳ [Tentativa {Attempt}] BGG retornou 429. Aguardando {Delay}s...",
                        attempt,
                        delay.TotalSeconds);

                    await Task.Delay(
                        delay,
                        cancellationToken);

                    delay = TimeSpan.FromSeconds(
                        Math.Min(
                            delay.TotalSeconds * 2,
                            30));

                    continue;
                }

                // 202: Accepted / a gerar resultado
                if (response.StatusCode ==
                    HttpStatusCode.Accepted)
                {
                    _logger.LogInformation(
                        "⏳ [Tentativa {Attempt}] BGG retornou 202 (Accepted). Aguardando {Delay}s...",
                        attempt,
                        delay.TotalSeconds);

                    await Task.Delay(
                        delay,
                        cancellationToken);

                    delay = TimeSpan.FromSeconds(
                        Math.Min(
                            delay.TotalSeconds * 1.5,
                            15));

                    continue;
                }

                response.EnsureSuccessStatusCode();

                return await response.Content
                    .ReadAsStringAsync(
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
                when (attempt < maxRetries)
            {
                _logger.LogWarning(
                    ex,
                    "⚠️ [Tentativa {Attempt}] Falha ao requisitar {Url}. Repetindo...",
                    attempt,
                    url);

                await Task.Delay(
                    delay,
                    cancellationToken);

                delay = TimeSpan.FromSeconds(
                    Math.Min(
                        delay.TotalSeconds * 2,
                        30));
            }
            finally
            {
                response?.Dispose();
            }
        }

        throw new HttpRequestException(
            $"❌ Todas as tentativas falharam para: {url}");
    }

    private static bool IsXml(string? s)
        => !string.IsNullOrWhiteSpace(s) &&
           s.TrimStart().StartsWith("<");

    private bool IsPossiblyExpansion(
        string description)
    {
        var lowerDesc =
            description.ToLowerInvariant();

        return lowerDesc.Contains("expands") &&
               lowerDesc.Contains("expansion");
    }

    private int LevenshteinDistance(
        string s,
        string t)
    {
        if (string.IsNullOrEmpty(s))
            return t.Length;

        if (string.IsNullOrEmpty(t))
            return s.Length;

        var d =
            new int[
                s.Length + 1,
                t.Length + 1];

        for (int i = 0; i <= s.Length; i++)
            d[i, 0] = i;

        for (int j = 0; j <= t.Length; j++)
            d[0, j] = j;

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
            {
                var cost =
                    s[i - 1] == t[j - 1]
                        ? 0
                        : 1;

                d[i, j] = Math.Min(
                    Math.Min(
                        d[i - 1, j] + 1,
                        d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s.Length, t.Length];
    }
}