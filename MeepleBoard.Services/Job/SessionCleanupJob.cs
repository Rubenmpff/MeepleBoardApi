using Hangfire;
using MeepleBoard.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Job que cancela e apaga sessões expiradas.
    ///
    /// Regras:
    ///   - Sessão Upcoming + passou o EffectiveDeadline + menos de 1 aceitação → apaga
    ///
    /// Executado pelo Hangfire (mesmo padrão do UserCleanupJob e BGGSyncJob).
    /// </summary>
    public class SessionCleanupJob
    {
        private readonly IGameSessionRepository _sessionRepository;
        private readonly ILogger<SessionCleanupJob> _logger;

        public SessionCleanupJob(
            IGameSessionRepository sessionRepository,
            ILogger<SessionCleanupJob> logger)
        {
            _sessionRepository = sessionRepository;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 1)]
        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("🧹 SessionCleanupJob: a verificar sessões expiradas...");

            // Busca todas as sessões com detalhes (Players incluídos para AcceptedGuestCount)
            var sessions = await _sessionRepository.GetListAsync(cancellationToken);

            var toDelete = sessions
                .Where(s => s.ShouldAutoCancelNow())
                .ToList();

            if (toDelete.Count == 0)
            {
                _logger.LogInformation("🧹 SessionCleanupJob: nenhuma sessão expirada encontrada.");
                return;
            }

            _logger.LogInformation(
                "🧹 SessionCleanupJob: {Count} sessão(ões) expirada(s) a apagar.", toDelete.Count);

            foreach (var session in toDelete)
            {
                try
                {
                    _logger.LogInformation(
                        "🗑 A apagar sessão expirada: {Name} (Id: {Id}) | Deadline: {Deadline}",
                        session.Name, session.Id, session.EffectiveDeadline);

                    await _sessionRepository.DeleteAsync(session.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Erro ao apagar sessão {Id}.", session.Id);
                }
            }

            await _sessionRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "✅ SessionCleanupJob: {Count} sessão(ões) apagada(s) com sucesso.", toDelete.Count);
        }
    }
}