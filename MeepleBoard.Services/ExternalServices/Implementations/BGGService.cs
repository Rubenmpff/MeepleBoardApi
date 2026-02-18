using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public class BGGService : IBGGService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BGGService> _logger;

    public BGGService(HttpClient httpClient, ILogger<BGGService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<GameDto?> GetGameByNameAsync(string gameName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return null;

        try
        {
            _logger.LogInformation("🔍 Buscando no BGG por nome: {GameName}", gameName);

            var results = await SearchGamesAsync(gameName, cancellationToken);
            if (!results.Any()) return null;

            var exact = results.FirstOrDefault(g =>
                string.Equals(g.Name.Trim(), gameName.Trim(), StringComparison.OrdinalIgnoreCase));

            return exact ?? results
                .OrderBy(g => LevenshteinDistance(g.Name.ToLowerInvariant(), gameName.ToLowerInvariant()))
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao buscar jogo por nome no BGG: {GameName}", gameName);
            return null;
        }
    }

    public async Task<List<GameDto>> SearchGamesAsync(string gameName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return new();

        try
        {
            // URL relativa (BaseAddress deve ser https://boardgamegeek.com/xmlapi2/)
            var url = $"search?query={Uri.EscapeDataString(gameName)}&type=boardgame,boardgameexpansion";
            var response = await GetWithRetryAsync(url, cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning("⚠️ Resposta inesperada ao buscar jogos: {Response}", response);
                return new();
            }

            var xml = XDocument.Parse(response);
            var items = xml.Descendants("item").ToList();
            if (!items.Any()) return new();

            var ids = items
                .Select(i => i.Attribute("id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (!ids.Any()) return new();

            // ⚡ Otimização: em vez de chamar /thing 1 a 1, faz bateladas
            // (mantém a tua intenção de não "martelar" a API)
            var games = new List<GameDto>();
            const int batchSize = 20;

            for (int i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).ToList();
                var batchGames = await GetGamesByIdsAsync(batch, cancellationToken);
                games.AddRange(batchGames);

                // pequeno delay entre batches para ser "BGG-friendly"
                if (i + batchSize < ids.Count)
                    await Task.Delay(600, cancellationToken);
            }

            return games;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao buscar lista de jogos no BGG.");
            return new();
        }
    }

    public async Task<GameDto?> GetGameByIdAsync(string gameId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return null;

        try
        {
            var url = $"thing?id={gameId}&stats=1";
            var response = await GetWithRetryAsync(url, cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning("⚠️ Resposta inesperada do BGG para ID {GameId}. Não é XML: {Response}", gameId, response);
                return null;
            }

            var xml = XDocument.Parse(response, LoadOptions.None);

            if (xml.Descendants("message").Any())
            {
                _logger.LogWarning("⚠️ Jogo com ID {GameId} ainda não disponível no BGG (/thing).", gameId);
                return null;
            }

            var item = xml.Descendants("item").FirstOrDefault();
            return item != null ? ParseGameItem(item, gameId) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao buscar jogo por ID no BGG ({GameId})", gameId);
            return null;
        }
    }

    public async Task<List<GameDto>> GetHotGamesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await GetWithRetryAsync("hot?type=boardgame", cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning("⚠️ Resposta inesperada da hot list do BGG: {Response}", response);
                return new();
            }

            var xml = XDocument.Parse(response);

            return xml.Descendants("item")
                .Select(item => new GameDto
                {
                    Name = item.Element("name")?.Attribute("value")?.Value ?? "Unknown",
                    ImageUrl = item.Element("thumbnail")?.Attribute("value")?.Value ?? "",
                    BggId = int.TryParse(item.Attribute("id")?.Value, out var id) ? id : null,
                    Description = "🔥 Popular game from BGG hot list",
                    IsExpansion = item.Attribute("type")?.Value == "boardgameexpansion"
                })
                .Where(g => g.BggId.HasValue)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao buscar jogos populares no BGG");
            return new();
        }
    }

    public async Task<List<GameDto>> GetGamesByIdsAsync(List<string> ids, CancellationToken cancellationToken = default)
    {
        if (ids == null || ids.Count == 0) return new();

        try
        {
            var cleanIds = ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (!cleanIds.Any()) return new();

            var url = $"thing?id={string.Join(",", cleanIds)}&stats=1&type=boardgame,boardgameexpansion";
            var response = await GetWithRetryAsync(url, cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning("⚠️ Resposta inesperada do BGG para GetGamesByIds");
                return new();
            }

            var xml = XDocument.Parse(response);

            return xml.Descendants("item")
                .Select(item => ParseGameItem(item, item.Attribute("id")?.Value))
                .OfType<GameDto>()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao buscar múltiplos jogos no BGG");
            return new();
        }
    }

    public async Task<List<GameSuggestionDto>> SearchGameSuggestionsAsync(
        string query,
        int offset = 0,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();

        try
        {
            var url = $"search?query={Uri.EscapeDataString(query)}&type=boardgame,boardgameexpansion";
            var response = await GetWithRetryAsync(url, cancellationToken);

            if (!IsXml(response))
            {
                _logger.LogWarning("⚠️ Resposta inesperada do BGG em SearchGameSuggestions: {Response}", response);
                return new();
            }

            var xml = XDocument.Parse(response);

            var allIds = xml.Descendants("item")
                .Select(i => i.Attribute("id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            // paginação segura
            var ids = allIds
                .Skip(Math.Max(0, offset))
                .Take(Math.Max(1, limit))
                .ToList();

            if (!ids.Any()) return new();

            var detailUrl = $"thing?id={string.Join(",", ids)}&stats=1";
            var detailResponse = await GetWithRetryAsync(detailUrl, cancellationToken);

            if (!IsXml(detailResponse))
            {
                _logger.LogWarning("⚠️ Resposta inesperada do BGG em SearchGameSuggestions (/thing): {Response}", detailResponse);
                return new();
            }

            var detailXml = XDocument.Parse(detailResponse);
            var detailItems = detailXml.Descendants("item");

            return detailItems
                .Select(item =>
                {
                    var idStr = item.Attribute("id")?.Value;
                    var name = item.Elements("name")
                        .FirstOrDefault(x => x.Attribute("type")?.Value == "primary")
                        ?.Attribute("value")?.Value;

                    var yearStr = item.Element("yearpublished")?.Attribute("value")?.Value;

                    // ⚠️ no XML API2, thumbnail/image são elementos com valor no texto (Value),
                    // mas por vezes vêm como <thumbnail>url</thumbnail>
                    var imageUrl = item.Element("thumbnail")?.Value;

                    var type = item.Attribute("type")?.Value;
                    var isExpansion = type == "boardgameexpansion";

                    if (int.TryParse(idStr, out var id) && !string.IsNullOrWhiteSpace(name))
                    {
                        return new GameSuggestionDto
                        {
                            BggId = id,
                            Name = name,
                            YearPublished = int.TryParse(yearStr, out var y) ? y : null,
                            ImageUrl = imageUrl,
                            IsExpansion = isExpansion
                        };
                    }

                    return null;
                })
                .Where(x => x != null)
                .ToList()!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro em SearchGameSuggestionsAsync");
            return new();
        }
    }

    private GameDto? ParseGameItem(XElement item, string? gameId)
    {
        try
        {
            var name = item.Descendants("name")
                .FirstOrDefault(n => n.Attribute("type")?.Value == "primary")?
                .Attribute("value")?.Value ?? "Unnamed";

            var rawDescription = item.Element("description")?.Value ?? "No description.";
            var cleanedDescription = WebUtility.HtmlDecode(Regex.Replace(rawDescription, "<.*?>", string.Empty));

            var imageUrl = item.Element("image")?.Value ?? "";

            var rankStr = item.Descendants("rank")
                .FirstOrDefault(r => r.Attribute("name")?.Value == "boardgame")?
                .Attribute("value")?.Value;

            int? bggRanking = (!string.IsNullOrWhiteSpace(rankStr) && rankStr != "Not Ranked")
                ? int.TryParse(rankStr, out var parsedRank) ? parsedRank : null
                : null;

            var avgStr = item.Descendants("average")?.FirstOrDefault()?.Attribute("value")?.Value;
            var avgRating = double.TryParse(avgStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var avg)
                ? avg : (double?)null;

            var weightStr = item.Descendants("averageweight")?.FirstOrDefault()?.Attribute("value")?.Value;
            var avgWeight = double.TryParse(weightStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var weight)
                ? weight : (double?)null;

            var yearStr = item.Element("yearpublished")?.Attribute("value")?.Value;
            int? yearPublished = int.TryParse(yearStr, out var parsedYear) ? parsedYear : null;

            int? minPlayers = int.TryParse(item.Element("minplayers")?.Attribute("value")?.Value, out var min) ? min : null;
            int? maxPlayers = int.TryParse(item.Element("maxplayers")?.Attribute("value")?.Value, out var max) ? max : null;

            var type = item.Attribute("type")?.Value;
            bool isExpansion = type == "boardgameexpansion";

            var expansionLink = item.Elements("link")
                .FirstOrDefault(l =>
                    l.Attribute("type")?.Value == "boardgameexpansion" &&
                    l.Attribute("inbound")?.Value == "true");

            int? baseGameBggId = null;
            if (expansionLink != null && int.TryParse(expansionLink.Attribute("id")?.Value, out var parsedBaseId))
            {
                baseGameBggId = parsedBaseId;
                isExpansion = true;
            }

            if (!isExpansion && IsPossiblyExpansion(cleanedDescription))
            {
                isExpansion = true;
                _logger.LogWarning("🟡 Heurística: '{Name}' parece ser expansão mas não está marcada no XML.", name);
            }

            if (!int.TryParse(gameId, out var parsedBggId))
            {
                _logger.LogWarning("⚠️ ID inválido vindo do BGG: '{GameId}'", gameId);
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
                Categories = item.Elements("link")
                    .Where(x => x.Attribute("type")?.Value == "boardgamecategory")
                    .Select(x => x.Attribute("value")?.Value)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct()
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao parsear item XML do BGG (ID: {GameId})", gameId);
            return null;
        }
    }

    private async Task<string> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        const int maxRetries = 6;
        var delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            HttpResponseMessage? response = null;

            try
            {
                response = await _httpClient.GetAsync(url, cancellationToken);

                // 401: token/config errado (não adianta retry infinito)
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var body401 = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("❌ BGG 401 Unauthorized. Confere o Bearer token e BaseAddress. Body: {Body}", body401);
                    response.EnsureSuccessStatusCode();
                }

                // 429: too many requests
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    _logger.LogWarning("⏳ [Tentativa {Attempt}] BGG retornou 429. Aguardando {Delay}s...", attempt, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                    continue;
                }

                // 202: Accepted / a gerar resultado (muito comum em alguns endpoints)
                if (response.StatusCode == HttpStatusCode.Accepted)
                {
                    _logger.LogInformation("⏳ [Tentativa {Attempt}] BGG retornou 202 (Accepted). Aguardando {Delay}s...", attempt, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 15));
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "⚠️ [Tentativa {Attempt}] Falha ao requisitar {Url}. Repetindo...", attempt, url);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
            finally
            {
                response?.Dispose();
            }
        }

        throw new HttpRequestException($"❌ Todas as tentativas falharam para: {url}");
    }

    private static bool IsXml(string? s)
        => !string.IsNullOrWhiteSpace(s) && s.TrimStart().StartsWith("<");

    private bool IsPossiblyExpansion(string description)
    {
        var lowerDesc = description.ToLowerInvariant();
        return lowerDesc.Contains("expands") && lowerDesc.Contains("expansion");
    }

    private int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];

        for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= t.Length; j++) d[0, j] = j;

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s.Length, t.Length];
    }
}
