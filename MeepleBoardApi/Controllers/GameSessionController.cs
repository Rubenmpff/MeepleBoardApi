using MeepleBoard.Application.DTOs;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    [ApiController]
    [Route("MeepleBoard/session")]
    [Authorize] // <-- se já tens autenticação JWT
    public class GameSessionController : ControllerBase
    {
        private readonly IGameSessionService _sessionService;

        public GameSessionController(IGameSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        /// <summary>Lista todas as sessões de jogo.</summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<GameSessionDto>>> GetAll()
        {
            var sessions = await _sessionService.GetAllAsync();
            return Ok(sessions);
        }

        /// <summary>Obtém uma sessão de jogo pelo seu ID.</summary>
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<GameSessionDto>> GetById(Guid id)
        {
            var session = await _sessionService.GetByIdAsync(id);
            if (session == null)
                return NotFound(new { message = "Sessão não encontrada." });

            return Ok(session);
        }

        /// <summary>Cria uma nova sessão de jogo.</summary>
        [HttpPost]
        public async Task<ActionResult<GameSessionDto>> Create([FromBody] CreateGameSessionDto dto)
        {
            var session = await _sessionService.CreateAsync(dto.Name, dto.OrganizerId, dto.Location);
            return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
        }

        /// <summary>Adiciona um jogador a uma sessão.</summary>
        [HttpPost("{sessionId:guid}/players")]
        public async Task<IActionResult> AddPlayer(Guid sessionId, [FromBody] AddPlayerDto dto)
        {
            await _sessionService.AddPlayerAsync(sessionId, dto.UserId, dto.IsOrganizer);
            return Ok(new { message = "Jogador adicionado com sucesso." });
        }

        /// <summary>Remove um jogador de uma sessão.</summary>
        [HttpDelete("{sessionId:guid}/players/{userId:guid}")]
        public async Task<IActionResult> RemovePlayer(Guid sessionId, Guid userId)
        {
            await _sessionService.RemovePlayerAsync(sessionId, userId);
            return Ok(new { message = "Jogador removido com sucesso." });
        }

        /// <summary>Fecha uma sessão de jogo.</summary>
        [HttpPost("{id:guid}/close")]
        public async Task<IActionResult> Close(Guid id)
        {
            await _sessionService.CloseSessionAsync(id);
            return Ok(new { message = "Sessão encerrada com sucesso." });
        }
    }
}
