using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;

namespace MeepleBoard.Services.Implementations
{
    public class UserGameLibraryService : IUserGameLibraryService
    {
        private readonly IUserGameLibraryRepository _userGameLibraryRepository;
        private readonly IGameRepository _gameRepository;
        private readonly IUserRepository _userRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IFriendshipRepository _friendshipRepository;
        private readonly IBGGService _bggService;
        private readonly IGameService _gameService;
        private readonly IMapper _mapper;

        public UserGameLibraryService(
            IUserGameLibraryRepository userGameLibraryRepository,
            IGameRepository gameRepository,
            IUserRepository userRepository,
            IMatchRepository matchRepository,
            IFriendshipRepository friendshipRepository,
            IBGGService bggService,
            IGameService gameService,
            IMapper mapper)
        {
            _userGameLibraryRepository = userGameLibraryRepository;
            _gameRepository = gameRepository;
            _userRepository = userRepository;
            _matchRepository = matchRepository;
            _friendshipRepository = friendshipRepository;
            _bggService = bggService;
            _gameService = gameService;
            _mapper = mapper;
        }

        public async Task<IEnumerable<UserGameLibraryDto>> GetUserLibraryAsync(
            Guid requesterId, Guid targetUserId, CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(targetUserId, cancellationToken);

            var isOwner = requesterId == targetUserId;

            if (!isOwner)
            {
                var targetUser = await _userRepository.GetByIdAsync(targetUserId, cancellationToken);
                var canView = targetUser!.LibraryPrivacy switch
                {
                    LibraryPrivacy.Public => true,
                    LibraryPrivacy.FriendsOnly => await _friendshipRepository.ExistsAcceptedAsync(requesterId, targetUserId, cancellationToken),
                    LibraryPrivacy.Private => false,
                    _ => false
                };

                if (!canView)
                    throw new UnauthorizedAccessException("Esta coleção é privada.");
            }

            var library = await _userGameLibraryRepository.GetByUserIdAsync(targetUserId, cancellationToken);
            var dtos = _mapper.Map<List<UserGameLibraryDto>>(library);

            var playStats = await _matchRepository.GetPlayCountsByGameForUserAsync(targetUserId, cancellationToken);
            foreach (var dto in dtos)
            {
                if (playStats.TryGetValue(dto.GameId, out var stats))
                {
                    dto.TotalTimesPlayed = stats.Count;
                    dto.LastPlayedAt = stats.LastPlayed;
                }
                else
                {
                    dto.TotalTimesPlayed = 0;
                }

                if (!isOwner)
                    dto.PricePaid = null;
            }

            return dtos;
        }

        public async Task<IEnumerable<PlayedGameDto>> GetPlayedGamesAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(userId, cancellationToken);

            var playStats = await _matchRepository.GetPlayCountsByGameForUserAsync(userId, cancellationToken);
            if (playStats.Count == 0) return Enumerable.Empty<PlayedGameDto>();

            var games = await _gameRepository.GetByIdsAsync(playStats.Keys, cancellationToken);
            var gameMap = games.ToDictionary(g => g.Id);

            var library = await _userGameLibraryRepository.GetByUserIdAsync(userId, cancellationToken);
            var libraryMap = library.ToDictionary(e => e.GameId);

            var result = new List<PlayedGameDto>();
            foreach (var (gameId, stats) in playStats)
            {
                if (!gameMap.TryGetValue(gameId, out var game)) continue;

                libraryMap.TryGetValue(gameId, out var libEntry);

                result.Add(new PlayedGameDto
                {
                    GameId = game.Id,
                    GameName = game.Name,
                    GameImageUrl = game.ImageUrl,
                    AverageRating = game.AverageRating,
                    MinPlayers = game.MinPlayers,
                    MaxPlayers = game.MaxPlayers,
                    IsExpansion = game.IsExpansion,
                    IsCooperative = game.IsCooperative,
                    SupportsSoloMode = game.SupportsSoloMode,
                    TimesPlayed = stats.Count,
                    LastPlayedAt = stats.LastPlayed,
                    InLibrary = libEntry != null,
                    Status = libEntry?.Status,
                    PricePaid = libEntry?.PricePaid,
                });
            }

            return result.OrderByDescending(g => g.TimesPlayed);
        }

        public async Task AddGameToLibraryAsync(Guid userId, Guid gameId, string gameName, GameLibraryStatus status, decimal? pricePaid, CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(userId, cancellationToken);
            var game = await ValidateOrFetchGameAsync(gameId, gameName, cancellationToken);

            if (await _userGameLibraryRepository.ExistsAsync(userId, game.Id, cancellationToken))
                throw new InvalidOperationException("O jogo já está na sua biblioteca.");

            if (pricePaid.HasValue && pricePaid < 0)
                throw new ArgumentException("O valor pago pelo jogo não pode ser negativo.");

            var userGameLibrary = new UserGameLibrary(userId, game.Id, status, pricePaid);

            await _userGameLibraryRepository.AddAsync(userGameLibrary, cancellationToken);
            await _userGameLibraryRepository.CommitAsync(cancellationToken);
        }

        public async Task UpdateGameInLibraryAsync(
            Guid userId, Guid gameId, GameLibraryStatus status, decimal? pricePaid,
            CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(userId, cancellationToken);

            var entry = await _userGameLibraryRepository.GetByUserAndGameAsync(userId, gameId, cancellationToken);
            if (entry == null)
                throw new InvalidOperationException("O jogo não está na sua biblioteca.");

            if (pricePaid.HasValue && pricePaid < 0)
                throw new ArgumentException("O valor pago pelo jogo não pode ser negativo.");

            entry.UpdateStatus(status);
            entry.SetPricePaid(pricePaid);

            await _userGameLibraryRepository.UpdateAsync(entry, cancellationToken);
            await _userGameLibraryRepository.CommitAsync(cancellationToken);
        }

        public async Task RemoveGameFromLibraryAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(userId, cancellationToken);

            var entry = await _userGameLibraryRepository.GetByUserAndGameAsync(userId, gameId, cancellationToken);
            if (entry == null)
                throw new InvalidOperationException("O jogo não está na sua biblioteca.");

            await _userGameLibraryRepository.RemoveAsync(userId, gameId, cancellationToken);
            await _userGameLibraryRepository.CommitAsync(cancellationToken);
        }

        public async Task<decimal> GetTotalAmountSpentByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(userId, cancellationToken);
            return await _userGameLibraryRepository.GetTotalAmountSpentByUserAsync(userId, cancellationToken);
        }

        private async Task EnsureUserExistsAsync(Guid userId, CancellationToken cancellationToken)
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
                var game = await _gameRepository.GetByIdAsync(gameId, cancellationToken);
                if (game != null)
                    return game;
            }

            if (!string.IsNullOrWhiteSpace(gameName))
            {
                var game = await _gameRepository.GetByNameAsync(gameName, cancellationToken);
                if (game != null)
                    return game;
            }

            if (!string.IsNullOrWhiteSpace(gameName))
            {
                var bggGame = await _bggService.GetGameByNameAsync(gameName, cancellationToken);
                if (bggGame?.BggId != null)
                    return await _gameService.GetOrCreateMinimalGameAsync(bggGame.BggId.Value, cancellationToken);
            }

            throw new KeyNotFoundException("Jogo não encontrado.");
        }
    }
}