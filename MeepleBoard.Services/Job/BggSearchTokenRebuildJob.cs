using Hangfire;
using MeepleBoard.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Reconstrói exclusivamente o índice auxiliar GameSearchToken
    /// a partir dos dados já existentes em GameSearchCatalog.
    ///
    /// Não importa novamente o dump BGG e não cria entidades Game.
    /// </summary>
    [Queue("bgg-rebuild")]
    public class BggSearchTokenRebuildJob
    {
        private readonly IGameSearchCatalogRepository
            _catalogRepository;

        private readonly ILogger<BggSearchTokenRebuildJob>
            _logger;

        public BggSearchTokenRebuildJob(
            IGameSearchCatalogRepository catalogRepository,
            ILogger<BggSearchTokenRebuildJob> logger)
        {
            _catalogRepository = catalogRepository;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
        public async Task ExecuteAsync(
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation(
                    "A iniciar reconstrução de GameSearchToken.");

                var tokenCount =
                    await _catalogRepository.RebuildSearchTokensAsync(
                        cancellationToken);

                stopwatch.Stop();

                _logger.LogInformation(
                    "Reconstrução de GameSearchToken concluída. " +
                    "{TokenCount} tokens criados em {ElapsedMilliseconds} ms.",
                    tokenCount,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();

                _logger.LogWarning(
                    "Reconstrução de GameSearchToken cancelada após " +
                    "{ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _logger.LogError(
                    ex,
                    "Erro durante reconstrução de GameSearchToken após " +
                    "{ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
        }
    }
}