using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Implementations
{
    public class MatchService : IMatchService
    {
        private readonly IMatchRepository _matchRepository;
        private readonly IMatchPlayerRepository _matchPlayerRepository;

        private readonly IGameSessionRepository _gameSessionRepository;
        private readonly IGameSessionPlayerRepository _gameSessionPlayerRepository;

        private readonly IUserRepository _userRepository;
        private readonly IGameRepository _gameRepository;
        private readonly IBGGService _bggService;
        private readonly IMapper _mapper;

        public MatchService(
            IMatchRepository matchRepository,
            IMatchPlayerRepository matchPlayerRepository,
            IGameSessionRepository gameSessionRepository,
            IGameSessionPlayerRepository gameSessionPlayerRepository,
            IUserRepository userRepository,
            IGameRepository gameRepository,
            IBGGService bggService,
            IMapper mapper)
        {
            _matchRepository = matchRepository;
            _matchPlayerRepository = matchPlayerRepository;
            _gameSessionRepository = gameSessionRepository;
            _gameSessionPlayerRepository = gameSessionPlayerRepository;
            _userRepository = userRepository;
            _gameRepository = gameRepository;
            _bggService = bggService;
            _mapper = mapper;
        }

        public async Task<IEnumerable<MatchDto>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            var matches = await _matchRepository.GetAllAsync(pageIndex, pageSize, cancellationToken);
            return _mapper.Map<IEnumerable<MatchDto>>(matches);
        }

        public async Task<IEnumerable<MatchDto>> GetRecentMatchesAsync(int count, CancellationToken cancellationToken = default)
        {
            var matches = await _matchRepository.GetRecentMatchesSinceAsync(count, cancellationToken: cancellationToken);
            return _mapper.Map<IEnumerable<MatchDto>>(matches);
        }

        public async Task<LastMatchDto?> GetLastMatchForUserAsync(Guid userId)
        {
            var matches = await _matchRepository.GetByUserIdAsync(userId);

            var lastMatch = matches
                .OrderByDescending(m => m.MatchDate)
                .FirstOrDefault();

            if (lastMatch == null)
                return null;

            var winner = lastMatch.MatchPlayers.FirstOrDefault(p => p.IsWinner);

            return new LastMatchDto
            {
                Name = lastMatch.Game?.Name ?? "Desconhecido",
                Date = lastMatch.MatchDate.ToString("yyyy-MM-dd"),
                Winner = winner?.User?.UserName ?? "Desconhecido"
            };
        }

        public async Task<MatchDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var match = await _matchRepository.GetByIdAsync(id, cancellationToken);
            return match is null ? null : _mapper.Map<MatchDto>(match);
        }

        public async Task<IEnumerable<MatchDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await ValidateUserExistsAsync(userId, cancellationToken);
            var matches = await _matchRepository.GetByUserIdAsync(userId, cancellationToken);
            return _mapper.Map<IEnumerable<MatchDto>>(matches);
        }

        public async Task<IEnumerable<MatchDto>> GetByGameIdAsync(Guid gameId, CancellationToken cancellationToken = default)
        {
            await ValidateGameExistsAsync(gameId, cancellationToken);
            var matches = await _matchRepository.GetByGameIdAsync(gameId, cancellationToken);
            return _mapper.Map<IEnumerable<MatchDto>>(matches);
        }

        /// <summary>
        /// ✅ Criação com regras (quick vs session) + criação de MatchPlayers.
        /// </summary>
        public async Task<MatchDto> CreateAsync(CreateMatchDto dto, Guid authenticatedUserId, CancellationToken cancellationToken = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            // 1) Validar/obter jogo
            var game = await ValidateOrFetchGameAsync(dto.GameId, dto.GameName, cancellationToken);

            // 2) Normalizar players
            var playerIds = (dto.PlayerIds ?? new List<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            if (playerIds.Count == 0)
                throw new ArgumentException("A partida deve ter pelo menos um jogador.");

            // 3) Regras quick vs session
            if (dto.GameSessionId is null)
            {
                // Quick match: sempre incluir user autenticado
                if (!playerIds.Contains(authenticatedUserId))
                    playerIds.Add(authenticatedUserId);
            }
            else
            {
                var sessionId = dto.GameSessionId.Value;

                // sessão existe? (usa método real do teu repo)
                var session = await _gameSessionRepository.GetByIdWithDetailsAsync(sessionId, cancellationToken);
                if (session == null)
                    throw new KeyNotFoundException("Sessão não encontrada.");

                // auth user pertence à sessão? (método real)
                var authLink = await _gameSessionPlayerRepository.GetBySessionAndUserAsync(sessionId, authenticatedUserId);
                if (authLink == null)
                    throw new UnauthorizedAccessException("Você não é membro desta sessão.");

                // players pertencem à sessão? (método real)
                var sessionPlayers = await _gameSessionPlayerRepository.GetBySessionIdAsync(sessionId);
                var memberSet = sessionPlayers.Select(p => p.UserId).ToHashSet();

                if (playerIds.Any(pid => !memberSet.Contains(pid)))
                    throw new InvalidOperationException("Todos os jogadores do match devem pertencer à sessão.");
            }

            // 4) winner tem de estar nos players (se existir)
            if (dto.WinnerId.HasValue && dto.WinnerId.Value != Guid.Empty)
            {
                if (!playerIds.Contains(dto.WinnerId.Value))
                    throw new ArgumentException("O vencedor tem de estar incluído nos jogadores da partida.");
            }

            // 5) Criar Match (o teu Match suporta sessionId no construtor ✅)
            var match = new Match(game.Id, dto.MatchDate, dto.GameSessionId);

            match.SetLocation(dto.Location);
            match.SetSoloGame(dto.IsSoloGame);
            match.SetDuration(dto.DurationInMinutes);
            match.SetWinner(dto.WinnerId);

            // Tens ScoreSummary no domínio, mas não tens setter dedicado:
            // usa UpdateMatchDetails para guardar ScoreSummary sem inventar método novo.
            match.UpdateMatchDetails(dto.Location, dto.ScoreSummary, dto.DurationInMinutes);
            match.SetSoloGame(dto.IsSoloGame);

            if (dto.WinnerId.HasValue)
                match.SetWinner(dto.WinnerId);


            await _matchRepository.AddAsync(match, cancellationToken);

            // 6) Criar MatchPlayers
            foreach (var userId in playerIds)
            {
                var mp = new MatchPlayer(match.Id, userId);

                if (dto.WinnerId.HasValue && dto.WinnerId.Value == userId)
                    mp.SetWinner(true);

                await _matchPlayerRepository.AddAsync(mp, cancellationToken);
            }

            // 7) Um commit (se os repos partilham DbContext, como tens)
            await _matchRepository.SaveChangesAsync(cancellationToken);

            return _mapper.Map<MatchDto>(match);
        }

        // Legacy - mantido para não rebentar outras partes já existentes
        public async Task<MatchDto> AddAsync(MatchDto matchDto, CancellationToken cancellationToken = default)
        {
            if (matchDto == null)
                throw new ArgumentNullException(nameof(matchDto), "Os dados da partida não podem ser nulos.");

            var game = await ValidateOrFetchGameAsync(matchDto.GameId, matchDto.GameName, cancellationToken);

            var match = new Match(game.Id, matchDto.MatchDate);
            match.UpdateMatchDetails(matchDto.Location, matchDto.ScoreSummary, matchDto.DurationInMinutes);
            match.SetSoloGame(matchDto.IsSoloGame);
            match.SetWinner(matchDto.WinnerId);

            await _matchRepository.AddAsync(match, cancellationToken);
            await _matchRepository.SaveChangesAsync(cancellationToken);

            return _mapper.Map<MatchDto>(match);
        }

        public async Task<int> UpdateAsync(MatchDto matchDto, CancellationToken cancellationToken = default)
        {
            var match = await _matchRepository.GetByIdAsync(matchDto.Id, cancellationToken);
            if (match == null)
                throw new KeyNotFoundException("Partida não encontrada.");

            match.SetGameId(matchDto.GameId);
            match.SetMatchDate(matchDto.MatchDate);
            match.UpdateMatchDetails(matchDto.Location, matchDto.ScoreSummary, matchDto.DurationInMinutes);
            match.SetSoloGame(matchDto.IsSoloGame);
            match.SetWinner(matchDto.WinnerId);

            await _matchRepository.UpdateAsync(match, cancellationToken);
            return await _matchRepository.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var match = await _matchRepository.GetByIdAsync(id, cancellationToken);
            if (match == null)
                return false;

            await _matchRepository.DeleteAsync(id, cancellationToken: cancellationToken);
            await _matchRepository.SaveChangesAsync(cancellationToken);
            return true;
        }

        private async Task ValidateGameExistsAsync(Guid gameId, CancellationToken cancellationToken)
        {
            if (gameId == Guid.Empty)
                throw new ArgumentException("ID do jogo inválido.");

            var game = await _gameRepository.GetByIdAsync(gameId, cancellationToken);
            if (game == null)
                throw new KeyNotFoundException("Jogo não encontrado.");
        }

        private async Task ValidateUserExistsAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("ID do usuário inválido.");

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
                throw new KeyNotFoundException("Usuário não encontrado.");
        }

        private async Task<Game> ValidateOrFetchGameAsync(Guid gameId, string gameName, CancellationToken cancellationToken)
        {
            if (gameId != Guid.Empty)
            {
                var existingGame = await _gameRepository.GetByIdAsync(gameId, cancellationToken);
                if (existingGame != null)
                    return existingGame;
            }

            if (!string.IsNullOrWhiteSpace(gameName))
            {
                var existingGame = await _gameRepository.GetByNameAsync(gameName, cancellationToken);
                if (existingGame != null)
                    return existingGame;

                var gameFromBGG = await _bggService.GetGameByNameAsync(gameName, cancellationToken);
                if (gameFromBGG != null)
                {
                    var newGame = new Game(gameFromBGG.Name, gameFromBGG.Description, gameFromBGG.ImageUrl);
                    await _gameRepository.AddAsync(newGame, cancellationToken);
                    await _gameRepository.CommitAsync(cancellationToken);
                    return newGame;
                }
            }

            throw new KeyNotFoundException("Jogo não encontrado.");
        }
    }
}
