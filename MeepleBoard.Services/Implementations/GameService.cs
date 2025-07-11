using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Serviço de aplicação responsável por coordenar operações de leitura, escrita,
    /// importação e sincronização de jogos. Faz a ponte entre o repositório e serviços externos como o BGG.
    /// </summary>
    public class GameService : IGameService
    {
        private readonly IGameRepository _gameRepository;
        private readonly IBGGService _bggService;
        private readonly IMapper _mapper;
        private readonly ILogger<GameService> _logger;

        public GameService(
            IGameRepository gameRepository,
            IBGGService bggService,
            IMapper mapper,
            ILogger<GameService> logger)
        {
            _gameRepository = gameRepository;
            _bggService = bggService;
            _mapper = mapper;
            _logger = logger;
        }



        /// <summary>
        /// Retrieves a paginated list of games from the repository,
        /// maps them to DTOs, and returns them inside a PagedResponse object.
        /// </summary>
        /// <param name="pageIndex">The current page index (zero-based).</param>
        /// <param name="pageSize">The number of items per page.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>Paged response containing a list of GameDto.</returns>
        public async Task<PagedResponse<GameDto>> GetAllAsync(int pageIndex, int pageSize, CancellationToken ct = default)
        {
            // Fetch all games to calculate the total count.
            // This is used for pagination metadata (total count).
            var totalGames = await _gameRepository.GetAllAsync(0, int.MaxValue, ct);

            // Fetch only the games for the requested page.
            // This is the actual paginated data.
            var pagedGames = await _gameRepository.GetAllAsync(pageIndex, pageSize, ct);

            // Map the domain entities to DTOs, then wrap them in a paginated response.
            return new PagedResponse<GameDto>(
                _mapper.Map<IReadOnlyList<GameDto>>(pagedGames), // Convert games to DTOs
                totalGames.Count,                                // Total number of games in the database
                pageSize,                                        // Number of items requested per page
                pageIndex                                        // Current page index
            );
        }




        /// <summary>
        /// Devolve um jogo pelo seu ID local (GUID), incluindo as expansões se aplicável.
        /// Os dados vêm exclusivamente da base de dados local.
        /// </summary>
        /// <param name="id">ID do jogo (GUID).</param>
        /// <param name="cancellationToken">Token opcional de cancelamento da operação.</param>
        /// <returns>
        /// Um objeto <see cref="GameDto"/> com os dados do jogo.
        /// Se for uma expansão, inclui o ID do jogo base.
        /// Se for um jogo base, inclui as expansões associadas.
        /// </returns>
        public async Task<GameDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            // Obtém o jogo com o ID fornecido da base de dados local.
            var game = await _gameRepository.GetByIdAsync(id, cancellationToken);

            // Retorna null se o jogo não for encontrado localmente.
            if (game == null)
                return null;

            // Mapeia a entidade Game para o DTO (Data Transfer Object).
            var dto = _mapper.Map<GameDto>(game);

            // Se for uma expansão com referência ao jogo base,
            // define o ID do jogo base no DTO (não busca o jogo base completo).
            if (game.IsExpansion && game.BaseGameId.HasValue)
            {
                dto.BaseGameId = game.BaseGameId;
            }
            else
            {
                // Caso contrário, assume que é um jogo base
                // e busca todas as expansões associadas a ele na base local.
                var expansions = await _gameRepository.GetExpansionsForBaseGameAsync(id, cancellationToken);

                // Mapeia as expansões para DTOs e atribui ao DTO do jogo principal.
                dto.Expansions = _mapper.Map<List<GameDto>>(expansions);
            }

            // Retorna o DTO com o jogo e, se aplicável, suas expansões.
            return dto;
        }







        /// <summary>
        /// Devolve um jogo existente na base de dados local com base no nome fornecido.
        /// Não realiza chamadas externas nem importa dados do BGG.
        /// </summary>
        /// <param name="name">Nome do jogo a procurar.</param>
        /// <param name="cancellationToken">Token de cancelamento opcional.</param>
        /// <returns>
        /// Um <see cref="GameDto"/> com os dados do jogo, se encontrado localmente; caso contrário, null.
        /// </returns>
        public async Task<GameDto?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            // Validação defensiva: garante que o nome é válido.
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("O nome do jogo não pode estar vazio.");

            // Remove espaços em branco desnecessários do nome.
            var trimmedName = name.Trim();

            // Log informativo (útil para debugging e auditoria).
            _logger.LogInformation("🔎 Buscando jogo por nome: {Name}", trimmedName);

            // Consulta o repositório local pelo nome do jogo.
            var game = await _gameRepository.GetByNameAsync(trimmedName, cancellationToken);

            // Retorna o DTO do jogo se encontrado; senão, null.
            return game != null ? _mapper.Map<GameDto>(game) : null;
        }









        /// <summary>
        /// Busca um jogo pelo nome: primeiro na base de dados local e, se não for encontrado,
        /// tenta importá-lo do BoardGameGeek (BGG).
        /// </summary>
        /// <param name="name">Nome do jogo a procurar.</param>
        /// <param name="ct">Token de cancelamento opcional.</param>
        /// <returns>
        /// Um <see cref="GameDto"/> representando o jogo, seja local ou recém-importado;
        /// retorna null se o jogo não for encontrado nem localmente nem no BGG.
        /// </returns>
        public async Task<GameDto?> GetOrImportByNameAsync(string name, CancellationToken ct = default)
        {
            // Validação: nome não pode estar vazio ou nulo.
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("O nome do jogo não pode estar vazio.");

            var trimmed = name.Trim();

            // 1) Primeiramente tenta obter o jogo da base de dados local.
            var local = await _gameRepository.GetByNameAsync(trimmed, ct);
            if (local != null)
                return _mapper.Map<GameDto>(local);

            // 2) Se não encontrou localmente, tenta obter informações básicas (incluindo BGGId) via serviço do BGG.
            var bgg = await _bggService.GetGameByNameAsync(trimmed, ct);
            if (bgg?.BggId is null)
                return null; // Se nem o BGG tem, aborta.

            // 3) Se encontrou um BGGId, aciona a importação completa (inclui base + expansões).
            var entity = await ImportGameRecursiveAsync(bgg.BggId.Value, ct);
            return entity == null ? null : _mapper.Map<GameDto>(entity);
        }








        /// <summary>
        /// Tenta associar expansões órfãs ao jogo base recentemente importado.
        /// Expansões "órfãs" são aquelas que possuem o BGGId do jogo base,
        /// mas ainda não estão corretamente ligadas a ele na base de dados.
        /// </summary>
        /// <param name="baseGame">Entidade do jogo base que foi recém importada.</param>
        /// <param name="cancellationToken">Token de cancelamento opcional.</param>
        private async Task TryLinkExpansionsAsync(Game baseGame, CancellationToken cancellationToken)
        {
            // Verifica se o jogo base tem um BGG ID válido.
            if (!baseGame.BGGId.HasValue) return;

            // Busca todas as expansões que foram importadas previamente
            // e que têm o BGG ID do jogo base salvo, mas ainda não estão associadas diretamente a ele.
            var expansions = await _gameRepository.GetExpansionsWithBaseGameBggIdAsync(
                baseGame.BGGId.Value,
                cancellationToken);

            // Itera sobre cada expansão órfã e corrige a relação com o jogo base real.
            foreach (var expansion in expansions)
            {
                // Só atualiza se a expansão ainda não estiver ligada corretamente.
                if (expansion.BaseGameId != baseGame.Id)
                {
                    expansion.SetBaseGame(baseGame);
                    await _gameRepository.UpdateAsync(expansion, cancellationToken);
                }
            }

            // Confirma todas as alterações no repositório.
            await _gameRepository.CommitAsync(cancellationToken);
        }






        /// <summary>
        /// Importa um jogo diretamente do BGG, usando o seu ID.
        /// Se o jogo já existir localmente, ele será retornado. Caso contrário, será importado do BGG.
        /// </summary>
        /// <param name="bggId">ID do jogo na base do BoardGameGeek (BGG).</param>
        /// <param name="ct">Token opcional de cancelamento.</param>
        /// <returns>DTO do jogo importado ou existente, ou null se não encontrado no BGG.</returns>
        public async Task<GameDto?> ImportByBggIdAsync(int bggId, CancellationToken ct = default)
        {
            // Tenta importar recursivamente o jogo (e seus relacionamentos) a partir do BGG.
            // Se já existir localmente, retorna o existente.
            var entity = await ImportGameRecursiveAsync(bggId, ct);

            // Se não encontrou ou não conseguiu importar, retorna null.
            // Caso contrário, faz o mapeamento para DTO e retorna.
            return entity == null ? null : _mapper.Map<GameDto>(entity);
        }





        /// <summary>
        /// Busca sugestões de jogos com base no nome informado, combinando dados locais e do BGG.
        /// </summary>
        /// <param name="query">Termo de pesquisa (nome do jogo).</param>
        /// <param name="offset">Deslocamento para paginação.</param>
        /// <param name="limit">Número máximo de resultados a retornar.</param>
        /// <param name="ct">Token de cancelamento.</param>
        /// <returns>Lista de sugestões de jogos.</returns>
        public async Task<List<GameSuggestionDto>> SearchSuggestionsAsync(string query, int offset = 0, int limit = 10, CancellationToken ct = default)
        {
            // Valida o termo de pesquisa
            if (string.IsNullOrWhiteSpace(query))
                return new List<GameSuggestionDto>();

            // ────────────────────────────────
            // 1. Busca na base de dados local
            // ────────────────────────────────
            var local = await _gameRepository.SearchByNameAsync(query, offset, limit, ct);

            // Converte resultados locais em sugestões
            var suggestions = local
                .Where(g => g.BGGId.HasValue) // ignora jogos sem referência ao BGG
                .Select(g => new GameSuggestionDto
                {
                    BggId = g.BGGId!.Value,
                    Name = g.Name,
                    YearPublished = g.YearPublished,
                    ImageUrl = g.ImageUrl
                })
                .ToList();

            // ───────────────────────────────────────
            // 2. Se faltarem sugestões, consulta o BGG
            // ───────────────────────────────────────
            if (suggestions.Count < limit)
            {
                try
                {
                    var bgg = await _bggService.SearchGameSuggestionsAsync(
                        query,
                        offset: offset + suggestions.Count, // ajusta offset para evitar duplicações
                        limit: limit - suggestions.Count,   // só busca o que ainda falta
                        ct);

                    // Evita repetir sugestões já encontradas localmente
                    var extras = bgg
                        .Where(b => !suggestions.Any(s => s.BggId == b.BggId))
                        .Take(limit - suggestions.Count);

                    suggestions.AddRange(extras);
                }
                catch (HttpRequestException ex)
                {
                    // Em caso de erro de rede com o BGG, apenas registra aviso e segue com os dados locais
                    _logger.LogWarning(ex, "BGG indisponível");
                }
            }

            return suggestions;
        }




        /// <summary>
        /// Retorna sugestões de expansões com base em uma pesquisa por nome,
        /// combinando resultados locais e do BGG.
        /// </summary>
        /// <param name="query">Texto da pesquisa (nome da expansão).</param>
        /// <param name="offset">Deslocamento para paginação.</param>
        /// <param name="limit">Número máximo de resultados.</param>
        /// <param name="cancellationToken">Token de cancelamento opcional.</param>
        /// <returns>Lista de sugestões de expansões.</returns>
        public async Task<List<GameSuggestionDto>> SearchExpansionSuggestionsAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default)
        {
            // Validação básica do termo de busca
            if (string.IsNullOrWhiteSpace(query))
                return new List<GameSuggestionDto>();

            // ─────────────────────────────────────
            // 1. Busca expansões na base de dados local
            // ─────────────────────────────────────
            var localExpansions = await _gameRepository.SearchExpansionsByNameAsync(
                query,
                offset,
                limit,
                cancellationToken);

            var suggestions = localExpansions
                .Where(g => g.BGGId.HasValue) // Garante que tem referência ao BGG
                .Select(g => new GameSuggestionDto
                {
                    BggId = g.BGGId!.Value,
                    Name = g.Name,
                    YearPublished = g.YearPublished,
                    ImageUrl = g.ImageUrl
                })
                .ToList();

            // ─────────────────────────────────────
            // 2. Busca adicionais no BGG (se necessário)
            // ─────────────────────────────────────
            if (suggestions.Count < limit)
            {
                try
                {
                    var bggSuggestions = await _bggService.SearchGameSuggestionsAsync(
                        query,
                        offset: 0,
                        limit: limit - suggestions.Count,
                        cancellationToken);

                    // Filtra apenas expansões e evita duplicatas
                    var missingExpansions = bggSuggestions
                        .Where(b => b.IsExpansion && !suggestions.Any(s => s.BggId == b.BggId))
                        .Take(limit - suggestions.Count);

                    suggestions.AddRange(missingExpansions);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "⚠️ Erro ao consultar expansões no BGG.");
                }
            }

            return suggestions;
        }




        /// <summary>
        /// Retorna sugestões de expansões para um jogo base, combinando dados locais e do BGG.
        /// </summary>
        /// <param name="baseGameId">ID (GUID) do jogo base.</param>
        /// <param name="cancellationToken">Token opcional de cancelamento.</param>
        /// <returns>Lista de sugestões de expansões (GameSuggestionDto).</returns>
        public async Task<List<GameSuggestionDto>> GetExpansionSuggestionsForBaseAsync(
            Guid baseGameId,
            CancellationToken cancellationToken = default)
        {
            // ─────────────────────────────────────
            // 1. Busca o jogo base localmente
            // ─────────────────────────────────────
            var baseGame = await _gameRepository.GetByIdAsync(baseGameId, cancellationToken);
            if (baseGame == null)
                throw new KeyNotFoundException("Jogo base não encontrado.");

            // ─────────────────────────────────────
            // 2. Busca expansões locais relacionadas ao jogo base
            // ─────────────────────────────────────
            var localExpansions = await _gameRepository.GetExpansionsForBaseGameAsync(
                baseGameId,
                cancellationToken);

            var suggestions = localExpansions
                .Where(g => g.BGGId.HasValue) // Garante que as expansões têm BGGId
                .Select(g => new GameSuggestionDto
                {
                    BggId = g.BGGId!.Value,
                    Name = g.Name,
                    YearPublished = g.YearPublished,
                    ImageUrl = g.ImageUrl
                })
                .ToList();

            // ─────────────────────────────────────
            // 3. Complementa com sugestões do BGG (se o jogo base tiver BGGId)
            // ─────────────────────────────────────
            if (baseGame.BGGId.HasValue)
            {
                try
                {
                    var bggSuggestions = await _bggService.SearchGameSuggestionsAsync(
                        baseGame.Name,
                        offset: 0,
                        limit: 10,
                        cancellationToken);

                    // Filtra apenas expansões que ainda não foram adicionadas
                    var bggExpansions = bggSuggestions
                        .Where(b => b.IsExpansion && !suggestions.Any(s => s.BggId == b.BggId))
                        .Take(10 - suggestions.Count);

                    suggestions.AddRange(bggExpansions);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "⚠️ Não foi possível buscar expansões no BGG.");
                }
            }

            return suggestions;
        }





        ///// <summary>
        ///// Pesquisa jogos base localmente e, se necessário, complementa com dados do BGG.
        ///// Jogos importados do BGG são adicionados e persistidos na base de dados.
        ///// </summary>
        ///// <param name="query">Texto de pesquisa (nome do jogo).</param>
        ///// <param name="offset">Deslocamento de paginação para os jogos locais.</param>
        ///// <param name="limit">Número máximo de jogos a retornar.</param>
        ///// <param name="cancellationToken">Token de cancelamento.</param>
        ///// <returns>Lista de GameDto contendo os jogos encontrados ou importados.</returns>
        //public async Task<List<GameDto>> SearchBaseGamesWithFallbackAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default)
        //{
        //    // ─────────────────────────────────────
        //    // 1. Busca local: jogos base já existentes na base de dados
        //    // ─────────────────────────────────────
        //    var localBaseGames = await _gameRepository.SearchBaseGamesByNameAsync(
        //        query, offset, limit, cancellationToken);

        //    // Se já encontrou jogos suficientes localmente, retorna mapeados
        //    if (localBaseGames.Count >= limit)
        //        return _mapper.Map<List<GameDto>>(localBaseGames);

        //    // ─────────────────────────────────────
        //    // 2. Complementa com jogos do BGG (se houver espaço)
        //    // ─────────────────────────────────────
        //    var bggSuggestions = await _bggService.SearchGamesAsync(query, cancellationToken);

        //    // Filtra apenas jogos base (não expansões) com BGG ID
        //    // e que ainda não existem localmente
        //    var filtered = bggSuggestions
        //        .Where(g => !g.IsExpansion && g.BggId.HasValue)
        //        .Where(bgg => !localBaseGames.Any(local => local.BGGId == bgg.BggId))
        //        .Take(limit - localBaseGames.Count)
        //        .ToList();

        //    // ─────────────────────────────────────
        //    // 3. Cria e persiste localmente os jogos importados
        //    // ─────────────────────────────────────
        //    var importedGames = new List<Game>();
        //    foreach (var bgg in filtered)
        //    {
        //        var game = new Game(
        //            bgg.Name,
        //            bgg.Description,
        //            bgg.ImageUrl,
        //            bgg.SupportsSoloMode);

        //        game.SetBggId(bgg.BggId);
        //        game.SetAverageRating(bgg.AverageRating);
        //        game.SetBggRanking(bgg.BggRanking);
        //        game.UpdateBggStats(
        //            bgg.Description,
        //            bgg.ImageUrl,
        //            bgg.BggRanking,
        //            bgg.AverageRating,
        //            bgg.YearPublished);

        //        await _gameRepository.AddAsync(game, cancellationToken);
        //        importedGames.Add(game);
        //    }

        //    // Confirma as inserções no repositório
        //    await _gameRepository.CommitAsync(cancellationToken);

        //    // ─────────────────────────────────────
        //    // 4. Retorna a lista final mapeada (local + importados)
        //    // ─────────────────────────────────────
        //    return _mapper.Map<List<GameDto>>(
        //        localBaseGames.Concat(importedGames).ToList());
        //}










        /// <summary>
        /// Retorna sugestões de jogos base com base numa pesquisa textual, combinando resultados locais com o BGG.
        /// </summary>
        /// <param name="query">Termo de pesquisa (nome do jogo).</param>
        /// <param name="offset">Deslocamento para paginação.</param>
        /// <param name="limit">Número máximo de resultados.</param>
        /// <param name="cancellationToken">Token de cancelamento opcional.</param>
        /// <returns>Lista de sugestões de jogos base.</returns>
        public async Task<List<GameSuggestionDto>> SearchBaseGameSuggestionsAsync(
            string query,
            int offset = 0,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            // 🔎 Verifica se a query é válida
            if (string.IsNullOrWhiteSpace(query))
                return new List<GameSuggestionDto>();

            // ───────────────────────────────────────
            // 1. Busca jogos base localmente (na BD)
            // ───────────────────────────────────────
            var localGames = await _gameRepository.SearchBaseGamesByNameAsync(query, offset, limit, cancellationToken);

            // Converte os resultados locais em sugestões
            var suggestions = localGames
                .Where(g => g.BGGId.HasValue) // Apenas os que têm referência ao BGG
                .Select(g => new GameSuggestionDto
                {
                    BggId = g.BGGId!.Value,
                    Name = g.Name,
                    YearPublished = g.YearPublished,
                    ImageUrl = g.ImageUrl
                })
                .ToList();

            // ──────────────────────────────────────────────
            // 2. Se necessário, complementa com sugestões BGG
            // ──────────────────────────────────────────────
            if (suggestions.Count < limit)
            {
                var bggOffset = offset + suggestions.Count; // ajusta para evitar duplicações

                var bggSuggestions = await _bggService.SearchGameSuggestionsAsync(
                    query,
                    offset: bggOffset,
                    limit: limit - suggestions.Count,
                    cancellationToken
                );

                // Filtra apenas jogos base e evita duplicações
                var missing = bggSuggestions
                    .Where(b => !b.IsExpansion && !suggestions.Any(s => s.BggId == b.BggId))
                    .Take(limit - suggestions.Count);

                suggestions.AddRange(missing);
            }

            // ✅ Retorna a lista final de sugestões
            return suggestions;
        }










        /// <summary>
        /// Retorna a lista de jogos que ainda estão pendentes de aprovação.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento opcional.</param>
        /// <returns>Lista somente leitura de jogos pendentes no formato DTO.</returns>
        public async Task<IReadOnlyList<GameDto>> GetPendingApprovalAsync(CancellationToken cancellationToken = default)
        {
            // 🔍 Obtém todos os jogos com status de aprovação pendente do repositório
            var games = await _gameRepository.GetPendingApprovalAsync(cancellationToken);

            // 🔄 Converte as entidades para DTOs antes de retornar
            return _mapper.Map<IReadOnlyList<GameDto>>(games);
        }









        /// <summary>
        /// Verifica se existe um jogo com o ID especificado.
        /// </summary>
        /// <param name="id">Identificador único do jogo (GUID).</param>
        /// <param name="cancellationToken">Token de cancelamento opcional.</param>
        /// <returns>Verdadeiro se o jogo existir; caso contrário, falso.</returns>
        public async Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            (await _gameRepository.GetByIdAsync(id, cancellationToken)) != null;









        /// <summary>
        /// Verifica se existe um jogo com o nome especificado (ignorando espaços em branco).
        /// </summary>
        /// <param name="name">Nome do jogo a verificar.</param>
        /// <param name="cancellationToken">Token de cancelamento opcional.</param>
        /// <returns>Verdadeiro se o jogo existir na base de dados; caso contrário, falso.</returns>
        public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
            await _gameRepository.ExistsByNameAsync(name.Trim(), cancellationToken);









        /// <summary>
        /// Adiciona um novo jogo à base de dados com base nos dados recebidos.
        /// </summary>
        /// <param name="gameDto">DTO contendo os dados do jogo a adicionar.</param>
        /// <param name="cancellationToken">Token opcional de cancelamento.</param>
        /// <returns>ID do jogo recém-criado.</returns>
        /// <exception cref="ArgumentException">Lançada se já existir um jogo com o mesmo nome.</exception>
        public async Task<Guid> AddAsync(GameDto gameDto, CancellationToken cancellationToken = default)
        {
            // Verifica se já existe um jogo com o mesmo nome (evita duplicações).
            if (await _gameRepository.ExistsByNameAsync(gameDto.Name, cancellationToken))
                throw new ArgumentException("Já existe um jogo com este nome.");

            // Cria uma nova instância da entidade Game com os dados fornecidos.
            var newGame = new Game(
                gameDto.Name,
                gameDto.Description,
                gameDto.ImageUrl,
                gameDto.SupportsSoloMode
            );

            // Se tiver um BGG ID associado, define-o na entidade.
            if (gameDto.BggId.HasValue)
                newGame.SetBggId(gameDto.BggId);

            // Persiste o novo jogo na base de dados.
            await _gameRepository.AddAsync(newGame, cancellationToken);
            await _gameRepository.CommitAsync(cancellationToken);

            // Retorna o ID do jogo recém-adicionado.
            return newGame.Id;
        }





        /// <summary>
        /// Atualiza os dados de um jogo existente com base nas informações do DTO.
        /// </summary>
        /// <param name="gameDto">DTO contendo os dados atualizados do jogo.</param>
        /// <param name="cancellationToken">Token opcional para cancelamento da operação.</param>
        /// <returns>Número de alterações persistidas na base de dados.</returns>
        /// <exception cref="KeyNotFoundException">Lançada se o jogo não for encontrado pelo ID fornecido.</exception>
        public async Task<int> UpdateAsync(GameDto gameDto, CancellationToken cancellationToken = default)
        {
            // Tenta obter o jogo existente pelo ID. Se não encontrar, lança exceção.
            var existingGame = await _gameRepository.GetByIdAsync(gameDto.Id, cancellationToken)
                ?? throw new KeyNotFoundException("Jogo não encontrado.");

            // Atualiza os detalhes do jogo com os novos dados do DTO.
            existingGame.UpdateDetails(
                gameDto.Name,
                gameDto.Description,
                gameDto.ImageUrl,
                gameDto.SupportsSoloMode
            );

            // Persiste as alterações no repositório.
            await _gameRepository.UpdateAsync(existingGame, cancellationToken);

            // Confirma e retorna o número de alterações feitas na base de dados.
            return await _gameRepository.CommitAsync(cancellationToken);
        }





        /// <summary>
        /// Remove um jogo da base de dados com base no seu ID.
        /// </summary>
        /// <param name="id">Identificador único do jogo (GUID).</param>
        /// <param name="cancellationToken">Token opcional para cancelamento da operação.</param>
        /// <returns>Número de alterações persistidas na base de dados.</returns>
        /// <exception cref="KeyNotFoundException">Lançada se o jogo com o ID especificado não for encontrado.</exception>
        public async Task<int> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            // Procura o jogo pelo ID; se não existir, lança exceção.
            var game = await _gameRepository.GetByIdAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException("Jogo não encontrado.");

            // Solicita a remoção do jogo do repositório.
            await _gameRepository.DeleteAsync(game, cancellationToken);

            // Confirma a operação e retorna o número de alterações persistidas.
            return await _gameRepository.CommitAsync(cancellationToken);
        }





        /// <summary>
        /// Marca um jogo como aprovado, permitindo sua visibilidade e uso no sistema.
        /// </summary>
        /// <param name="gameId">ID do jogo a ser aprovado.</param>
        /// <param name="cancellationToken">Token de cancelamento opcional.</param>
        /// <returns>Número de alterações persistidas na base de dados.</returns>
        /// <exception cref="KeyNotFoundException">Se o jogo com o ID fornecido não for encontrado.</exception>
        public async Task<int> ApproveGameAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            // Busca o jogo pelo ID fornecido.
            var game = await _gameRepository.GetByIdAsync(gameId, cancellationToken)
                ?? throw new KeyNotFoundException("Jogo não encontrado.");

            // Altera o estado interno do jogo para aprovado.
            game.ApproveGame();

            // Atualiza o jogo no repositório.
            await _gameRepository.UpdateAsync(game, cancellationToken);

            // Confirma a transação e retorna quantas alterações foram salvas.
            return await _gameRepository.CommitAsync(cancellationToken);
        }






        /// <summary>
        /// Obtém a lista de jogos mais recentemente jogados, até um limite definido.
        /// </summary>
        /// <param name="limit">Número máximo de jogos a retornar.</param>
        /// <param name="cancellationToken">Token opcional para cancelamento da operação.</param>
        /// <returns>Lista somente leitura de jogos (GameDto) ordenada por data de última jogada.</returns>
        public async Task<IReadOnlyList<GameDto>> GetRecentlyPlayedAsync(int limit, CancellationToken cancellationToken = default)
        {
            // Consulta os jogos mais recentemente jogados, limitado ao valor especificado.
            var games = await _gameRepository.GetRecentlyPlayedAsync(limit, cancellationToken);

            // Mapeia as entidades para DTOs e retorna a lista.
            return _mapper.Map<IReadOnlyList<GameDto>>(games);
        }






        /// <summary>
        /// Obtém a lista dos jogos mais pesquisados pelos utilizadores, até um determinado limite.
        /// </summary>
        /// <param name="limit">Número máximo de jogos a retornar.</param>
        /// <param name="cancellationToken">Token opcional de cancelamento.</param>
        /// <returns>Lista somente leitura de objetos GameDto representando os jogos mais pesquisados.</returns>
        public async Task<IReadOnlyList<GameDto>> GetMostSearchedAsync(int limit, CancellationToken cancellationToken = default)
        {
            // Consulta o repositório para obter os jogos mais pesquisados, limitado ao número especificado.
            var games = await _gameRepository.GetMostSearchedAsync(limit, cancellationToken);

            // Mapeia as entidades Game para DTOs antes de retornar.
            return _mapper.Map<IReadOnlyList<GameDto>>(games);
        }






        // -----------------------------------------------------------------------------
        // MÉTODO PRIVADO CENTRAL – Importa um jogo (base ou expansão) recursivamente.
        // -----------------------------------------------------------------------------
        /// <summary>
        /// Importa um jogo a partir do BGG (BoardGameGeek) de forma recursiva, garantindo que expansões
        /// têm seus jogos base previamente importados. Evita ciclos de importação com controle por HashSet.
        /// </summary>
        /// <param name="bggId">ID do jogo no BGG.</param>
        /// <param name="visited">IDs já visitados nesta cadeia recursiva (para evitar loops).</param>
        /// <param name="ct">Token opcional de cancelamento.</param>
        /// <returns>Entidade Game importada e persistida na base de dados, ou null se falhar.</returns>
        private async Task<Game?> ImportGameRecursiveAsync(
            int bggId,
            HashSet<int> visited,
            CancellationToken ct)
        {
            // ✔️ Evita ciclos infinitos em jogos com referências circulares
            if (visited.Contains(bggId))
            {
                _logger.LogInformation("🔁 BGG ID {BggId} já visitado. Evitando loop.", bggId);
                return await _gameRepository.GetGameByBggIdAsync(bggId, ct);
            }

            visited.Add(bggId);

            // A) Verifica se já existe localmente
            var existing = await _gameRepository.GetGameByBggIdAsync(bggId, ct);
            if (existing != null)
            {
                _logger.LogInformation("✅ Jogo já existente no DB: {GameName} (BGG ID: {BggId})", existing.Name, bggId);
                return existing;
            }

            // B) Consulta dados no BGG
            var bgg = await _bggService.GetGameByIdAsync(bggId.ToString(), ct);
            if (bgg == null)
            {
                _logger.LogWarning("❌ Nenhum jogo encontrado no BGG com o ID {BggId}", bggId);
                return null;
            }

            _logger.LogInformation("📦 Jogo encontrado no BGG: {Name} (ID: {BggId}) - Expansão: {IsExpansion}", bgg.Name, bgg.BggId, bgg.IsExpansion);

            // C) Cria nova entidade Game com os dados do BGG
            var game = new Game(bgg.Name, bgg.Description, bgg.ImageUrl, bgg.SupportsSoloMode);
            game.SetBggId(bgg.BggId);
            game.SetAverageRating(bgg.AverageRating);
            game.SetBggRanking(bgg.BggRanking);
            game.UpdateBggStats(bgg.Description, bgg.ImageUrl, bgg.BggRanking, bgg.AverageRating, bgg.YearPublished);

            // D) Se for expansão, importa também o jogo base
            if (bgg.IsExpansion && bgg.BaseGameBggId.HasValue)
            {
                _logger.LogInformation("🔧 Importando jogo base para expansão '{Name}' (BaseGameBggId: {BaseId})", bgg.Name, bgg.BaseGameBggId.Value);

                var baseGame = await ImportGameRecursiveAsync(bgg.BaseGameBggId.Value, visited, ct);

                if (baseGame != null)
                {
                    game.SetBaseGame(baseGame);
                }
                else
                {
                    // Associa por ID BGG, mesmo que não exista localmente ainda
                    game.SetBaseGameBggId(bgg.BaseGameBggId);
                    _logger.LogWarning("⚠️ Jogo base não foi importado para expansão '{Name}'", bgg.Name);
                }
            }

            // E) Persiste o jogo na base de dados
            await _gameRepository.AddAsync(game, ct);
            await _gameRepository.CommitAsync(ct);
            _logger.LogInformation("💾 Jogo salvo no banco de dados: {Name} (BGG ID: {BggId})", game.Name, game.BGGId);

            // F) Se for um jogo base, tenta ligar expansões órfãs que tenham referenciado este jogo via BGG ID
            if (!bgg.IsExpansion)
            {
                _logger.LogInformation("🔍 Tentando ligar expansões órfãs ao jogo base '{Name}'", game.Name);
                await TryLinkExpansionsAsync(game, ct);
            }

            return game;
        }

        // Sobrecarga conveniente que inicializa o conjunto de visitados
        private Task<Game?> ImportGameRecursiveAsync(int bggId, CancellationToken ct) =>
            ImportGameRecursiveAsync(bggId, new HashSet<int>(), ct);








        /// <summary>
        /// Atualiza os dados de um jogo local com base nas informações mais recentes obtidas do BGG (BoardGameGeek).
        /// </summary>
        /// <param name="game">Objeto DTO do jogo que se deseja atualizar.</param>
        /// <param name="cancellationToken">Token opcional para cancelamento da operação.</param>
        /// <returns>
        /// Retorna <c>true</c> se a atualização foi bem-sucedida, ou <c>false</c>
        /// se o jogo não tiver BGG ID, não for encontrado localmente ou a chamada ao BGG falhar.
        /// </returns>
        public async Task<bool> UpdateFromBggAsync(GameDto game, CancellationToken cancellationToken = default)
        {
            // 🔎 Valida se o jogo tem um ID do BGG
            if (!game.BggId.HasValue)
            {
                _logger.LogWarning("⚠️ Jogo '{GameId}' sem BGG ID. Atualização não possível.", game.Id);
                return false;
            }

            // 🌐 Busca os dados atualizados do jogo no BGG
            var bggUpdated = await _bggService.GetGameByIdAsync(game.BggId.Value.ToString(), cancellationToken);
            if (bggUpdated == null)
            {
                _logger.LogWarning("❌ Falha ao obter dados do BGG para jogo '{GameId}'.", game.Id);
                return false;
            }

            // 🔍 Busca o jogo atual na base local
            var existingGame = await _gameRepository.GetByIdAsync(game.Id, cancellationToken);
            if (existingGame == null)
            {
                _logger.LogWarning("❌ Jogo local com ID '{GameId}' não encontrado.", game.Id);
                return false;
            }

            // 🛠️ Atualiza os campos relevantes com os dados do BGG
            existingGame.UpdateDetails(
                bggUpdated.Name,
                bggUpdated.Description,
                bggUpdated.ImageUrl,
                bggUpdated.SupportsSoloMode
            );

            existingGame.SetBggRanking(bggUpdated.BggRanking);
            existingGame.SetAverageRating(bggUpdated.AverageRating);

            // 💾 Persiste as alterações na base de dados
            await _gameRepository.UpdateAsync(existingGame, cancellationToken);
            await _gameRepository.CommitAsync(cancellationToken);

            _logger.LogInformation("✅ Jogo '{Name}' sincronizado com sucesso com o BGG.", existingGame.Name);
            return true;
        }

    }
}
