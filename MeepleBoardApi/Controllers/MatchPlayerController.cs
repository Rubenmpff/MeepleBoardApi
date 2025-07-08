using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    [Route("MeepleBoard/matches")]
    [ApiController]
    public class MatchPlayerController : ControllerBase
    {
        private readonly IMatchPlayerService _matchPlayerService;

        public MatchPlayerController(IMatchPlayerService matchPlayerService)
        {
            _matchPlayerService = matchPlayerService;
        }

        /// <summary>
        /// 🔹 Adiciona um jogador à partida.
        /// </summary>
        /// <param name="matchId">ID da partida.</param>
        /// <param name="playerDto">Dados do jogador.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>201 Created se o jogador foi adicionado.</returns>
        /// <response code="201">Jogador adicionado com sucesso.</response>
        /// <response code="400">Dados inválidos.</response>
        /// <response code="404">Partida não encontrada.</response>
        /// <response code="409">O jogador já está na partida.</response>
        [HttpPost("{matchId:guid}/players")]
        public async Task<ActionResult> AddPlayer(Guid matchId, [FromBody] MatchPlayerDto playerDto, CancellationToken cancellationToken)
        {
            if (playerDto == null)
                return BadRequest("Os dados do jogador são obrigatórios.");

            if (playerDto.UserId == Guid.Empty)
                return BadRequest("O ID do jogador não pode ser vazio.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                await _matchPlayerService.AddPlayerToMatchAsync(matchId, playerDto.UserId, cancellationToken);
                return CreatedAtAction(nameof(AddPlayer), new { matchId, playerId = playerDto.UserId }, "Jogador adicionado à partida com sucesso.");
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erro interno ao adicionar jogador: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔹 Remove um jogador da partida.
        /// </summary>
        /// <param name="matchId">ID da partida.</param>
        /// <param name="playerId">ID do jogador.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>204 No Content se removido.</returns>
        /// <response code="204">Jogador removido com sucesso.</response>
        /// <response code="400">ID inválido.</response>
        /// <response code="404">Jogador ou partida não encontrados.</response>
        [HttpDelete("{matchId:guid}/players/{playerId:guid}")]
        public async Task<ActionResult> RemovePlayer(Guid matchId, Guid playerId, CancellationToken cancellationToken)
        {
            if (playerId == Guid.Empty)
                return BadRequest("O ID do jogador não pode ser vazio.");

            try
            {
                await _matchPlayerService.RemovePlayerFromMatchAsync(matchId, playerId, cancellationToken);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erro interno ao remover jogador: {ex.Message}");
            }
        }
    }
}