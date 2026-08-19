using Hangfire;
using MeepleBoard.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Job que fecha automaticamente o diário de partidas expiradas.
    ///
    /// Regras:
    ///   - Partida com JournalStatus = Open + passou o AutoCloseAt → fecha o diário
    ///   - Jogadores que ainda não avaliaram ficam com entrada Pending
    ///   - Podem avaliar depois — as notas ficam visíveis quando submetem
    ///
    /// Executado pelo Hangfire de hora a hora (mesmo padrão do SessionCleanupJob).
    /// </summary>
    public class MatchCleanupJob
    {
        private readonly IMatchRepository _matchRepository;
        private readonly ILogger<MatchCleanupJob> _logger;

        public MatchCleanupJob(
            IMatchRepository matchRepository,
            ILogger<MatchCleanupJob> logger)
        {
            _matchRepository = matchRepository;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 1)]
        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("🧹 MatchCleanupJob: a verificar diários de partidas expirados...");

            // Busca todas as partidas com diário aberto
            var matches = await _matchRepository.GetOpenJournalMatchesAsync(cancellationToken);

            var toClose = matches
                .Where(m => m.ShouldAutoCloseNow)
                .ToList();

            if (toClose.Count == 0)
            {
                _logger.LogInformation("🧹 MatchCleanupJob: nenhum diário expirado encontrado.");
                return;
            }

            _logger.LogInformation(
                "🧹 MatchCleanupJob: {Count} diário(s) expirado(s) a fechar.", toClose.Count);

            foreach (var match in toClose)
            {
                try
                {
                    _logger.LogInformation(
                        "🔒 A fechar diário da partida: {GameId} (Id: {Id}) | AutoCloseAt: {AutoCloseAt}",
                        match.GameId, match.Id, match.AutoCloseAt);

                    match.CloseJournal();
                    await _matchRepository.UpdateAsync(match, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Erro ao fechar diário da partida {Id}.", match.Id);
                }
            }

            await _matchRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "✅ MatchCleanupJob: {Count} diário(s) fechado(s) com sucesso.", toClose.Count);
        }
    }
}