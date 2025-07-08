using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
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
        /// Devolve todos os jogos, sem paginação.
        /// </summary>
        public async Task<IReadOnlyList<GameDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var games = await _gameRepository.GetAllAsync(-1, -1, cancellationToken);
            return _mapper.Map<IReadOnlyList<GameDto>>(games);
        }

        /// <summary>
        /// Devolve um jogo por ID, incluindo as expansões se aplicável.
        /// </summary>
        public async Task<GameDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var game = await _gameRepository.GetByIdAsync(id, cancellationToken);
            if (game == null) return null;

            var dto = _mapper.Map<GameDto>(game);

            if (game.IsExpansion && game.BaseGameId.HasValue)
                dto.BaseGameId = game.BaseGameId;
            else
            {
                var expansions = await _gameRepository.GetExpansionsForBaseGameAsync(id, cancellationToken);
                dto.Expansions = _mapper.Map<List<GameDto>>(expansions);
            }

            return dto;
        }

        /// <summary>
        /// Devolve um jogo por nome, se estiver na base de dados.
        /// </summary>
        public async Task<GameDto?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("O nome do jogo não pode estar vazio.");

            var trimmedName = name.Trim();
            _logger.LogInformation("🔎 Buscando jogo por nome: {Name}", trimmedName);

            var game = await _gameRepository.GetByNameAsync(trimmedName, cancellationToken);
            return game != null ? _mapper.Map<GameDto>(game) : null;
        }

        /// <summary>
        /// Busca um jogo localmente ou importa automaticamente do BGG se não existir.
        /// </summary>
        public async Task<GameDto?> GetOrImportByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("O nome do jogo não pode estar vazio.");

            var trimmedName = name.Trim();
            _logger.LogInformation("🔍 Iniciando processo de busca/importação para: {Name}", trimmedName);

            var localGame = await _gameRepository.GetByNameAsync(trimmedName, cancellationToken);
            if (localGame != null)
            {
                _logger.LogInformation("✅ Jogo encontrado localmente: {Name}", trimmedName);
                return _mapper.Map<GameDto>(localGame);
            }

            // Busca no BGG
            var bggGame = await _bggService.GetGameByNameAsync(trimmedName, cancellationToken);
            if (bggGame == null)
            {
                _logger.LogWarning("⚠️ Jogo '{Name}' não encontrado no BGG.", trimmedName);
                return null;
            }

            // Verifica duplicados por BGG ID
            if (bggGame.BggId.HasValue)
            {
                var existingByBgg = await _gameRepository.GetGameByBggIdAsync(bggGame.BggId.Value, cancellationToken);
                if (existingByBgg != null)
                {
                    _logger.LogInformation("✅ Jogo com BGG ID {BggId} já existe.", bggGame.BggId);
                    return _mapper.Map<GameDto>(existingByBgg);
                }
            }

            // Cria novo jogo
            var newGame = new Game(bggGame.Name, bggGame.Description, bggGame.ImageUrl, bggGame.SupportsSoloMode);
            newGame.SetBggId(bggGame.BggId);
            newGame.SetBggRanking(bggGame.BggRanking);
            newGame.SetAverageRating(bggGame.AverageRating);
            newGame.UpdateBggStats(bggGame.Description, bggGame.ImageUrl, bggGame.BggRanking, bggGame.AverageRating, bggGame.YearPublished);

            // Se for expansão, tenta buscar jogo base
            if (bggGame.IsExpansion && bggGame.BaseGameBggId.HasValue)
            {
                var baseGame = await _gameRepository.GetGameByBggIdAsync(bggGame.BaseGameBggId.Value, cancellationToken);

                if (baseGame == null)
                {
                    _logger.LogInformation("📥 Importando jogo base (BGG ID: {BaseId})", bggGame.BaseGameBggId);

                    var baseFromBgg = await _bggService.GetGameByIdAsync(bggGame.BaseGameBggId.Value.ToString(), cancellationToken);
                    if (baseFromBgg != null)
                    {
                        baseGame = new Game(baseFromBgg.Name, baseFromBgg.Description, baseFromBgg.ImageUrl, baseFromBgg.SupportsSoloMode);
                        baseGame.SetBggId(baseFromBgg.BggId);
                        baseGame.SetBggRanking(baseFromBgg.BggRanking);
                        baseGame.SetAverageRating(baseFromBgg.AverageRating);
                        baseGame.UpdateBggStats(baseFromBgg.Description, baseFromBgg.ImageUrl, baseFromBgg.BggRanking, baseFromBgg.AverageRating, baseFromBgg.YearPublished);

                        await _gameRepository.AddAsync(baseGame, cancellationToken);
                        await _gameRepository.CommitAsync(cancellationToken);
                    }
                }

                if (baseGame != null)
                    newGame.SetBaseGame(baseGame);
                else
                    newGame.SetBaseGameBggId(bggGame.BaseGameBggId);
            }

            // Guarda novo jogo
            await _gameRepository.AddAsync(newGame, cancellationToken);
            await _gameRepository.CommitAsync(cancellationToken);

            // Tenta associar expansões
            if (!bggGame.IsExpansion && bggGame.BggId.HasValue)
                await TryLinkExpansionsAsync(newGame, cancellationToken);

            _logger.LogInformation("✅ Jogo '{Name}' importado com sucesso.", bggGame.Name);
            return _mapper.Map<GameDto>(newGame);
        }

        /// <summary>
        /// Tenta associar expansões órfãs a um jogo base recentemente importado.
        /// </summary>
        private async Task TryLinkExpansionsAsync(Game baseGame, CancellationToken cancellationToken)
        {
            if (!baseGame.BGGId.HasValue) return;

            var expansions = await _gameRepository.GetExpansionsWithBaseGameBggIdAsync(baseGame.BGGId.Value, cancellationToken);
            foreach (var expansion in expansions)
            {
                if (expansion.BaseGameId != baseGame.Id)
                {
                    expansion.SetBaseGame(baseGame);
                    await _gameRepository.UpdateAsync(expansion, cancellationToken);
                }
            }
            await _gameRepository.CommitAsync(cancellationToken);
        }


        public async Task<GameDto?> ImportByBggIdAsync(int bggId, CancellationToken cancellationToken = default)
        {
            // Verifica se o jogo com esse BGG ID já existe na base de dados
            var existing = await _gameRepository.GetGameByBggIdAsync(bggId, cancellationToken);
            if (existing != null)
            {
                // Se existir, retorna-o imediatamente (evita importações duplicadas)
                _logger.LogInformation("✅ Jogo já existe localmente (BGG ID: {BggId})", bggId);
                return _mapper.Map<GameDto>(existing);
            }

            // Faz pedido à API do BGG para obter os dados do jogo por ID
            var bgg = await _bggService.GetGameByIdAsync(bggId.ToString(), cancellationToken);
            if (bgg == null)
            {
                // Se o BGG não devolver nada, termina aqui
                _logger.LogWarning("❌ Jogo com BGG ID {BggId} não encontrado no BGG.", bggId);
                return null;
            }

            // Cria uma nova entidade Game com os dados recebidos do BGG
            var game = new Game(bgg.Name, bgg.Description, bgg.ImageUrl, bgg.SupportsSoloMode);
            game.SetBggId(bgg.BggId);
            game.SetAverageRating(bgg.AverageRating);
            game.SetBggRanking(bgg.BggRanking);
            game.UpdateBggStats(bgg.Description, bgg.ImageUrl, bgg.BggRanking, bgg.AverageRating, bgg.YearPublished);

            // Se o jogo importado for uma expansão...
            if (bgg.IsExpansion && bgg.BaseGameBggId.HasValue)
            {
                // Tenta obter o jogo base pela base de dados local
                var baseGame = await _gameRepository.GetGameByBggIdAsync(bgg.BaseGameBggId.Value, cancellationToken);
                if (baseGame == null)
                {
                    // Se não existir localmente, tenta buscar o jogo base ao BGG
                    var baseBgg = await _bggService.GetGameByIdAsync(bgg.BaseGameBggId.Value.ToString(), cancellationToken);
                    if (baseBgg != null)
                    {
                        // Cria a entidade do jogo base com os dados do BGG
                        baseGame = new Game(baseBgg.Name, baseBgg.Description, baseBgg.ImageUrl, baseBgg.SupportsSoloMode);
                        baseGame.SetBggId(baseBgg.BggId);
                        baseGame.SetAverageRating(baseBgg.AverageRating);
                        baseGame.SetBggRanking(baseBgg.BggRanking);
                        baseGame.UpdateBggStats(baseBgg.Description, baseBgg.ImageUrl, baseBgg.BggRanking, baseBgg.AverageRating, baseBgg.YearPublished);

                        // Adiciona o jogo base à base de dados (mas ainda sem commit)
                        await _gameRepository.AddAsync(baseGame, cancellationToken);
                    }
                }

                // Liga o jogo à sua base local se disponível, ou guarda só o BGG ID
                if (baseGame != null)
                    game.SetBaseGame(baseGame);
                else
                    game.SetBaseGameBggId(bgg.BaseGameBggId);
            }

            // Adiciona o novo jogo à base de dados
            await _gameRepository.AddAsync(game, cancellationToken);

            // Confirma todas as alterações (incluindo jogo base, se houver)
            await _gameRepository.CommitAsync(cancellationToken);

            // Tenta associar expansões órfãs a este jogo base (se for aplicável)
            await TryLinkExpansionsAsync(game, cancellationToken);

            // Retorna o jogo importado, mapeado para DTO
            return _mapper.Map<GameDto>(game);
        }





        /// <summary>
        /// Pesquisa jogos base localmente e complementa com dados do BGG se necessário.
        /// </summary>
        public async Task<List<GameDto>> SearchBaseGamesWithFallbackAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default)
        {
            var localBaseGames = await _gameRepository.SearchBaseGamesByNameAsync(query, offset, limit, cancellationToken);

            if (localBaseGames.Count >= limit)
                return _mapper.Map<List<GameDto>>(localBaseGames);

            var bggSuggestions = await _bggService.SearchGamesAsync(query, cancellationToken);

            var filtered = bggSuggestions
                .Where(g => !g.IsExpansion && g.BggId.HasValue)
                .Where(bgg => !localBaseGames.Any(local => local.BGGId == bgg.BggId))
                .Take(limit - localBaseGames.Count)
                .ToList();

            var importedGames = new List<Game>();
            foreach (var bgg in filtered)
            {
                var game = new Game(bgg.Name, bgg.Description, bgg.ImageUrl, bgg.SupportsSoloMode);
                game.SetBggId(bgg.BggId);
                game.SetAverageRating(bgg.AverageRating);
                game.SetBggRanking(bgg.BggRanking);
                game.UpdateBggStats(bgg.Description, bgg.ImageUrl, bgg.BggRanking, bgg.AverageRating, bgg.YearPublished);

                await _gameRepository.AddAsync(game, cancellationToken);
                importedGames.Add(game);
            }

            await _gameRepository.CommitAsync(cancellationToken);

            return _mapper.Map<List<GameDto>>(localBaseGames.Concat(importedGames).ToList());
        }





        public async Task<IReadOnlyList<GameDto>> GetPendingApprovalAsync(CancellationToken cancellationToken = default)
        {
            var games = await _gameRepository.GetPendingApprovalAsync(cancellationToken);
            return _mapper.Map<IReadOnlyList<GameDto>>(games);
        }





        public async Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            (await _gameRepository.GetByIdAsync(id, cancellationToken)) != null;




        public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
            await _gameRepository.ExistsByNameAsync(name.Trim(), cancellationToken);




        public async Task<Guid> AddAsync(GameDto gameDto, CancellationToken cancellationToken = default)
        {
            if (await _gameRepository.ExistsByNameAsync(gameDto.Name, cancellationToken))
                throw new ArgumentException("Já existe um jogo com este nome.");

            var newGame = new Game(gameDto.Name, gameDto.Description, gameDto.ImageUrl, gameDto.SupportsSoloMode);

            if (gameDto.BggId.HasValue)
                newGame.SetBggId(gameDto.BggId);

            await _gameRepository.AddAsync(newGame, cancellationToken);
            await _gameRepository.CommitAsync(cancellationToken);

            return newGame.Id;
        }




        public async Task<int> UpdateAsync(GameDto gameDto, CancellationToken cancellationToken = default)
        {
            var existingGame = await _gameRepository.GetByIdAsync(gameDto.Id, cancellationToken)
                ?? throw new KeyNotFoundException("Jogo não encontrado.");

            existingGame.UpdateDetails(gameDto.Name, gameDto.Description, gameDto.ImageUrl, gameDto.SupportsSoloMode);
            await _gameRepository.UpdateAsync(existingGame, cancellationToken);
            return await _gameRepository.CommitAsync(cancellationToken);
        }




        public async Task<int> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var game = await _gameRepository.GetByIdAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException("Jogo não encontrado.");

            await _gameRepository.DeleteAsync(game, cancellationToken);
            return await _gameRepository.CommitAsync(cancellationToken);
        }




        public async Task<int> ApproveGameAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            var game = await _gameRepository.GetByIdAsync(gameId, cancellationToken)
                ?? throw new KeyNotFoundException("Jogo não encontrado.");

            game.ApproveGame();
            await _gameRepository.UpdateAsync(game, cancellationToken);
            return await _gameRepository.CommitAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<GameDto>> GetRecentlyPlayedAsync(int limit, CancellationToken cancellationToken = default)
        {
            var games = await _gameRepository.GetRecentlyPlayedAsync(limit, cancellationToken);
            return _mapper.Map<IReadOnlyList<GameDto>>(games);
        }

        public async Task<IReadOnlyList<GameDto>> GetMostSearchedAsync(int limit, CancellationToken cancellationToken = default)
        {
            var games = await _gameRepository.GetMostSearchedAsync(limit, cancellationToken);
            return _mapper.Map<IReadOnlyList<GameDto>>(games);
        }

        /// <summary>
        /// Atualiza os dados de um jogo com base nos dados mais recentes do BGG.
        /// </summary>
        public async Task<bool> UpdateFromBggAsync(GameDto game, CancellationToken cancellationToken = default)
        {
            if (!game.BggId.HasValue)
            {
                _logger.LogWarning("⚠️ Jogo '{GameId}' sem BGG ID.", game.Id);
                return false;
            }

            var bggUpdated = await _bggService.GetGameByIdAsync(game.BggId.Value.ToString(), cancellationToken);
            if (bggUpdated == null)
            {
                _logger.LogWarning("❌ Falha ao obter dados do BGG para jogo '{GameId}'.", game.Id);
                return false;
            }

            var existingGame = await _gameRepository.GetByIdAsync(game.Id, cancellationToken);
            if (existingGame == null)
            {
                _logger.LogWarning("❌ Jogo local com ID '{GameId}' não encontrado.", game.Id);
                return false;
            }

            existingGame.UpdateDetails(bggUpdated.Name, bggUpdated.Description, bggUpdated.ImageUrl, bggUpdated.SupportsSoloMode);
            existingGame.SetBggRanking(bggUpdated.BggRanking);
            existingGame.SetAverageRating(bggUpdated.AverageRating);

            await _gameRepository.UpdateAsync(existingGame, cancellationToken);
            await _gameRepository.CommitAsync(cancellationToken);

            _logger.LogInformation("✅ Jogo '{Name}' sincronizado com o BGG.", existingGame.Name);
            return true;
        }
    }
}
