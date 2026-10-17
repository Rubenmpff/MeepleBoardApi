using Hangfire;
using MeepleBoard.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.Job
{
    /// <summary>
    /// Termina as sessões que chegaram à hora de fim (24 h após o início real,
    /// ou a hora escolhida pelo organizador, no máximo 48 h).
    ///
    /// - Grava EndDate = hora prevista de fim, não a hora em que o job correu.
    /// - Só considera sessões sem EndDate e não canceladas: repetir não muda nada.
    /// - Participantes, partidas, resultados e diários ficam intactos.
    /// - Não existe estado persistido de "partida em curso" (as partidas são
    ///   registadas já concluídas), por isso não há encerramento a adiar.
    ///
    /// Recorrente no Hangfire; o estado vive na base de dados, por isso continua
    /// certo depois de reinícios e recupera sessões antigas que ficaram ativas.
    /// </summary>
    public class SessionAutoCloseJob
    {
        private readonly IGameSessionRepository _sessionRepository;
        private readonly ILogger<SessionAutoCloseJob> _logger;

        public SessionAutoCloseJob(IGameSessionRepository sessionRepository, ILogger<SessionAutoCloseJob> logger)
        {
            _sessionRepository = sessionRepository;
            _logger = logger;
        }

        [AutomaticRetry(Attempts = 1)]
        [DisableConcurrentExecution(timeoutInSeconds: 60)]
        public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var open = await _sessionRepository.GetOpenForUpdateAsync(cancellationToken);
            var closed = 0;
            // Each close is a conditional update: a session the organizer ended meanwhile keeps that end.
            foreach (var session in open)
                if (session.AutoClose(now) && await _sessionRepository.SaveCloseAsync(session, cancellationToken)) closed++;
            if (closed == 0) return 0;

            _logger.LogInformation("SessionAutoCloseJob: {Count} sessão(ões) terminada(s) automaticamente.", closed);
            return closed;
        }
    }
}
