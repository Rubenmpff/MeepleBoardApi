using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    [Route("MeepleBoard/matches")]
    [ApiController]
    public class MatchController : ControllerBase
    {
        private readonly IMatchService _matchService;

        public MatchController(IMatchService matchService)
        {
            _matchService = matchService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MatchDto>>> GetAll(
            int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            if (pageIndex < 0 || pageSize <= 0)
                return BadRequest("Os parâmetros de paginação devem ser positivos.");

            var matches = await _matchService.GetAllAsync(pageIndex, pageSize, cancellationToken);

            if (matches == null || !matches.Any())
                return NoContent();

            return Ok(matches);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<MatchDto>> GetById(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
                return BadRequest("O ID da partida não pode ser vazio.");

            var match = await _matchService.GetByIdAsync(id, cancellationToken);
            if (match == null)
                return NotFound("Partida não encontrada.");

            return Ok(match);
        }

        [HttpGet("last")]
        [Authorize]
        public async Task<IActionResult> GetLastMatchForUser()
        {
            var userId = User.GetUserId();

            var lastMatch = await _matchService.GetLastMatchForUserAsync(userId);

            if (lastMatch == null)
                return NotFound(new { message = "Nenhuma partida encontrada." });

            return Ok(lastMatch);
        }

        /// <summary>
        /// 🔹 Cria uma nova partida.
        /// Regras no service:
        /// - Quick match: inclui sempre o utilizador autenticado
        /// - Match dentro de sessão: players têm de pertencer à sessão
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<MatchDto>> Create([FromBody] CreateMatchDto dto, CancellationToken cancellationToken)
        {
            if (dto == null)
                return BadRequest("Os dados da partida são obrigatórios.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.GetUserId();

            try
            {
                var created = await _matchService.CreateAsync(dto, userId, cancellationToken);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
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

        [HttpPut("{id:guid}")]
        [Authorize]
        public async Task<ActionResult> Update(Guid id, [FromBody] MatchDto matchDto, CancellationToken cancellationToken)
        {
            if (matchDto == null)
                return BadRequest("Os dados da partida são obrigatórios.");

            if (id != matchDto.Id)
                return BadRequest("O ID da partida na URL não corresponde ao ID do corpo da requisição.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var existingMatch = await _matchService.GetByIdAsync(id, cancellationToken);
            if (existingMatch == null)
                return NotFound("Partida não encontrada.");

            var rowsAffected = await _matchService.UpdateAsync(matchDto, cancellationToken);
            if (rowsAffected == 0)
                return NotFound("Nenhuma partida foi atualizada.");

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        [Authorize]
        public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
                return BadRequest("O ID da partida não pode ser vazio.");

            var success = await _matchService.DeleteAsync(id, cancellationToken);
            if (!success)
                return NotFound("Partida não encontrada.");

            return NoContent();
        }
    }
}
