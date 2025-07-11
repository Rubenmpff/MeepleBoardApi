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
        /// Retorna todos os jogos cadastrados no sistema com suporte a paginação.
        /// </summary>
        /// <param name="pageIndex">Índice da página (começando em 0).</param>
        /// <param name="pageSize">Quantidade de itens por página.</param>
        /// <param name="cancellationToken">Token opcional para cancelamento da operação.</param>
        /// <returns>
        /// Um <see cref="PagedResponse{GameDto}"/> com os jogos encontrados, ou 204 se nenhum jogo for encontrado.
        /// </returns>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResponse<GameDto>), 200)] // Sucesso com conteúdo paginado
        [ProducesResponseType(204)] // Nenhum conteúdo encontrado
        public async Task<IActionResult> GetAll( int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            // Solicita ao serviço a lista paginada de jogos
            var result = await _gameService.GetAllAsync(pageIndex, pageSize, cancellationToken);

            // Se não há jogos encontrados na página solicitada, retorna HTTP 204 (No Content)
            if (result.Data == null || result.Data.Count == 0)
                return NoContent();

            // Caso contrário, retorna HTTP 200 com os dados paginados
            return Ok(result);
        }







        /// <summary>
        /// Busca um jogo pelo nome na base local. Caso não exista, tenta importar do BGG automaticamente.
        /// </summary>
        /// <param name="name">Nome do jogo a ser pesquisado.</param>
        /// <param name="cancellationToken">Token opcional de cancelamento.</param>
        /// <returns>
        /// Retorna o jogo encontrado ou importado como <see cref="GameDto"/>.
        /// - 200: Se o jogo for encontrado localmente ou importado com sucesso.
        /// - 404: Se o jogo não for encontrado nem na base local nem no BGG.
        /// - 400: Se o parâmetro de nome estiver vazio ou inválido.
        /// </returns>
        [HttpGet("search")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> SearchOrImport( [FromQuery] string name, CancellationToken cancellationToken)
        {
            // Valida se o nome foi informado corretamente
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("O nome do jogo é obrigatório.");

            // Tenta buscar localmente ou importar do BGG
            var game = await _gameService.GetOrImportByNameAsync(name, cancellationToken);

            // Se não encontrar nem localmente nem no BGG
            if (game == null)
                return NotFound($"Jogo '{name}' não encontrado no BGG.");

            // Retorna o jogo encontrado (local ou importado)
            return Ok(game);
        }








        /// <summary>
        /// Importa um jogo diretamente do BoardGameGeek (BGG) com base no seu BGG ID.
        /// </summary>
        /// <param name="bggId">Identificador único do jogo no BGG.</param>
        /// <param name="cancellationToken">Token opcional de cancelamento da requisição.</param>
        /// <returns>
        /// Retorna os dados importados do jogo como <see cref="GameDto"/>.
        /// - 200: Se o jogo foi importado com sucesso.
        /// - 404: Se o jogo não for encontrado no BGG.
        /// </returns>
        [HttpPost("import/{bggId:int}")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<GameDto>> ImportByBggId(int bggId, CancellationToken cancellationToken)
        {
            // Tenta importar o jogo do BGG utilizando o ID fornecido
            var game = await _gameService.ImportByBggIdAsync(bggId, cancellationToken);

            // Se não encontrou o jogo no BGG, retorna 404
            if (game == null)
                return NotFound($"Jogo com BGG ID {bggId} não encontrado.");

            // Jogo importado com sucesso
            return Ok(game);
        }







        /// <summary>
        /// Retorna sugestões de jogos base, combinando dados locais e do BGG sem persistir os dados externos.
        /// </summary>
        /// <param name="query">Termo de busca (nome parcial ou completo do jogo).</param>
        /// <param name="offset">Número de itens a pular (útil para paginação).</param>
        /// <param name="limit">Número máximo de sugestões a retornar.</param>
        /// <param name="cancellationToken">Token opcional de cancelamento da requisição.</param>
        /// <returns>
        /// Lista de sugestões de jogos base que correspondem à pesquisa.
        /// Sempre retorna 200 OK com uma lista (vazia ou preenchida).
        /// </returns>
        [HttpGet("base-search")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        public async Task<IActionResult> SearchBaseGamesWithFallback( [FromQuery] string query, [FromQuery] int offset = 0, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
        {
            // Chama o serviço para obter sugestões, misturando dados locais e do BGG
            var suggestions = await _gameService.SearchBaseGameSuggestionsAsync(
                query, offset, limit, cancellationToken);

            // Retorna a lista (vazia ou preenchida) com status 200
            return Ok(suggestions);
        }








        /// <summary>
        /// Retorna sugestões rápidas de jogos baseando-se em uma pesquisa por nome.
        /// Os resultados misturam dados locais com o BGG, mas não são persistidos no banco.
        /// </summary>
        /// <param name="query">Texto da pesquisa (nome do jogo).</param>
        /// <param name="offset">Número de itens a pular para paginação.</param>
        /// <param name="limit">Número máximo de sugestões a retornar.</param>
        /// <param name="ct">Token de cancelamento para a operação assíncrona.</param>
        /// <returns>
        /// 200 OK com a lista de sugestões,  
        /// 204 NoContent se não houver resultados,  
        /// 503 ServiceUnavailable se o BGG estiver indisponível (tratado no serviço).
        /// </returns>
        [HttpGet("suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        [ProducesResponseType(204)]
        [ProducesResponseType(503)]
        public async Task<IActionResult> SearchSuggestions([FromQuery] string query, [FromQuery] int offset = 0, [FromQuery] int limit = 10, CancellationToken ct = default)
        {
            // Valida a query para evitar chamadas desnecessárias
            if (string.IsNullOrWhiteSpace(query))
                return Ok(Array.Empty<GameSuggestionDto>());

            // Chama o serviço para obter sugestões combinando dados locais e do BGG
            var suggestions = await _gameService.SearchSuggestionsAsync(query, offset, limit, ct);

            // Retorna 204 se nenhuma sugestão foi encontrada
            return suggestions.Count == 0
                ? NoContent()
                : Ok(suggestions);
        }




        /// <summary>
        /// Retorna sugestões de expansões de jogos com base em uma pesquisa por nome.
        /// Os resultados combinam dados locais e do BGG, sem persistência no banco de dados.
        /// </summary>
        /// <param name="query">Texto de busca (nome parcial ou completo da expansão).</param>
        /// <param name="offset">Número de resultados a pular (para paginação).</param>
        /// <param name="limit">Número máximo de sugestões a retornar.</param>
        /// <param name="cancellationToken">Token para cancelamento assíncrono.</param>
        /// <returns>
        /// 200 OK com a lista de sugestões encontradas (pode estar vazia).
        /// </returns>
        [HttpGet("expansion-suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        public async Task<IActionResult> SearchExpansions( [FromQuery] string query, [FromQuery] int offset = 0, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
        {
            // Executa a busca de sugestões de expansões via serviço
            var suggestions = await _gameService.SearchExpansionSuggestionsAsync(query, offset, limit, cancellationToken);

            // Retorna a lista (pode ser vazia, mas sempre com status 200 OK)
            return Ok(suggestions);
        }




        /// <summary>
        /// Retorna sugestões de expansões para um jogo base, combinando dados locais e do BGG.
        /// </summary>
        /// <param name="baseGameId">Identificador (GUID) do jogo base.</param>
        /// <param name="cancellationToken">Token de cancelamento assíncrono.</param>
        /// <returns>
        /// 200 OK com uma lista de sugestões de expansões, ou
        /// 404 Not Found se o jogo base não for encontrado.
        /// </returns>
        [HttpGet("{baseGameId:guid}/expansion-suggestions")]
        [ProducesResponseType(typeof(List<GameSuggestionDto>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetExpansionsOfBase(Guid baseGameId, CancellationToken cancellationToken)
        {
            try
            {
                // Solicita ao serviço a lista de expansões para o jogo base fornecido
                var suggestions = await _gameService.GetExpansionSuggestionsForBaseAsync(baseGameId, cancellationToken);

                // Retorna a lista com status 200 OK (pode estar vazia)
                return Ok(suggestions);
            }
            catch (KeyNotFoundException ex)
            {
                // Se o jogo base não foi encontrado na base local, retorna 404 com a mensagem
                return NotFound(ex.Message);
            }
        }




        /// <summary>
        /// Busca um jogo pelo ID único (GUID), retornando também suas expansões se for um jogo base,
        /// ou o jogo base se for uma expansão.
        /// </summary>
        /// <param name="id">Identificador único do jogo (GUID).</param>
        /// <param name="cancellationToken">Token opcional de cancelamento da operação assíncrona.</param>
        /// <returns>
        /// 200 OK com os dados do jogo (incluindo expansões ou jogo base, se aplicável);
        /// 400 Bad Request se o ID for inválido;
        /// 404 Not Found se o jogo não for encontrado.
        /// </returns>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(GameDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            // Verifica se o GUID é inválido (valor vazio)
            if (id == Guid.Empty)
                return BadRequest("O ID fornecido é inválido.");

            // Consulta o jogo no serviço de aplicação
            var game = await _gameService.GetByIdAsync(id, cancellationToken);

            // Se não encontrar o jogo, retorna 404
            if (game == null)
                return NotFound("Jogo não encontrado.");

            // Caso contrário, retorna 200 com os dados do jogo
            return Ok(game);
        }








        /// <summary>
        /// Registra manualmente um novo jogo no sistema (sem importar dados do BGG).
        /// </summary>
        /// <param name="gameDto">Objeto com os dados do jogo.</param>
        /// <param name="cancellationToken">Token opcional para cancelamento da operação.</param>
        /// <returns>
        /// 201 Created com o ID do novo jogo;
        /// 400 Bad Request se os dados estiverem inválidos ou já existir um jogo com o mesmo nome.
        /// </returns>
        [HttpPost]
        [ProducesResponseType(typeof(Guid), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create([FromBody] GameDto gameDto, CancellationToken cancellationToken)
        {
            // Valida se o payload foi enviado
            if (gameDto == null)
                return BadRequest("Dados do jogo são obrigatórios.");

            // Valida o modelo (anotações de data annotations, como [Required], [MaxLength], etc.)
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // Chama o serviço de aplicação para criar o jogo
                var gameId = await _gameService.AddAsync(gameDto, cancellationToken);

                // Retorna 201 Created, apontando para o endpoint GetById recém-criado
                return CreatedAtAction(nameof(GetById), new { id = gameId }, gameId);
            }
            catch (ArgumentException ex)
            {
                // Em caso de violação de regra de negócio (ex: nome duplicado), retorna erro 400 com a mensagem
                return BadRequest(ex.Message);
            }
        }

    }
}
