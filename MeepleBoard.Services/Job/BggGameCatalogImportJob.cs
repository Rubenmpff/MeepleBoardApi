using Hangfire;
using MeepleBoard.Services.ExternalServices.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Job responsável por importar e atualizar
    /// o catálogo completo de pesquisa do BoardGameGeek.
    ///
    /// Trabalha exclusivamente sobre GameSearchCatalog
    /// e nunca cria entidades Game reais.
    /// </summary>
    [Queue("bgg-catalog")]
    public class BggGameCatalogImportJob
    {
        private readonly IBggGameCatalogImportService
            _catalogImportService;

        private readonly IConfiguration
            _configuration;

        private readonly ILogger<BggGameCatalogImportJob>
            _logger;

        public BggGameCatalogImportJob(
            IBggGameCatalogImportService catalogImportService,
            IConfiguration configuration,
            ILogger<BggGameCatalogImportJob> logger)
        {
            _catalogImportService = catalogImportService;
            _configuration = configuration;
            _logger = logger;
        }

        /*
         * A importação é idempotente, mas deixamos o retry automático
         * desativado para que uma falha pesada ou de infraestrutura não
         * provoque imediatamente uma nova importação sem diagnóstico.
         *
         * Uma política de retry controlada poderá ser adicionada mais
         * tarde quando o catálogo estiver alojado em infraestrutura
         * definitiva.
         */
        [AutomaticRetry(Attempts = 0)]

        /*
         * Segunda linha de proteção contra concorrência.
         *
         * Mesmo existindo apenas um worker na queue bgg-catalog,
         * este lock distribuído protege também o cenário em que existam
         * várias instâncias da API/Hangfire ligadas ao mesmo storage.
         */
        [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
        public async Task ExecuteAsync(
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var localFilePath =
                    _configuration["Bgg:CatalogLocalFilePath"];

                int processed;

                if (!string.IsNullOrWhiteSpace(localFilePath))
                {
                    _logger.LogInformation(
                        "A iniciar importação local do catálogo BGG " +
                        "a partir de {FileName}.",
                        Path.GetFileName(localFilePath));

                    processed =
                        await _catalogImportService.ImportFromFileAsync(
                            localFilePath,
                            cancellationToken);
                }
                else
                {
                    _logger.LogInformation(
                        "A iniciar importação do catálogo completo BGG " +
                        "a partir da fonte configurada.");

                    processed =
                        await _catalogImportService.ImportAsync(
                            cancellationToken);
                }

                stopwatch.Stop();

                _logger.LogInformation(
                    "Importação do catálogo BGG concluída. " +
                    "{Processed} jogos processados em {ElapsedMilliseconds} ms.",
                    processed,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();

                _logger.LogWarning(
                    "Importação do catálogo BGG cancelada após " +
                    "{ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _logger.LogError(
                    ex,
                    "Erro durante importação do catálogo BGG após " +
                    "{ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
        }
    }
}