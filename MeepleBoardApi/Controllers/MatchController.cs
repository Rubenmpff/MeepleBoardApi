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
            if (matches == null || !matches.Any()) return NoContent();
            return Ok(matches);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<MatchDto>> GetById(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest("O ID da partida não pode ser vazio.");
            var match = await _matchService.GetByIdAsync(id, cancellationToken);
            if (match == null) return NotFound("Partida não encontrada.");
            return Ok(match);
        }

        [HttpGet("last")]
        [Authorize]
        public async Task<IActionResult> GetLastMatchForUser()
        {
            var userId = User.GetUserId();
            var lastMatch = await _matchService.GetLastMatchForUserAsync(userId);
            if (lastMatch == null) return NotFound(new { message = "Nenhuma partida encontrada." });
            return Ok(lastMatch);
        }

        /// <summary>
        /// Historial de partidas do utilizador autenticado para um jogo específico.
        /// </summary>
        [HttpGet("history/game/{gameId:guid}")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<MatchDto>>> GetHistoryByGame(
            Guid gameId, CancellationToken cancellationToken)
        {
            if (gameId == Guid.Empty)
                return BadRequest("O ID do jogo não pode ser vazio.");

            var userId = User.GetUserId();
            try
            {
                var history = await _matchService.GetMatchHistoryByGameAsync(gameId, userId, cancellationToken);
                if (!history.Any()) return NoContent();
                return Ok(history);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>
        /// Avaliação pessoal média do utilizador para um jogo (0–10).
        /// Só conta partidas oficiais.
        /// </summary>
        [HttpGet("rating/game/{gameId:guid}")]
        [Authorize]
        public async Task<ActionResult<double?>> GetUserRatingForGame(
            Guid gameId, CancellationToken cancellationToken)
        {
            if (gameId == Guid.Empty)
                return BadRequest("O ID do jogo não pode ser vazio.");

            var userId = User.GetUserId();
            try
            {
                var rating = await _matchService.GetUserRatingForGameAsync(gameId, userId, cancellationToken);
                return Ok(new { rating, hasRating = rating.HasValue });
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>
        /// Partidas com diário aberto onde o utilizador autenticado ainda não avaliou.
        /// Usado no PendingJournalScreen.
        /// </summary>
        [HttpGet("pending-journal")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<MatchDto>>> GetPendingJournal(
            CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            try
            {
                var matches = await _matchService.GetPendingJournalMatchesAsync(userId, cancellationToken);
                if (!matches.Any()) return NoContent();
                return Ok(matches);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>
        /// Fecha o diário de uma partida manualmente (só o criador da partida).
        /// Após fechar, jogadores pendentes ainda podem avaliar.
        /// </summary>
        [HttpPost("{id:guid}/close-journal")]
        [Authorize]
        public async Task<ActionResult> CloseJournal(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
                return BadRequest("O ID da partida não pode ser vazio.");

            var userId = User.GetUserId();
            try
            {
                await _matchService.CloseJournalAsync(id, userId, cancellationToken);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult<MatchDto>> Create(
            [FromBody] CreateMatchDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return BadRequest("Os dados da partida são obrigatórios.");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = User.GetUserId();
            try
            {
                var created = await _matchService.CreateAsync(dto, userId, cancellationToken);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpPut("{id:guid}")]
        [Authorize]
        public async Task<ActionResult> Update(
            Guid id, [FromBody] MatchDto matchDto, CancellationToken cancellationToken)
        {
            if (matchDto == null) return BadRequest("Os dados da partida são obrigatórios.");
            if (id != matchDto.Id) return BadRequest("O ID na URL não corresponde ao ID do corpo.");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existingMatch = await _matchService.GetByIdAsync(id, cancellationToken);
            if (existingMatch == null) return NotFound("Partida não encontrada.");

            var rowsAffected = await _matchService.UpdateAsync(matchDto, cancellationToken);
            if (rowsAffected == 0) return NotFound("Nenhuma partida foi atualizada.");
            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        [Authorize]
        public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest("O ID da partida não pode ser vazio.");
            var success = await _matchService.DeleteAsync(id, cancellationToken);
            if (!success) return NotFound("Partida não encontrada.");
            return NoContent();
        }
    }
}