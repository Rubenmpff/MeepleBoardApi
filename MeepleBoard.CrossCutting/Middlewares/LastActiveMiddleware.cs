using MeepleBoard.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace MeepleBoard.CrossCutting.Middlewares
{
    /// <summary>
    /// Regista a última atividade do utilizador autenticado — usado só para o
    /// indicador "online" nos ecrãs de Amigos. Corre depois da autenticação,
    /// por isso já tem acesso aos claims do utilizador. Nunca deve derrubar o
    /// pedido real: qualquer falha aqui é registada e ignorada.
    /// </summary>
    public class LastActiveMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<LastActiveMiddleware> _logger;

        public LastActiveMiddleware(RequestDelegate next, IUserRepository userRepository, ILogger<LastActiveMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var userIdClaim = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (Guid.TryParse(userIdClaim, out var userId))
            {
                try
                {
                    await _userRepository.TouchLastActiveAsync(userId, context.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Não foi possível atualizar LastActiveAt para o utilizador {UserId}.", userId);
                }
            }

            await _next(context);
        }
    }
}