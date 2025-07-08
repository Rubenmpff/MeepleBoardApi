using AutoMapper;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.AspNetCore.Mvc;


namespace MeepleBoardApi.Controllers
{
    [ApiController]
    [Route("MeepleBoard/game")]
    public class GameController : ControllerBase
    {
        private readonly IGameService _gameService;
        private readonly IMapper _mapper;
        private readonly ILogger<GameController> _logger;

        public GameController(IGameService gameService, IMapper mapper, ILogger<GameController> logger)
        {
            _gameService = gameService;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>
        /// Retorna todos os jogos cadastrados no sistema com paginação.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResponse<GameDto>), 200)]
        [ProducesResponseType(204)]
        public async Task<IActionResult> GetAll(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            var result = await _gameService.GetAllAsync(pageIndex, pageSize, cancellationToken);

            if (result.Data == null || result.Data.Count == 0)
                return NoContent();

            return Ok(result);
        }




        /// <summary>
        /// Busca um jogo localmente ou importa automaticamente do BGG se não existir.
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> SearchOrImport([FromQuery] string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("O nome do jogo é obrigatório.");

            var game = await _gameService.GetOrImportByNameAsync(name, cancellationToken);
            return game == null
                ? NotFound($"Jogo '{name}' não encontrado no BGG.")
                : Ok(game);
        }




        [HttpPost("import/{bggId:int}")]
        public async Task<ActionResult<GameDto>> ImportByBggId(int bggId, CancellationToken cancellationToken)
        {
            var game = await _gameService.ImportByBggIdAsync(bggId, cancellationToken);
            if (game == null)
                return NotFound($"Jogo com BGG ID {bggId} não encontrado.");

            return Ok(game);
        }






        /// <summary>
        /// Sugestões de jogos base (mistura local + BGG, sem guardar na base).
        /// </summary>
        [HttpGet("base-search")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        public async Task<IActionResult> SearchBaseGamesWithFallback([FromQuery] string query, [FromQuery] int offset = 0, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
        {
            var suggestions = await _gameService.SearchBaseGameSuggestionsAsync(query, offset, limit, cancellationToken);
            return Ok(suggestions);
        }







        /// <summary>
        /// Sugestões rápidas (mistura local + BGG, sem guardar na base).
        /// </summary>
        [HttpGet("suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(503)]
        public async Task<IActionResult> SearchSuggestions(
    [FromQuery] string query,
    [FromQuery] int offset = 0,
    [FromQuery] int limit = 10,
    CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Ok(Array.Empty<GameSuggestionDto>());

            var suggestions = await _gameService.SearchSuggestionsAsync(query, offset, limit, ct);

            return suggestions.Count == 0 ? NoContent() : Ok(suggestions);
        }



        /// <summary>
        /// Sugestões de expansões (mistura local + BGG, sem guardar na base).
        /// </summary>
        [HttpGet("expansion-suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        public async Task<IActionResult> SearchExpansions(
    [FromQuery] string query,
    [FromQuery] int offset = 0,
    [FromQuery] int limit = 10,
    CancellationToken cancellationToken = default)
        {
            var suggestions = await _gameService.SearchExpansionSuggestionsAsync(query, offset, limit, cancellationToken);
            return Ok(suggestions);
        }



        /// <summary>
        /// Retorna expansões de um jogo base (mistura local + BGG).
        /// </summary>
        [HttpGet("{baseGameId:guid}/expansion-suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        public async Task<IActionResult> GetExpansionsOfBase(Guid baseGameId, CancellationToken cancellationToken)
        {
            try
            {
                var suggestions = await _gameService.GetExpansionSuggestionsForBaseAsync(baseGameId, cancellationToken);
                return Ok(suggestions);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }



        /// <summary>
        /// Busca um jogo pelo ID único (GUID).
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
                return BadRequest("O ID fornecido é inválido.");

            var game = await _gameService.GetByIdAsync(id, cancellationToken);
            return game == null
                ? NotFound("Jogo não encontrado.")
                : Ok(game);
        }





        /// <summary>
        /// Cadastra manualmente um novo jogo (sem importar do BGG).
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(Guid), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create([FromBody] GameDto gameDto, CancellationToken cancellationToken)
        {
            if (gameDto == null)
                return BadRequest("Dados do jogo são obrigatórios.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var gameId = await _gameService.AddAsync(gameDto, cancellationToken);
                return CreatedAtAction(nameof(GetById), new { id = gameId }, gameId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
