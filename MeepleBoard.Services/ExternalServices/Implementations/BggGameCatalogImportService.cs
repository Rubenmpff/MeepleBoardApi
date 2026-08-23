using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.ExternalServices.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MeepleBoard.Services.ExternalServices.Implementations
{
    public class BggGameCatalogImportService
        : IBggGameCatalogImportService
    {
        private const int ImportBatchSize = 5000;

        private readonly HttpClient _httpClient;
        private readonly IGameSearchCatalogRepository _catalogRepository;
        private readonly ILogger<BggGameCatalogImportService> _logger;

        public BggGameCatalogImportService(
            HttpClient httpClient,
            IGameSearchCatalogRepository catalogRepository,
            ILogger<BggGameCatalogImportService> logger)
        {
            _httpClient = httpClient;
            _catalogRepository = catalogRepository;
            _logger = logger;
        }

        public async Task<int> ImportAsync(
            CancellationToken cancellationToken = default)
        {
            if (_httpClient.BaseAddress == null)
            {
                throw new InvalidOperationException(
                    "Bgg:CatalogPageUrl não está configurado.");
            }

            _logger.LogInformation(
                "A iniciar importação do catálogo BGG.");

            using var initialResponse =
                await _httpClient.GetAsync(
                    string.Empty,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            initialResponse.EnsureSuccessStatusCode();

            /*
             * bg_ranks normalmente é uma página que contém
             * o link para o dump atual.
             *
             * Também suportamos a possibilidade de a configuração
             * apontar diretamente para CSV/ZIP.
             */
            if (IsHtml(initialResponse))
            {
                var html =
                    await initialResponse.Content.ReadAsStringAsync(
                        cancellationToken);

                var pageUri =
                    initialResponse.RequestMessage?.RequestUri
                    ?? _httpClient.BaseAddress;

                var dumpUri =
                    FindDumpUri(
                        html,
                        pageUri);

                if (dumpUri == null)
                {
                    throw new InvalidOperationException(
                        "Não foi possível localizar o ficheiro de rankings " +
                        "na página de data dumps do BoardGameGeek.");
                }

                _logger.LogInformation(
                    "Data dump BGG localizado: {DumpUri}",
                    dumpUri.GetLeftPart(UriPartial.Path));

                using var dumpResponse =
                    await _httpClient.GetAsync(
                        dumpUri,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                dumpResponse.EnsureSuccessStatusCode();

                return await ImportResponseAsync(
                    dumpResponse,
                    cancellationToken);
            }

            return await ImportResponseAsync(
                initialResponse,
                cancellationToken);
        }

        public async Task<int> ImportFromFileAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(
                    "O caminho do ficheiro é obrigatório.",
                    nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "O ficheiro de catálogo BGG não foi encontrado.",
                    filePath);
            }

            var extension =
                Path.GetExtension(filePath);

            if (!extension.Equals(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(
                    ".csv",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "O ficheiro de catálogo BGG tem de ser .zip ou .csv.");
            }

            _logger.LogInformation(
                "A iniciar importação local do catálogo BGG: {FilePath}",
                filePath);

            await using var stream =
                new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    useAsync: true);

            if (extension.Equals(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                return await ImportZipAsync(
                    stream,
                    cancellationToken);
            }

            return await ImportCsvAsync(
                stream,
                cancellationToken);
        }

        private async Task<int> ImportResponseAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            if (IsHtml(response))
            {
                throw new InvalidOperationException(
                    "O BGG devolveu HTML quando era esperado CSV ou ZIP.");
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            if (IsZip(response))
            {
                return await ImportZipAsync(
                    stream,
                    cancellationToken);
            }

            return await ImportCsvAsync(
                stream,
                cancellationToken);
        }

        private async Task<int> ImportZipAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            using var archive =
                new ZipArchive(
                    stream,
                    ZipArchiveMode.Read,
                    leaveOpen: false);

            var csvEntry =
                archive.Entries
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Name) &&
                        x.Name.EndsWith(
                            ".csv",
                            StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x =>
                        x.Name.Contains(
                            "rank",
                            StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

            if (csvEntry == null)
            {
                throw new InvalidOperationException(
                    "O ZIP do BoardGameGeek não contém um ficheiro CSV.");
            }

            _logger.LogInformation(
                "CSV encontrado no dump BGG: {FileName}",
                csvEntry.FullName);

            await using var csvStream =
                csvEntry.Open();

            return await ImportCsvAsync(
                csvStream,
                cancellationToken);
        }

        private async Task<int> ImportCsvAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            using var reader =
                new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 64 * 1024,
                    leaveOpen: false);

            var headerLine =
                await reader.ReadLineAsync(
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(headerLine))
            {
                throw new InvalidOperationException(
                    "O CSV BGG está vazio.");
            }

            if (LooksLikeHtml(headerLine))
            {
                throw new InvalidOperationException(
                    "A resposta recebida não é um CSV válido.");
            }

            var headers =
                BuildHeaderMap(
                    headerLine);

            var idColumn =
                GetRequiredColumn(
                    headers,
                    "id");

            var nameColumn =
                GetRequiredColumn(
                    headers,
                    "name");

            var yearPublishedColumn =
                GetRequiredColumn(
                    headers,
                    "yearpublished");

            var rankColumn =
                GetRequiredColumn(
                    headers,
                    "rank");

            var averageColumn =
                GetRequiredColumn(
                    headers,
                    "average");

            var ratingsCountColumn =
                GetRequiredColumn(
                    headers,
                    "usersrated");

            var isExpansionColumn =
                GetRequiredColumn(
                    headers,
                    "is_expansion");

            _logger.LogInformation(
                "CSV BGG válido. A iniciar processamento em batches.");

            var batch =
                new List<BggCatalogCsvRow>(
                    ImportBatchSize);

            var processed = 0;
            var skipped = 0;

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line =
                    await reader.ReadLineAsync(
                        cancellationToken);

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var row =
                    ParseRow(
                        line,
                        idColumn,
                        nameColumn,
                        yearPublishedColumn,
                        rankColumn,
                        averageColumn,
                        ratingsCountColumn,
                        isExpansionColumn);

                if (row == null)
                {
                    skipped++;
                    continue;
                }

                batch.Add(row);

                if (batch.Count < ImportBatchSize)
                {
                    continue;
                }

                processed +=
                    await ImportBatchAsync(
                        batch,
                        cancellationToken);

                batch.Clear();

                _logger.LogInformation(
                    "{Processed} jogos BGG processados.",
                    processed);
            }

            if (batch.Count > 0)
            {
                processed +=
                    await ImportBatchAsync(
                        batch,
                        cancellationToken);
            }

            _logger.LogInformation(
                "Importação do catálogo BGG concluída. " +
                "{Processed} jogos processados; {Skipped} linhas ignoradas.",
                processed,
                skipped);

            return processed;
        }

        private async Task<int> ImportBatchAsync(
            IReadOnlyCollection<BggCatalogCsvRow> rows,
            CancellationToken cancellationToken)
        {
            if (rows.Count == 0)
            {
                return 0;
            }

            /*
             * Proteção contra IDs duplicados no próprio dump.
             *
             * A importação massiva já não consulta os registos existentes
             * através do Entity Framework. O repositório trata o INSERT/UPDATE
             * em bloco através de BulkUpsertAsync.
             */
            var uniqueRows =
                rows
                    .Where(x => x.BggId > 0)
                    .GroupBy(x => x.BggId)
                    .Select(x => x.First())
                    .ToList();

            if (uniqueRows.Count == 0)
            {
                return 0;
            }

            var catalogGames =
                new List<GameSearchCatalog>(
                    uniqueRows.Count);

            foreach (var row in uniqueRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedName =
                    NormalizeSearchText(
                        row.Name);

                if (string.IsNullOrWhiteSpace(normalizedName))
                {
                    continue;
                }

                /*
                 * Estes objetos funcionam apenas como dados de entrada
                 * para o bulk upsert.
                 *
                 * Não são adicionados ao DbContext e não ficam tracked.
                 * O repositório utiliza apenas os campos básicos do dump
                 * para atualizar/inserir GameSearchCatalog diretamente
                 * no SQL Server.
                 */
                catalogGames.Add(
                    new GameSearchCatalog(
                        bggId: row.BggId,
                        name: row.Name,
                        normalizedName: normalizedName,
                        yearPublished: row.YearPublished,
                        thumbnailUrl: null,
                        isExpansion: row.IsExpansion,
                        minPlayers: null,
                        maxPlayers: null,
                        isCooperative: false,
                        supportsCampaign: false,
                        averageRating: row.AverageRating,
                        ratingsCount: row.RatingsCount,
                        bggRank: row.BggRank));
            }

            if (catalogGames.Count == 0)
            {
                return 0;
            }

            return await _catalogRepository.BulkUpsertAsync(
                catalogGames,
                cancellationToken);
        }

        private static BggCatalogCsvRow? ParseRow(
            string line,
            int idColumn,
            int nameColumn,
            int yearPublishedColumn,
            int rankColumn,
            int averageColumn,
            int ratingsCountColumn,
            int isExpansionColumn)
        {
            var columns =
                ParseCsvLine(line);

            if (!TryGetColumn(
                    columns,
                    idColumn,
                    out var idValue) ||
                !int.TryParse(
                    idValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var bggId) ||
                bggId <= 0)
            {
                return null;
            }

            if (!TryGetColumn(
                    columns,
                    nameColumn,
                    out var name) ||
                string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            int? yearPublished = null;

            if (TryGetColumn(
                    columns,
                    yearPublishedColumn,
                    out var yearPublishedValue) &&
                int.TryParse(
                    yearPublishedValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedYearPublished) &&
                parsedYearPublished > 0)
            {
                yearPublished = parsedYearPublished;
            }

            int? rank = null;

            if (TryGetColumn(
                    columns,
                    rankColumn,
                    out var rankValue) &&
                int.TryParse(
                    rankValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedRank) &&
                parsedRank > 0)
            {
                rank = parsedRank;
            }

            double? averageRating = null;

            if (TryGetColumn(
                    columns,
                    averageColumn,
                    out var averageValue) &&
                double.TryParse(
                    averageValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsedAverage) &&
                double.IsFinite(parsedAverage))
            {
                averageRating = parsedAverage;
            }

            int? ratingsCount = null;

            if (TryGetColumn(
                    columns,
                    ratingsCountColumn,
                    out var ratingsCountValue) &&
                int.TryParse(
                    ratingsCountValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedRatingsCount) &&
                parsedRatingsCount >= 0)
            {
                ratingsCount = parsedRatingsCount;
            }

            var isExpansion = false;

            if (TryGetColumn(
                    columns,
                    isExpansionColumn,
                    out var isExpansionValue))
            {
                isExpansion =
                    isExpansionValue.Equals(
                        "1",
                        StringComparison.OrdinalIgnoreCase) ||
                    isExpansionValue.Equals(
                        "true",
                        StringComparison.OrdinalIgnoreCase);
            }

            return new BggCatalogCsvRow
            {
                BggId = bggId,
                Name = name.Trim(),
                YearPublished = yearPublished,
                BggRank = rank,
                AverageRating = averageRating,
                RatingsCount = ratingsCount,
                IsExpansion = isExpansion
            };
        }

        private static Dictionary<string, int> BuildHeaderMap(
            string headerLine)
        {
            return ParseCsvLine(headerLine)
                .Select((value, index) => new
                {
                    Name = NormalizeHeader(value),
                    Index = index
                })
                .GroupBy(
                    x => x.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.First().Index,
                    StringComparer.OrdinalIgnoreCase);
        }

        private static int GetRequiredColumn(
            IReadOnlyDictionary<string, int> headers,
            string name)
        {
            var normalized =
                NormalizeHeader(
                    name);

            if (headers.TryGetValue(
                    normalized,
                    out var index))
            {
                return index;
            }

            var aliases =
                name switch
                {
                    "id" => new[]
                    {
                        "bggid",
                        "objectid"
                    },

                    "name" => new[]
                    {
                        "title"
                    },

                    "yearpublished" => new[]
                    {
                        "year",
                        "publicationyear"
                    },

                    "rank" => new[]
                    {
                        "bggrank",
                        "boardgamerank"
                    },

                    "average" => new[]
                    {
                        "averagerating"
                    },

                    "usersrated" => new[]
                    {
                        "ratingscount",
                        "numratings"
                    },

                    "is_expansion" => new[]
                    {
                        "isexpansion",
                        "expansion"
                    },

                    _ => Array.Empty<string>()
                };

            foreach (var alias in aliases)
            {
                if (headers.TryGetValue(
                        NormalizeHeader(alias),
                        out index))
                {
                    return index;
                }
            }

            throw new InvalidOperationException(
                $"O CSV BGG não contém a coluna obrigatória '{name}'.");
        }

        private static bool TryGetColumn(
            IReadOnlyList<string> columns,
            int index,
            out string value)
        {
            if (index < 0 ||
                index >= columns.Count)
            {
                value = string.Empty;
                return false;
            }

            value =
                columns[index].Trim();

            return true;
        }

        private static List<string> ParseCsvLine(
            string line)
        {
            var values =
                new List<string>();

            var current =
                new StringBuilder();

            var insideQuotes =
                false;

            for (var i = 0; i < line.Length; i++)
            {
                var character =
                    line[i];

                if (character == '"')
                {
                    if (insideQuotes &&
                        i + 1 < line.Length &&
                        line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        insideQuotes =
                            !insideQuotes;
                    }

                    continue;
                }

                if (character == ',' &&
                    !insideQuotes)
                {
                    values.Add(
                        current.ToString());

                    current.Clear();

                    continue;
                }

                current.Append(
                    character);
            }

            values.Add(
                current.ToString());

            return values;
        }

        private static Uri? FindDumpUri(
            string html,
            Uri pageUri)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var candidates =
                new List<string>();

            /*
             * 1) Links normais em href="...".
             */
            var hrefMatches =
                Regex.Matches(
                    html,
                    @"href\s*=\s*[""'](?<url>[^""']+)[""']",
                    RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant);

            candidates.AddRange(
                hrefMatches
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(x =>
                        WebUtility.HtmlDecode(
                            x.Groups["url"].Value)));

            /*
             * 2) URLs absolutas que possam estar embebidas
             * em JSON/JavaScript em vez de href.
             */
            var absoluteUrlMatches =
                Regex.Matches(
                    html,
                    @"https:\/\/[^""'\s<>\\]+",
                    RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant);

            candidates.AddRange(
                absoluteUrlMatches
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(x =>
                        WebUtility.HtmlDecode(
                            x.Value)));

            foreach (var candidate in candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsDumpLink(candidate))
                {
                    continue;
                }

                if (!Uri.TryCreate(
                        pageUri,
                        candidate,
                        out var uri))
                {
                    continue;
                }

                /*
                 * O link vem da página oficial do BGG, mas o
                 * ficheiro pode ser servido por infraestrutura
                 * externa/CDN. Exigimos pelo menos HTTPS.
                 */
                if (!IsBggUri(uri))
                {
                    continue;
                }

                return uri;
            }

            return null;
        }

        private static bool IsDumpLink(
            string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var decoded =
                WebUtility.HtmlDecode(url);

            /*
             * Nome histórico mais comum do dump.
             */
            if (decoded.Contains(
                    "boardgames_ranks",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            /*
             * Alternativa genérica para ficheiros CSV/ZIP
             * associados a rank/ranks.
             */
            var looksLikeFile =
                decoded.Contains(
                    ".csv",
                    StringComparison.OrdinalIgnoreCase) ||
                decoded.Contains(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase);

            var looksLikeRankDump =
                decoded.Contains(
                    "rank",
                    StringComparison.OrdinalIgnoreCase) ||
                decoded.Contains(
                    "ranks",
                    StringComparison.OrdinalIgnoreCase);

            return looksLikeFile &&
                   looksLikeRankDump;
        }

        private static bool IsBggUri(
            Uri uri)
        {
            /*
             * O dump pode ser entregue através de CDN externo.
             * Como o URL é descoberto na página oficial do BGG,
             * aqui exigimos apenas HTTPS.
             */
            return uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHtml(
            HttpResponseMessage response)
        {
            var mediaType =
                response.Content.Headers.ContentType?.MediaType;

            return
                mediaType?.Equals(
                    "text/html",
                    StringComparison.OrdinalIgnoreCase)
                == true ||
                mediaType?.Equals(
                    "application/xhtml+xml",
                    StringComparison.OrdinalIgnoreCase)
                == true;
        }

        private static bool IsZip(
            HttpResponseMessage response)
        {
            var mediaType =
                response.Content.Headers.ContentType?.MediaType;

            if (mediaType?.Contains(
                    "zip",
                    StringComparison.OrdinalIgnoreCase)
                == true)
            {
                return true;
            }

            var fileName =
                response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName;

            if (fileName?.Trim('"')
                    .EndsWith(
                        ".zip",
                        StringComparison.OrdinalIgnoreCase)
                == true)
            {
                return true;
            }

            return response.RequestMessage?.RequestUri
                ?.AbsolutePath
                .EndsWith(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase)
                == true;
        }

        private static bool LooksLikeHtml(
            string value)
        {
            var text =
                value.TrimStart();

            return
                text.StartsWith(
                    "<!DOCTYPE html",
                    StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith(
                    "<html",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeHeader(
            string value)
        {
            return value
                .Trim()
                .Trim('"')
                .Replace(
                    " ",
                    string.Empty)
                .Replace(
                    "-",
                    string.Empty)
                .Replace(
                    "_",
                    string.Empty)
                .ToLowerInvariant();
        }

        private static string NormalizeSearchText(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var decomposed =
                value
                    .Trim()
                    .ToLowerInvariant()
                    .Normalize(
                        NormalizationForm.FormD);

            var builder =
                new StringBuilder(
                    decomposed.Length);

            var previousWasSpace =
                false;

            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) ==
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