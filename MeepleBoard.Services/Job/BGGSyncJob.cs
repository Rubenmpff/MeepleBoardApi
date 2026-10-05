
using Hangfire;
using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Atualiza periodicamente os jogos reais existentes na MeepleBoard
    /// com dados atuais do BoardGameGeek.
    ///
    /// Não serve para alimentar o catálogo de pesquisa.
    /// O GameSearchCatalog será mantido através do data dump do BGG.
    /// </summary>
    public class BGGSyncJob
    {
        private const int LocalPageSize = 100;
        private const int BggBatchSize = 20;

        private readonly IBGGService _bggService;
        private readonly IGameService _gameService;
        private readonly ILogger<BGGSyncJob> _logger;

        public BGGSyncJob(
            IBGGService bggService,
            IGameService gameService,
            ILogger<BGGSyncJob> logger)
        {
            _bggService = bggService;
            _gameService = gameService;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 2)]
        public async Task ExecuteAsync(
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            var totalLocalGames = 0;
            var totalRequestedFromBgg = 0;
            var totalReturnedFromBgg = 0;
            var totalUpdated = 0;
            var pageIndex = 0;

            try
            {
                _logger.LogInformation(
                    "A iniciar sincronização dos jogos locais com o BGG.");

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var pagedGames =
                        await _gameService.GetAllAsync(
                            pageIndex,
                            LocalPageSize,
                            cancellationToken);

                    var pageGames =
                        pagedGames.Data.ToList();

                    if (pageGames.Count == 0)
                    {
                        break;
                    }

                    totalLocalGames += pageGames.Count;

                    var bggIds =
                        pageGames
                            .Where(x => x.BggId.HasValue)
                            .Select(x => x.BggId!.Value)
                            .Distinct()
                            .ToList();

                    foreach (var batch in bggIds.Chunk(BggBatchSize))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var batchIds =
                            batch
                                .Select(x => x.ToString())
                                .ToList();

                        if (batchIds.Count == 0)
                        {
                            continue;
                        }

                        totalRequestedFromBgg += batchIds.Count;

                        var updatedGames =
                            await _bggService.GetGamesByIdsAsync(
                                batchIds,
                                cancellationToken);

                        totalReturnedFromBgg += updatedGames.Count;

                        foreach (var updatedGame in updatedGames)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            try
                            {
                                var success =
                                    await _gameService.UpdateFromBggAsync(
                                        updatedGame,
                                        cancellationToken);

                                if (success)
                                {
                                    totalUpdated++;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "Falha ao atualizar jogo local '{Name}' (BGG ID: {BggId}).",
                                    updatedGame.Name,
                                    updatedGame.BggId);
                            }
                        }
                    }

                    if (pageGames.Count < LocalPageSize)
                    {
                        break;
                    }

                    pageIndex++;
                }

                stopwatch.Stop();

                if (totalRequestedFromBgg == 0)
                {
                    _logger.LogInformation(
                        "Não existem jogos locais com BGG ID para sincronizar. " +
                        "{TotalLocalGames} jogos locais analisados em {ElapsedMilliseconds} ms.",
                        totalLocalGames,
                        stopwatch.ElapsedMilliseconds);

                    return;
                }

                _logger.LogInformation(
                    "Sincronização BGG concluída. " +
                    "{Updated}/{Returned} jogos atualizados. " +
                    "{Requested} BGG IDs enviados ao BGG. " +
                    "{TotalLocalGames} jogos locais analisados em {ElapsedMilliseconds} ms.",
                    totalUpdated,
                    totalReturnedFromBgg,
                    totalRequestedFromBgg,
                    totalLocalGames,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();

                _logger.LogWarning(
                    "Sincronização BGG cancelada após {ElapsedMilliseconds} ms. " +
                    "{Updated} jogos já tinham sido atualizados.",
                    stopwatch.ElapsedMilliseconds,
                    totalUpdated);

                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _logger.LogError(
                    ex,
                    "Erro durante sincronização BGG após {ElapsedMilliseconds} ms. " +
                    "{Updated} jogos já tinham sido atualizados.",
                    stopwatch.ElapsedMilliseconds,
                    totalUpdated);

                throw;
            }
        }
    }
}
