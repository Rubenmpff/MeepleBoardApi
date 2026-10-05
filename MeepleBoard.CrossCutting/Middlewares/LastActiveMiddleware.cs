using MeepleBoard.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace MeepleBoard.CrossCutting.Middlewares
{
    /// <summary>
    /// Regista a última atividade do utilizador autenticado — usado apenas
    /// para o indicador "online" nos ecrãs de Amigos.
    ///
    /// Corre depois da autenticação, por isso já tem acesso aos claims
    /// do utilizador.
    ///
    /// A atualização de LastActiveAt nunca deve derrubar o pedido real.
    /// Cancelamentos normais do request são ignorados silenciosamente
    /// (com LogDebug), enquanto falhas reais continuam registadas como warning.
    ///
    /// Para evitar uma ida à base de dados em todos os pedidos autenticados,
    /// existe um throttle em memória por utilizador.
    /// </summary>
    public class LastActiveMiddleware
    {
        private static readonly TimeSpan TouchInterval =
            TimeSpan.FromSeconds(45);

        /*
         * Guarda, por utilizador, até quando podemos ignorar novos pedidos
         * de atualização de LastActiveAt.
         *
         * É static porque este middleware é instanciado manualmente no
         * Program.cs para cada request. Assim, o throttle é partilhado entre
         * todos os pedidos desta instância da API.
         *
         * Em ambientes com várias instâncias da API, cada instância terá o
         * seu próprio throttle. Isso é aceitável porque LastActiveAt é apenas
         * informação auxiliar de presença.
         */
        private static readonly ConcurrentDictionary<Guid, DateTime>
            NextAllowedTouchByUser = new();

        private readonly RequestDelegate _next;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<LastActiveMiddleware> _logger;

        public LastActiveMiddleware(
            RequestDelegate next,
            IUserRepository userRepository,
            ILogger<LastActiveMiddleware> logger)
        {
            _next =
                next ??
                throw new ArgumentNullException(
                    nameof(next));

            _userRepository =
                userRepository ??
                throw new ArgumentNullException(
                    nameof(userRepository));

            _logger =
                logger ??
                throw new ArgumentNullException(
                    nameof(logger));
        }

        public async Task InvokeAsync(
            HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(
                context);

            var userIdClaim =
                context.User?
                    .FindFirst(
                        ClaimTypes.NameIdentifier)?
                    .Value;

            if (Guid.TryParse(
                userIdClaim,
                out var userId) &&
                TryAcquireTouchSlot(
                    userId))
            {
                try
                {
                    await _userRepository
                        .TouchLastActiveAsync(
                            userId,
                            context.RequestAborted);
                }
                catch (OperationCanceledException)
                    when (context.RequestAborted.IsCancellationRequested)
                {
                    /*
                     * O pedido foi cancelado antes de conseguirmos confirmar
                     * a atualização. Libertamos o throttle para permitir que
                     * o próximo request tente novamente.
                     */
                    ReleaseTouchSlot(
                        userId);

                    _logger.LogDebug(
                        "Atualização de LastActiveAt cancelada porque o request foi abortado para o utilizador {UserId}.",
                        userId);
                }
                catch (Exception ex)
                {
                    /*
                     * LastActiveAt é informação auxiliar de presença.
                     * Uma falha aqui nunca deve impedir o pedido principal.
                     *
                     * Como a atualização falhou, libertamos também o throttle
                     * para não bloquear uma nova tentativa durante 45 segundos.
                     */
                    ReleaseTouchSlot(
                        userId);

                    _logger.LogWarning(
                        ex,
                        "Não foi possível atualizar LastActiveAt para o utilizador {UserId}.",
                        userId);
                }
            }

            await _next(
                context);
        }

        /// <summary>
        /// Reserva o direito de executar TouchLastActiveAsync para este
        /// utilizador durante a janela atual.
        ///
        /// Devolve false quando outro pedido já atualizou/reservou a presença
        /// há menos de 45 segundos.
        ///
        /// O TryAdd/TryUpdate evita que vários pedidos simultâneos do mesmo
        /// utilizador atravessem o throttle ao mesmo tempo.
        /// </summary>
        private static bool TryAcquireTouchSlot(
            Guid userId)
        {
            var now =
                DateTime.UtcNow;

            var nextAllowedTouch =
                now.Add(
                    TouchInterval);

            while (true)
            {
                if (!NextAllowedTouchByUser.TryGetValue(
                        userId,
                        out var currentNextAllowedTouch))
                {
                    if (NextAllowedTouchByUser.TryAdd(
                            userId,
                            nextAllowedTouch))
                    {
                        return true;
                    }

                    continue;
                }

                if (currentNextAllowedTouch > now)
                {
                    return false;
                }

                if (NextAllowedTouchByUser.TryUpdate(
                        userId,
                        nextAllowedTouch,
                        currentNextAllowedTouch))
                {
                    return true;
                }
            }
        }

        private static void ReleaseTouchSlot(
            Guid userId)
        {
            NextAllowedTouchByUser.TryRemove(
                userId,
                out _);
        }
    }
}
