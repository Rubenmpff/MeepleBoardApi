using MeepleBoard.Application.DTOs;
using MeepleBoard.Services.Interfaces;
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

        /// <summary>
        /// Lista todas as sessões de jogo existentes.
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
        /// Cria uma nova sessão de jogo.
        /// O utilizador autenticado é automaticamente o organizador.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(GameSessionDto), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Create([FromBody] CreateGameSessionDto dto)
        {
            try
            {
                // 🔐 Extrai o utilizador autenticado do JWT
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("nameid")?.Value;

                if (string.IsNullOrEmpty(userIdClaim))
                    return Unauthorized(new { message = "Token JWT inválido ou utilizador não autenticado." });

                var organizerId = Guid.Parse(userIdClaim);

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { message = "O nome da sessão é obrigatório." });

                var session = await _sessionService.CreateAsync(dto.Name, organizerId, dto.Location);
                _logger.LogInformation("Sessão criada com sucesso por utilizador {UserId}", organizerId);

                return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Erro de validação ao criar sessão.");
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro inesperado ao criar sessão.");
                return StatusCode(500, new { message = "Erro interno ao criar sessão." });
            }
        }

        /// <summary>
        /// Adiciona um jogador a uma sessão existente.
        /// </summary>
        [HttpPost("{sessionId:guid}/players")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> AddPlayer(Guid sessionId, [FromBody] AddPlayerDto dto)
        {
            try
            {
                await _sessionService.AddPlayerAsync(sessionId, dto.UserId, dto.IsOrganizer);
                return Ok(new { message = "Jogador adicionado com sucesso." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Remove um jogador de uma sessão.
        /// </summary>
        [HttpDelete("{sessionId:guid}/players/{userId:guid}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RemovePlayer(Guid sessionId, Guid userId)
        {
            try
            {
                await _sessionService.RemovePlayerAsync(sessionId, userId);
                return Ok(new { message = "Jogador removido com sucesso." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Fecha uma sessão de jogo ativa.
        /// </summary>
        [HttpPost("{id:guid}/close")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Close(Guid id)
        {
            try
            {
                await _sessionService.CloseSessionAsync(id);
                return Ok(new { message = "Sessão encerrada com sucesso." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
