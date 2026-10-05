using Hangfire;
using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Enriquece em background jogos específicos que apareceram
    /// numa pesquisa sem capa/detalhes completos.
    ///
    /// Trabalha exclusivamente sobre GameSearchCatalog.
    /// Nunca cria entidades Game reais.
    /// </summary>
    [Queue("bgg-catalog")]
    public class BggCatalogSearchEnrichmentJob
    {
        private const int MaximumIdsPerJob = 50;

        private readonly IGameSearchCatalogService
            _catalogService;

        private readonly ILogger<BggCatalogSearchEnrichmentJob>
            _logger;

        public BggCatalogSearchEnrichmentJob(
            IGameSearchCatalogService catalogService,
            ILogger<BggCatalogSearchEnrichmentJob> logger)
        {
            _catalogService =
                catalogService ??
                throw new ArgumentNullException(
                    nameof(catalogService));

            _logger =
                logger ??
                throw new ArgumentNullException(
                    nameof(logger));
        }

        [AutomaticRetry(Attempts = 0)]
        public async Task ExecuteAsync(
            int[] bggIds,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(
                bggIds);

            var cleanIds =
                bggIds
                    .Where(id => id > 0)
                    .Distinct()
                    .Take(MaximumIdsPerJob)
                    .ToArray();

            if (cleanIds.Length == 0)
            {
                return;
            }

            var stopwatch =
                Stopwatch.StartNew();

            try
            {
                _logger.LogInformation(
                    "A iniciar enriquecimento lazy de {Count} jogos do catálogo BGG.",
                    cleanIds.Length);

                var enriched =
                    await _catalogService
                        .EnrichDetailsAsync(
                            cleanIds,
                            cancellationToken);

                stopwatch.Stop();

                _logger.LogInformation(
                    "Enriquecimento lazy do catálogo BGG concluído. " +
                    "{Requested} pedidos; {Enriched} enriquecidos em " +
                    "{ElapsedMilliseconds} ms.",
                    cleanIds.Length,
                    enriched,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();

                _logger.LogDebug(
                    "Enriquecimento lazy do catálogo BGG cancelado após " +
                    "{ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _logger.LogError(
                    ex,
                    "Erro durante enriquecimento lazy do catálogo BGG após " +
                    "{ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
        }
    }
}