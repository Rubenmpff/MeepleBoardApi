using Hangfire;
using MeepleBoard.Services.DTOs;
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

            try
            {
                _logger.LogInformation(
                    "A iniciar sincronização dos jogos locais com o BGG.");

                /*
                 * Apenas entidades Game reais existentes na MeepleBoard.
                 *
                 * Não precisamos de:
                 * - Hot Games;
                 * - jogos recentes;
                 * - jogos mais pesquisados.
                 *
                 * Esses subconjuntos já estão incluídos nos jogos existentes.
                 */
                var pagedGames =
                    await _gameService.GetAllAsync(
                        0,
                        int.MaxValue,
                        cancellationToken);

                var gamesWithBggId =
                    pagedGames.Data
                        .Where(x => x.BggId.HasValue)
                        .ToList();

                if (gamesWithBggId.Count == 0)
                {
                    _logger.LogInformation(
                        "Não existem jogos locais com BGG ID para sincronizar.");

                    return;
                }

                var ids =
                    gamesWithBggId
                        .Select(x => x.BggId!.Value)
                        .Distinct()
                        .Select(x => x.ToString())
                        .ToList();

                _logger.LogInformation(
                    "{Count} jogos locais serão sincronizados com o BGG.",
                    ids.Count);

                /*
                 * Uma chamada em batch ao BGG.
                 */
                var updatedGames =
                    await _bggService.GetGamesByIdsAsync(
                        ids,
                        cancellationToken);

                var totalUpdated = 0;

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

                stopwatch.Stop();

                _logger.LogInformation(
                    "Sincronização BGG concluída. " +
                    "{Updated}/{Total} jogos atualizados em {ElapsedMilliseconds} ms.",
                    totalUpdated,
                    updatedGames.Count,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();

                _logger.LogWarning(
                    "Sincronização BGG cancelada após {ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _logger.LogError(
                    ex,
                    "Erro durante sincronização BGG após {ElapsedMilliseconds} ms.",
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
        }
    }
}