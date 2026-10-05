using Hangfire;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Enriquece, em background, um batch de jogos do GameSearchCatalog
    /// que ainda não possui detalhes sincronizados com o BGG.
    ///
    /// Este job é utilizado para pré-enriquecimento do catálogo e trabalha
    /// exclusivamente sobre GameSearchCatalog.
    ///
    /// Nunca cria entidades Game reais.
    /// </summary>
    [Queue("bgg-catalog")]
    public class BggCatalogDetailsEnrichmentJob
    {
        private const int DefaultBatchSize = 100;
        private const int MaximumBatchSize = 500;

        private readonly IGameSearchCatalogRepository
            _catalogRepository;

        private readonly IGameSearchCatalogService
            _catalogService;

        private readonly ILogger<BggCatalogDetailsEnrichmentJob>
            _logger;

        public BggCatalogDetailsEnrichmentJob(
            IGameSearchCatalogRepository catalogRepository,
            IGameSearchCatalogService catalogService,
            ILogger<BggCatalogDetailsEnrichmentJob> logger)
        {
            _catalogRepository =
                catalogRepository ??
                throw new ArgumentNullException(
                    nameof(catalogRepository));

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
        [DisableConcurrentExecution(3600)]
        public async Task ExecuteAsync(
            int batchSize = DefaultBatchSize,
            CancellationToken cancellationToken = default)
        {
            var safeBatchSize =
                Math.Clamp(
                    batchSize,
                    1,
                    MaximumBatchSize);

            var stopwatch =
                Stopwatch.StartNew();

            _logger.LogInformation(
                "A iniciar enriquecimento do catálogo BGG. BatchSize={BatchSize}.",
                safeBatchSize);

            var candidates =
                await _catalogRepository
                    .GetCandidatesForEnrichmentAsync(
                        safeBatchSize,
                        cancellationToken);

            if (candidates.Count == 0)
            {
                stopwatch.Stop();

                _logger.LogInformation(
                    "Nenhum jogo pendente de enriquecimento no catálogo BGG. " +
                    "Tempo: {ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);

                return;
            }

            var bggIds =
                candidates
                    .Select(game => game.BggId)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToArray();

            if (bggIds.Length == 0)
            {
                stopwatch.Stop();

                _logger.LogWarning(
                    "Foram encontrados candidatos para enriquecimento, " +
                    "mas nenhum possuía um BGG ID válido.");

                return;
            }

            var enriched =
                await _catalogService
                    .EnrichDetailsAsync(
                        bggIds,
                        cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Enriquecimento do catálogo BGG concluído. " +
                "Candidatos={CandidateCount}; pedidos={RequestedCount}; " +
                "enriquecidos={EnrichedCount}; tempo={ElapsedMilliseconds} ms.",
                candidates.Count,
                bggIds.Length,
                enriched,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
