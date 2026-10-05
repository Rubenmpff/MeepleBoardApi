using AutoMapper;
using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.AspNetCore.Authorization;
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

        public GameController(
            IGameService gameService,
            IMapper mapper,
            ILogger<GameController> logger)
        {
            _gameService = gameService;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>
        /// Retorna todos os jogos cadastrados no sistema com suporte a paginação.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResponse<GameDto>), 200)]
        [ProducesResponseType(204)]
        public async Task<IActionResult> GetAll(
            int pageIndex = 0,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            var result =
                await _gameService.GetAllAsync(
                    pageIndex,
                    pageSize,
                    cancellationToken);

            if (result.Data == null ||
                result.Data.Count == 0)
            {
                return NoContent();
            }

            return Ok(result);
        }

        /// <summary>
        /// Ranking de jogos ordenado pela fonte escolhida.
        /// </summary>
        [HttpGet("rankings")]
        [ProducesResponseType(typeof(PagedResponse<GameDto>), 200)]
        [ProducesResponseType(204)]
        public async Task<IActionResult> GetRankings(
            int pageIndex = 0,
            int pageSize = 20,
            string source = "meepleboard",
            CancellationToken cancellationToken = default)
        {
            var result =
                await _gameService.GetRankingsAsync(
                    pageIndex,
                    pageSize,
                    source,
                    cancellationToken);

            if (result.Data == null ||
                result.Data.Count == 0)
            {
                return NoContent();
            }

            return Ok(result);
        }

        /// <summary>
        /// Ranking pessoal do utilizador autenticado.
        /// </summary>
        [HttpGet("rankings/mine")]
        [Authorize]
        public async Task<IActionResult> GetMyRankings(
            int pageIndex = 0,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var userId = User.GetUserId();

            var result =
                await _gameService.GetPersonalRankingsAsync(
                    userId,
                    pageIndex,
                    pageSize,
                    cancellationToken);

            if (result.Data == null ||
                result.Data.Count == 0)
            {
                return NoContent();
            }

            return Ok(result);
        }

        /// <summary>
        /// Recalcula o MeepleBoardScore de todos os jogos.
        /// </summary>
        [HttpPost("rankings/recompute")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RecomputeRankings(
            CancellationToken cancellationToken = default)
        {
            var updated =
                await _gameService.RecomputeAllMeepleBoardScoresAsync(
                    cancellationToken);

            return Ok(new { updated });
        }

        /// <summary>
        /// Procura um jogo pelo nome na base local e, se necessário,
        /// executa uma importação explícita do BGG.
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> SearchOrImport(
            [FromQuery] string name,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(
                    "O nome do jogo é obrigatório.");
            }

            var game =
                await _gameService.GetOrImportByNameAsync(
                    name,
                    cancellationToken);

            if (game == null)
            {
                return NotFound(
                    $"Jogo '{name}' não encontrado no BGG.");
            }

            return Ok(game);
        }

        /// <summary>
        /// Importa explicitamente um jogo pelo BGG ID.
        /// </summary>
        [HttpPost("import/{bggId:int}")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<GameDto>> ImportByBggId(
            int bggId,
            CancellationToken cancellationToken)
        {
            var game =
                await _gameService.ImportByBggIdAsync(
                    bggId,
                    cancellationToken);

            if (game == null)
            {
                return NotFound(
                    $"Jogo com BGG ID {bggId} não encontrado.");
            }

            return Ok(game);
        }

        /// <summary>
        /// Pesquisa apenas jogos base.
        ///
        /// A pesquisa normal usa Games locais + GameSearchCatalog.
        /// Não chama o BGG em tempo real.
        /// </summary>
        [HttpGet("base-search")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        public async Task<IActionResult> SearchBaseGamesWithFallback(
            [FromQuery] string query,
            [FromQuery] int offset = 0,
            [FromQuery] int limit = 10,
            [FromQuery] int? playerCount = null,
            [FromQuery] double? minBggRating = null,
            [FromQuery] string sort = "relevance",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Ok(
                    Array.Empty<GameSuggestionDto>());
            }

            try
            {
                var suggestions =
                    await _gameService.SearchBaseGameSuggestionsAsync(
                        query,
                        offset,
                        limit,
                        playerCount,
                        minBggRating,
                        sort,
                        cancellationToken);

                return Ok(suggestions);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(
                    "Pesquisa de jogos base cancelada pelo cliente: {Query}",
                    query);

                return new EmptyResult();
            }
        }

        /// <summary>
        /// Pesquisa sugestões de jogos e expansões.
        ///
        /// Fluxo:
        /// - Games locais;
        /// - GameSearchCatalog;
        /// - filtros e ordenação antes da paginação;
        /// - sem chamadas ao BGG no caminho crítico.
        /// </summary>
        [HttpGet("suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        public async Task<IActionResult> SearchSuggestions(
            [FromQuery] string query,
            [FromQuery] int offset = 0,
            [FromQuery] int limit = 10,
            [FromQuery] bool? isExpansion = null,
            [FromQuery] int? playerCount = null,
            [FromQuery] double? minBggRating = null,
            [FromQuery] string sort = "relevance",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Ok(
                    Array.Empty<GameSuggestionDto>());
            }

            try
            {
                var suggestions =
                    await _gameService.SearchSuggestionsAsync(
                        query,
                        offset,
                        limit,
                        isExpansion,
                        playerCount,
                        minBggRating,
                        sort,
                        ct);

                /*
                 * Para autocomplete, uma lista vazia continua a ser um resultado
                 * válido. Mantemos sempre 200 OK para simplificar o frontend.
                 */
                return Ok(suggestions);
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                /*
                 * O autocomplete cancela o pedido anterior quando o utilizador
                 * continua a escrever, apaga texto, muda filtros ou ordenação.
                 *
                 * Isto é comportamento normal e não deve ser tratado como erro
                 * da aplicação nem interromper o debugger como user-unhandled.
                 */
                _logger.LogDebug(
                    "Pesquisa de sugestões cancelada pelo cliente: {Query}",
                    query);

                return new EmptyResult();
            }
        }

        /// <summary>
        /// Pesquisa apenas expansões.
        ///
        /// A pesquisa normal usa Games locais + GameSearchCatalog.
        /// Não chama o BGG em tempo real.
        /// </summary>
        [HttpGet("expansion-suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        public async Task<IActionResult> SearchExpansions(
            [FromQuery] string query,
            [FromQuery] int offset = 0,
            [FromQuery] int limit = 10,
            [FromQuery] int? playerCount = null,
            [FromQuery] double? minBggRating = null,
            [FromQuery] string sort = "relevance",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Ok(
                    Array.Empty<GameSuggestionDto>());
            }

            try
            {
                var suggestions =
                    await _gameService.SearchExpansionSuggestionsAsync(
                        query,
                        offset,
                        limit,
                        playerCount,
                        minBggRating,
                        sort,
                        cancellationToken);

                return Ok(suggestions);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(
                    "Pesquisa de expansões cancelada pelo cliente: {Query}",
                    query);

                return new EmptyResult();
            }
        }

        /// <summary>
        /// Retorna sugestões de expansões para um jogo base específico.
        /// </summary>
        [HttpGet("{baseGameId:guid}/expansion-suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetExpansionsOfBase(
            Guid baseGameId,
            CancellationToken cancellationToken)
        {
            try
            {
                var suggestions =
                    await _gameService.GetExpansionSuggestionsForBaseAsync(
                        baseGameId,
                        cancellationToken);

                return Ok(suggestions);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        /// Obtém um jogo pelo ID local.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(
            Guid id,
            CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
            {
                return BadRequest(
                    "O ID fornecido é inválido.");
            }

            var game =
                await _gameService.GetByIdAsync(
                    id,
                    cancellationToken);

            if (game == null)
            {
                return NotFound(
                    "Jogo não encontrado.");
            }

            return Ok(game);
        }

        /// <summary>
        /// Regista manualmente um novo jogo.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(Guid), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create(
            [FromBody] GameDto gameDto,
            CancellationToken cancellationToken)
        {
            if (gameDto == null)
            {
                return BadRequest(
                    "Dados do jogo são obrigatórios.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(
                    ModelState);
            }

            try
            {
                var gameId =
                    await _gameService.AddAsync(
                        gameDto,
                        cancellationToken);

                return CreatedAtAction(
                    nameof(GetById),
                    new { id = gameId },
                    gameId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(
                    ex.Message);
            }
        }
    }
}
