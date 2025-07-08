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
        private readonly IGameRepository _gameRepository;
        private readonly IBGGService _bggService;
        private readonly IMapper _mapper;
        private readonly ILogger<GameController> _logger;

        public GameController(IGameService gameService, IGameRepository gameRepository, IBGGService bggService, IMapper mapper, ILogger<GameController> logger)
        {
            _gameService = gameService;
            _gameRepository = gameRepository;
            _bggService = bggService;
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
            var total = await _gameRepository.GetAllAsync(0, int.MaxValue, cancellationToken);
            var games = await _gameRepository.GetAllAsync(pageIndex, pageSize, cancellationToken);

            if (games == null || games.Count == 0)
                return NoContent();

            var response = new PagedResponse<GameDto>(
                _mapper.Map<IReadOnlyList<GameDto>>(games),
                total.Count,
                pageSize,
                pageIndex
            );

            return Ok(response);
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
        public async Task<IActionResult> SearchBaseGamesWithFallback(
    [FromQuery] string query,
    [FromQuery] int offset = 0,
    [FromQuery] int limit = 10,
    CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Ok(new List<GameSuggestionDto>());

            // 1. Jogos locais
            var localGames = await _gameRepository.SearchBaseGamesByNameAsync(query, offset, limit, cancellationToken);

            var suggestions = localGames
                .Where(g => g.BGGId.HasValue)
                .Select(g => new GameSuggestionDto
                {
                    BggId = g.BGGId!.Value,
                    Name = g.Name,
                    YearPublished = g.YearPublished,
                    ImageUrl = g.ImageUrl
                })
                .ToList();

            // 2. Se não houver suficientes, busca ao BGG com offset ajustado
            if (suggestions.Count < limit)
            {
                var bggOffset = offset + suggestions.Count;

                var bggSuggestions = await _bggService.SearchGameSuggestionsAsync(
                    query,
                    offset: bggOffset,
                    limit: limit - suggestions.Count,
                    cancellationToken
                );

                var missing = bggSuggestions
                    .Where(b => !b.IsExpansion && !suggestions.Any(s => s.BggId == b.BggId))
                    .Take(limit - suggestions.Count);

                suggestions.AddRange(missing);
            }

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

            // 1. Jogos locais paginados
            var local = await _gameRepository.SearchByNameAsync(query, offset, limit, ct);

            var suggestions = local
                .Where(g => g.BGGId.HasValue)
                .Select(g => new GameSuggestionDto
                {
                    BggId = g.BGGId!.Value,
                    Name = g.Name,
                    YearPublished = g.YearPublished,
                    ImageUrl = g.ImageUrl
                })
                .ToList();

            // 2. Complementa com BGG se faltar
            if (suggestions.Count < limit)
            {
                try
                {
                    var bgg = await _bggService.SearchGameSuggestionsAsync(
                        query,
                        offset: offset + suggestions.Count,
                        limit: limit - suggestions.Count,
                        ct);

                    var extras = bgg
                        .Where(b => !suggestions.Any(s => s.BggId == b.BggId))
                        .Take(limit - suggestions.Count);

                    suggestions.AddRange(extras);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "BGG indisponível");
                    // devolve o que tiveres; a app usará hasMore = false
                    return suggestions.Count == 0
                        ? StatusCode(503)          // ou NoContent()
                        : Ok(suggestions);
                }
            }

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
            if (string.IsNullOrWhiteSpace(query))
                return Ok(new List<GameSuggestionDto>());

            // 🔍 Passo 1: Busca expansões locais
            var localExpansions = await _gameRepository.SearchExpansionsByNameAsync(query, offset, limit, cancellationToken);

            var suggestions = localExpansions
                .Where(g => g.BGGId.HasValue)
                .Select(g => new GameSuggestionDto
                {
                    BggId = g.BGGId!.Value,
                    Name = g.Name,
                    YearPublished = g.YearPublished,
                    ImageUrl = g.ImageUrl
                })
                .ToList();

            // 🔁 Passo 2: Busca do BGG se necessário
            if (suggestions.Count < limit)
            {
                var bggSuggestions = await _bggService.SearchGameSuggestionsAsync(
                    query,
                    offset: 0,
                    limit: limit - suggestions.Count,
                    cancellationToken
                );

                var missingExpansions = bggSuggestions
                    .Where(b => b.IsExpansion && !suggestions.Any(s => s.BggId == b.BggId))
                    .Take(limit - suggestions.Count);

                suggestions.AddRange(missingExpansions);
            }

            return Ok(suggestions);
        }


        /// <summary>
        /// Retorna expansões de um jogo base (mistura local + BGG).
        /// </summary>
        [HttpGet("{baseGameId:guid}/expansion-suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        public async Task<IActionResult> GetExpansionsOfBase(Guid baseGameId, CancellationToken cancellationToken)
        {
            // Passo 1: Busca o jogo base
            var baseGame = await _gameRepository.GetByIdAsync(baseGameId, cancellationToken);
            if (baseGame == null)
                return NotFound("Jogo base não encontrado.");

            // Passo 2: Expansões locais
            var localExpansions = await _gameRepository.GetExpansionsForBaseGameAsync(baseGameId, cancellationToken);

            var suggestions = localExpansions
                .Where(g => g.BGGId.HasValue)
                .Select(g => new GameSuggestionDto
                {
                    BggId = g.BGGId!.Value,
                    Name = g.Name,
                    YearPublished = g.YearPublished,
                    ImageUrl = g.ImageUrl
                })
                .ToList();

            // Passo 3: Expansões do BGG (se o jogo base tiver BGGId)
            if (baseGame.BGGId.HasValue)
            {
                var bggSuggestions = await _bggService.SearchGameSuggestionsAsync(
                    baseGame.Name, // usa o nome do jogo base como query
                    offset: 0,
                    limit: 10,
                    cancellationToken
                );

                var bggExpansions = bggSuggestions
                    .Where(b => b.IsExpansion && !suggestions.Any(s => s.BggId == b.BggId))
                    .Take(10 - suggestions.Count);

                suggestions.AddRange(bggExpansions);
            }

            return Ok(suggestions);
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
