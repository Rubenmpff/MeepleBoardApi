using MeepleBoard.Application.DTOs;
using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MeepleBoardApi.Controllers
{
    [ApiController]
    [Route("MeepleBoard/session")]
    [Authorize]
    public class GameSessionController : ControllerBase
    {
        private readonly IGameSessionService _sessionService;
        private readonly ILogger<GameSessionController> _logger;

        public GameSessionController(
            IGameSessionService sessionService,
            ILogger<GameSessionController> logger)
        {
            _sessionService = sessionService;
            _logger = logger;
        }

        private Guid GetAuthUserId()
        {
            var userIdClaim =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst("nameid")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
                throw new UnauthorizedAccessException("Token JWT inválido ou utilizador não autenticado.");

            return Guid.Parse(userIdClaim);
        }

        /// <summary>
        /// ✅ Lista as minhas sessões:
        /// - onde sou Organizer
        /// - onde fui convidado (Pending/Accepted/Declined)
        /// </summary>
        [HttpGet("mine")]
        [ProducesResponseType(typeof(IEnumerable<GameSessionDto>), 200)]
        public async Task<IActionResult> GetMine()
        {
            try
            {
                var userId = GetAuthUserId();
                var sessions = await _sessionService.GetMineAsync(userId);
                return Ok(sessions);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Lista todas as sessões (admin/debug).
        /// ⚠️ Se não quiseres expor a todos, remove este endpoint ou filtra para "mine".
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<GameSessionDto>), 200)]
        public async Task<IActionResult> GetAll()
        {
            var sessions = await _sessionService.GetAllAsync();
            return Ok(sessions);
        }

        /// <summary>
        /// Obtém uma sessão de jogo pelo seu identificador.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(GameSessionDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(Guid id)
        {
            var session = await _sessionService.GetByIdAsync(id);
            if (session == null)
                return NotFound(new { message = "Sessão não encontrada." });

            return Ok(session);
        }

        /// <summary>
        /// Cria uma nova sessão de jogo (evento agendado).
        /// O utilizador autenticado é automaticamente o organizador.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateGameSessionDto dto)
        {
            try
            {
                var organizerId = GetAuthUserId();

                if (dto == null)
                    return BadRequest(new { message = "Dados inválidos." });

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { message = "O nome da sessão é obrigatório." });

                // dto.ScheduledStartDate pode vir null -> service assume "agora"
                var session = await _sessionService.CreateAsync(dto, organizerId);

                return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro interno ao criar sessão.");
                return StatusCode(500, new { message = "Erro interno ao criar sessão." });
            }
        }

        /// <summary>
        /// ✅ Convida um jogador (entra como Pending).
        /// Apenas o Organizer deve conseguir convidar.
        /// </summary>
        [HttpPost("{sessionId:guid}/players")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> InvitePlayer(Guid sessionId, [FromBody] AddPlayerDto dto)
        {
            try
            {
                var authUserId = GetAuthUserId();

                if (dto == null || dto.UserId == Guid.Empty)
                    return BadRequest(new { message = "UserId inválido." });

                // Segurança: não aceitar IsOrganizer vindo do FE
                dto.IsOrganizer = false;

                await _sessionService.InvitePlayerAsync(sessionId, authUserId, dto.UserId);

                return Ok(new { message = "Convite enviado com sucesso." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// ✅ Convidado aceita/recusa convite.
        /// </summary>
        [HttpPost("{sessionId:guid}/invites/respond")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> RespondInvite(Guid sessionId, [FromBody] RespondInviteDto dto)
        {
            try
            {
                var userId = GetAuthUserId();

                if (dto == null)
                    return BadRequest(new { message = "Dados inválidos." });

                await _sessionService.RespondInviteAsync(sessionId, userId, dto.Accept);

                return Ok(new { message = dto.Accept ? "Convite aceite." : "Convite recusado." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Fecha uma sessão de jogo (só Organizer).
        /// </summary>
        [HttpPost("{id:guid}/close")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Close(Guid id)
        {
            try
            {
                var authUserId = GetAuthUserId();

                await _sessionService.CloseSessionAsync(id, authUserId);

                return Ok(new { message = "Sessão encerrada com sucesso." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HttpPost("{id:guid}/cancel")]
        [Authorize]
        public async Task<IActionResult> Cancel(Guid id)
        {
            var organizerId = User.GetUserId();
            await _sessionService.CancelSessionAsync(id, organizerId);
            return NoContent();
        }

    }
}