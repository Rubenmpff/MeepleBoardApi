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
        private readonly IUserRepository _userRepository;
        private readonly IGameRepository _gameRepository;
        private readonly IBGGService _bggService;
        private readonly IMapper _mapper;

        public MatchService(
            IMatchRepository matchRepository,
            IUserRepository userRepository,
            IGameRepository gameRepository,
            IBGGService bggService,
            IMapper mapper)
        {
            _matchRepository = matchRepository;
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

        public async Task<MatchDto> AddAsync(MatchDto matchDto, CancellationToken cancellationToken = default)
        {
            if (matchDto == null)
                throw new ArgumentNullException(nameof(matchDto), "Os dados da partida não podem ser nulos.");

            var game = await ValidateOrFetchGameAsync(matchDto.GameId, matchDto.GameName, cancellationToken);

            var match = new Match(game.Id, matchDto.MatchDate);
            match.SetLocation(matchDto.Location);
            match.SetSoloGame(matchDto.IsSoloGame);
            match.SetDuration(matchDto.DurationInMinutes);
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
            match.SetLocation(matchDto.Location);
            match.SetDuration(matchDto.DurationInMinutes);
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