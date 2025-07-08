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
        private readonly IBGGService _bggService;
        private readonly IMapper _mapper;

        public UserGameLibraryService(
            IUserGameLibraryRepository userGameLibraryRepository,
            IGameRepository gameRepository,
            IUserRepository userRepository,
            IBGGService bggService,
            IMapper mapper)
        {
            _userGameLibraryRepository = userGameLibraryRepository;
            _gameRepository = gameRepository;
            _userRepository = userRepository;
            _bggService = bggService;
            _mapper = mapper;
        }

        /// <summary>
        /// Obtém a biblioteca de jogos de um usuário.
        /// </summary>
        public async Task<IEnumerable<UserGameLibraryDto>> GetUserLibraryAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(userId, cancellationToken);
            var library = await _userGameLibraryRepository.GetByUserIdAsync(userId, cancellationToken);
            return _mapper.Map<IEnumerable<UserGameLibraryDto>>(library);
        }

        /// <summary>
        /// Adiciona um jogo à biblioteca do usuário.
        /// </summary>
        public async Task AddGameToLibraryAsync(Guid userId, Guid gameId, string gameName, GameLibraryStatus status, decimal? pricePaid, CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(userId, cancellationToken);
            var game = await ValidateOrFetchGameAsync(gameId, gameName, cancellationToken);

            // 🚀 Evita adicionar duplicatas na biblioteca
            if (await _userGameLibraryRepository.ExistsAsync(userId, game.Id, cancellationToken))
                throw new InvalidOperationException("O jogo já está na sua biblioteca.");

            if (pricePaid.HasValue && pricePaid < 0)
                throw new ArgumentException("O valor pago pelo jogo não pode ser negativo.");

            var userGameLibrary = new UserGameLibrary(userId, game.Id, status, pricePaid);

            await _userGameLibraryRepository.AddAsync(userGameLibrary, cancellationToken);
            await _userGameLibraryRepository.CommitAsync(cancellationToken);
        }

        /// <summary>
        /// Remove um jogo da biblioteca do usuário.
        /// </summary>
        public async Task RemoveGameFromLibraryAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(userId, cancellationToken);

            var entry = await _userGameLibraryRepository.GetByUserAndGameAsync(userId, gameId, cancellationToken);
            if (entry == null)
                throw new InvalidOperationException("O jogo não está na sua biblioteca.");

            await _userGameLibraryRepository.RemoveAsync(userId, gameId, cancellationToken);
            await _userGameLibraryRepository.CommitAsync(cancellationToken);
        }

        /// <summary>
        /// Obtém o total gasto pelo usuário em sua biblioteca de jogos.
        /// </summary>
        public async Task<decimal> GetTotalAmountSpentByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await EnsureUserExistsAsync(userId, cancellationToken);
            return await _userGameLibraryRepository.GetTotalAmountSpentByUserAsync(userId, cancellationToken);
        }

        // 🔹 Métodos auxiliares:

        /// <summary>
        /// Verifica se o usuário existe.
        /// </summary>
        private async Task EnsureUserExistsAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("ID do usuário inválido.");

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
                throw new KeyNotFoundException("Usuário não encontrado.");
        }

        /// <summary>
        /// Valida se o jogo existe na base de dados ou busca no BGG.
        /// </summary>
        private async Task<Game> ValidateOrFetchGameAsync(Guid gameId, string gameName, CancellationToken cancellationToken)
        {
            Game? game = null;

            // 🚀 Primeiro verifica no banco de dados para evitar chamadas desnecessárias à API externa
            if (gameId != Guid.Empty)
            {
                game = await _gameRepository.GetByIdAsync(gameId, cancellationToken);
            }

            // 🚀 Se não encontrou pelo ID, tenta buscar pelo nome no banco
            if (game == null && !string.IsNullOrWhiteSpace(gameName))
            {
                game = await _gameRepository.GetByNameAsync(gameName, cancellationToken);
            }

            // 🚀 Se ainda não encontrou, busca no BGG
            if (game == null && !string.IsNullOrWhiteSpace(gameName))
            {
                var bggGame = await _bggService.GetGameByNameAsync(gameName, cancellationToken);
                if (bggGame != null)
                {
                    game = new Game(bggGame.Name, bggGame.Description, bggGame.ImageUrl);
                    game.SetBggRanking(bggGame.BggRanking);
                    game.SetAverageRating(bggGame.AverageRating);
                    game.ApproveGame(); // ✅ Define como aprovado automaticamente

                    await _gameRepository.AddAsync(game, cancellationToken);
                    await _gameRepository.CommitAsync(cancellationToken);
                }
            }

            return game ?? throw new KeyNotFoundException("Jogo não encontrado.");
        }
    }
}