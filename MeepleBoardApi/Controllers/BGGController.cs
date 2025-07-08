using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    [ApiController]
    [Route("MeepleBoard/BGG")]
    public class BGGController : ControllerBase
    {
        private readonly IBGGService _bggService;

        public BGGController(IBGGService bggService)
        {
            _bggService = bggService;
        }

        /// <summary>
        /// Busca um jogo no BGG pelo nome.
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> SearchGame([FromQuery] string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("O nome do jogo não pode estar vazio.");

            var game = await _bggService.GetGameByNameAsync(name, cancellationToken);
            if (game == null)
                return NotFound($"Jogo '{name}' não encontrado.");

            return Ok(game);
        }

        /// <summary>
        /// Obtém detalhes de um jogo no BGG pelo ID do BGG.
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
        {
            var game = await _bggService.GetGameByIdAsync(id, cancellationToken);
            if (game == null)
                return NotFound("Jogo não encontrado.");

            return Ok(game);
        }

        /// <summary>
        /// Obtém a lista dos jogos mais populares (hot list) do BGG.
        /// </summary>
        [HttpGet("hot")]
        [ProducesResponseType(typeof(List<GameDto>), 200)]
        public async Task<IActionResult> GetHotGames(CancellationToken cancellationToken)
        {
            var games = await _bggService.GetHotGamesAsync(cancellationToken);
            return Ok(games);
        }

        /// <summary>
        /// Busca múltiplos jogos por seus IDs no BGG.
        /// </summary>
        [HttpPost("by-ids")]
        [ProducesResponseType(typeof(List<GameDto>), 200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> GetGamesByIds([FromBody] List<string> ids, CancellationToken cancellationToken)
        {
            if (ids == null || ids.Count == 0)
                return BadRequest("A lista de IDs não pode estar vazia.");

            var games = await _bggService.GetGamesByIdsAsync(ids, cancellationToken);
            if (games == null || games.Count == 0)
                return NoContent(); // 204

            return Ok(games);
        }
    }
}