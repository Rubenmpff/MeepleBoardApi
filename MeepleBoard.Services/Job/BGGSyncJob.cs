using Hangfire;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Job para sincronizar periodicamente jogos com o BGG (BoardGameGeek).
    /// Atualiza jogos cadastrados, mais buscados, populares e recentemente jogados.
    /// Executado por Hangfire.
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
        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("🚀 Iniciando job de sincronização com o BGG...");

            var jogosParaAtualizar = new HashSet<int>();

            // 🔹 1. Jogos cadastrados no sistema
            try
            {
                var paged = await _gameService.GetAllAsync(0, int.MaxValue, cancellationToken);
                var jogosDaApp = paged.Data;
                foreach (var jogo in jogosDaApp.Where(j => j.BggId.HasValue))
                    jogosParaAtualizar.Add(jogo.BggId!.Value);

                _logger.LogInformation("📌 {Count} jogos cadastrados encontrados.", jogosDaApp.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Falha ao buscar jogos cadastrados.");
            }

            // 🔹 2. Jogos da hot list do BGG
            try
            {
                var hotGames = await _bggService.GetHotGamesAsync(cancellationToken);
                foreach (var jogo in hotGames.Where(j => j.BggId.HasValue))
                    jogosParaAtualizar.Add(jogo.BggId!.Value);

                _logger.LogInformation("🔥 {Count} jogos da hot list encontrados.", hotGames.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Falha ao buscar hot list do BGG.");
            }

            // 🔹 3. Jogos recentemente jogados
            try
            {
                var recentes = await _gameService.GetRecentlyPlayedAsync(50, cancellationToken);
                foreach (var jogo in recentes.Where(j => j.BggId.HasValue))
                    jogosParaAtualizar.Add(jogo.BggId!.Value);

                _logger.LogInformation("🎲 {Count} jogos recentemente jogados encontrados.", recentes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Falha ao buscar jogos recentemente jogados.");
            }

            // 🔹 4. Jogos mais buscados
            try
            {
                var populares = await _gameService.GetMostSearchedAsync(50, cancellationToken);
                foreach (var jogo in populares.Where(j => j.BggId.HasValue))
                    jogosParaAtualizar.Add(jogo.BggId!.Value);

                _logger.LogInformation("🔍 {Count} jogos mais buscados encontrados.", populares.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Falha ao buscar jogos mais buscados.");
            }

            _logger.LogInformation("📦 Total de jogos únicos a sincronizar: {Total}", jogosParaAtualizar.Count);

            if (!jogosParaAtualizar.Any())
            {
                _logger.LogWarning("🚫 Nenhum jogo com BGG ID foi encontrado para sincronização.");
                return;
            }

            // 🔄 Busca detalhes atualizados do BGG
            List<GameDto> jogosAtualizados;
            try
            {
                var ids = jogosParaAtualizar.Select(id => id.ToString()).ToList();
                jogosAtualizados = await _bggService.GetGamesByIdsAsync(ids, cancellationToken);
                _logger.LogInformation("📥 {Count} jogos recebidos com detalhes do BGG.", jogosAtualizados.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao buscar detalhes dos jogos no BGG.");
                return;
            }

            int totalAtualizados = 0;
            foreach (var jogo in jogosAtualizados)
            {
                try
                {
                    var sucesso = await _gameService.UpdateFromBggAsync(jogo, cancellationToken);
                    if (sucesso) totalAtualizados++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Falha ao atualizar jogo local: {Name}", jogo.Name);
                }
            }

            stopwatch.Stop();
            _logger.LogInformation("✅ {Count} jogos atualizados com sucesso. ⏱️ Tempo total: {Time} ms", totalAtualizados, stopwatch.ElapsedMilliseconds);
        }
    }
}