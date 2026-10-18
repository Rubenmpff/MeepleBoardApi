using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.Implementations
{
    public class MatchPlayerService : IMatchPlayerService
    {
        private readonly IMatchPlayerRepository _matchPlayerRepository;
        private readonly IUserRepository _userRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IGameRepository? _gameRepository;
        private readonly IMapper _mapper;
        private readonly ILogger<MatchPlayerService> _logger;
        private readonly IFriendshipRepository? _friendships;
        private readonly ICommunityRepository? _community;
        private readonly IGameSessionPlayerRepository? _sessionPlayers;

        public MatchPlayerService(
            IMatchPlayerRepository matchPlayerRepository,
            IUserRepository userRepository,
            IMatchRepository matchRepository,
            IMapper mapper,
            ILogger<MatchPlayerService> logger,
            IGameRepository? gameRepository = null,
            IFriendshipRepository? friendships = null,
            ICommunityRepository? community = null,
            IGameSessionPlayerRepository? sessionPlayers = null)
        {
            _friendships = friendships;
            _community = community;
            _sessionPlayers = sessionPlayers;
            _gameRepository = gameRepository;
            _matchPlayerRepository = matchPlayerRepository;
            _userRepository = userRepository;
            _matchRepository = matchRepository;
            _mapper = mapper;
            _logger = logger;
        }

        // 🔹 Obtém todas as partidas de um usuário
        public async Task<IEnumerable<MatchPlayerDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await ValidateUserExistsAsync(userId, cancellationToken);

            var players = await _matchPlayerRepository.GetByUserIdAsync(userId, includeMatch: true, cancellationToken);
            return _mapper.Map<IEnumerable<MatchPlayerDto>>(players);
        }

        // 🔹 Obtém o total de partidas jogadas por um usuário
        public async Task<int> GetTotalMatchesByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await ValidateUserExistsAsync(userId, cancellationToken);
            return await _matchPlayerRepository.GetTotalMatchesByUserAsync(userId, cancellationToken);
        }

        // 🔹 Obtém o total de vitórias de um usuário
        public async Task<int> GetTotalWinsByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await ValidateUserExistsAsync(userId, cancellationToken);
            return await _matchPlayerRepository.GetTotalWinsByUserAsync(userId, cancellationToken);
        }

        // 🔹 Obtém a taxa de vitórias do usuário
        public async Task<double?> GetWinRateByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            await ValidateUserExistsAsync(userId, cancellationToken);
            return await _matchPlayerRepository.GetWinRateByUserAsync(userId, cancellationToken);
        }

        // 🔹 Obtém o total de partidas jogadas em um período específico
        public async Task<int> GetTotalMatchesByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            await ValidateUserExistsAsync(userId, cancellationToken);
            ValidateDateRange(startDate, endDate);
            return await _matchPlayerRepository.GetTotalMatchesByUserInPeriodAsync(userId, startDate, endDate, cancellationToken);
        }

        // 🔹 Obtém o total de vitórias em um período específico
        public async Task<int> GetTotalWinsByUserInPeriodAsync(Guid userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            await ValidateUserExistsAsync(userId, cancellationToken);
            ValidateDateRange(startDate, endDate);
            return await _matchPlayerRepository.GetTotalWinsByUserInPeriodAsync(userId, startDate, endDate, cancellationToken);
        }

        // 🔹 Adiciona um jogador a uma partida
        public async Task AddPlayerToMatchAsync(Guid matchId, Guid playerId, Guid actorId, CancellationToken cancellationToken = default)
        {
            // Verifica se a partida existe
            var match = await _matchRepository.GetByIdAsync(matchId, cancellationToken);
            if (match == null)
                throw new KeyNotFoundException("Partida não encontrada.");
            EnsureCreator(match, actorId);

            // Verifica se o usuário existe
            await ValidateUserExistsAsync(playerId, cancellationToken);

            // Verifica se o jogador já está na partida
            bool alreadyExists = await _matchPlayerRepository.ExistsAsync(playerId, matchId, cancellationToken);
            if (alreadyExists)
                throw new InvalidOperationException("O jogador já está registrado nesta partida.");

            // The same company rules as registering (MatchCompany): no block with anyone already playing; in a session an
            // accepted participant, otherwise an accepted friend of whoever registered the match.
            if (_community == null || _friendships == null || _sessionPlayers == null)
                throw new InvalidOperationException("Não é possível acrescentar jogadores agora.");
            await MatchCompany.RequireNoBlockAsync(_community, match.MatchPlayers.Select(p => p.UserId).Append(playerId), cancellationToken);
            if (match.GameSessionId is { } sessionId)
            {
                var link = await _sessionPlayers.GetBySessionAndUserAsync(sessionId, playerId);
                if (link?.Status != MeepleBoard.Domain.Enums.GameSessionInviteStatus.Accepted)
                    throw new InvalidOperationException("Todos os jogadores têm de pertencer à sessão e ter aceite o convite.");
            }
            else if (!await _friendships.ExistsAcceptedAsync(actorId, playerId, cancellationToken))
                throw new InvalidOperationException(MatchCompany.NotFriends);

            // Adiciona o jogador à partida
            var matchPlayer = new MatchPlayer(matchId, playerId);
            await _matchPlayerRepository.AddAsync(matchPlayer, cancellationToken);
            await _matchPlayerRepository.CommitAsync(cancellationToken);
            // A player who comes back makes an existing diary rating eligible again.
            if (_gameRepository != null) await _gameRepository.RecomputeMeepleBoardRatingAsync(match.GameId, cancellationToken);
        }

        // 🔹 Remove um jogador de uma partida
        public async Task RemovePlayerFromMatchAsync(Guid matchId, Guid playerId, Guid actorId, CancellationToken cancellationToken = default)
        {
            // Verifica se a partida existe
            var match = await _matchRepository.GetByIdAsync(matchId, cancellationToken);
            if (match == null)
                throw new KeyNotFoundException("Partida não encontrada.");
            EnsureCreator(match, actorId);

            // Verifica se o jogador está na partida
            var matchPlayer = await _matchPlayerRepository.GetByMatchAndPlayerAsync(matchId, playerId, cancellationToken);
            if (matchPlayer == null)
                throw new KeyNotFoundException("Jogador não encontrado na partida.");

            // Remove o jogador da partida
            await _matchPlayerRepository.DeleteAsync(matchPlayer, cancellationToken);
            await _matchPlayerRepository.CommitAsync(cancellationToken);
            // A removed player's diary rating stops counting (the entry itself is kept).
            if (_gameRepository != null) await _gameRepository.RecomputeMeepleBoardRatingAsync(match.GameId, cancellationToken);
        }

        // Same rule as editing or deleting a match: only its known creator; a legacy match without one is never inferred.
        private static void EnsureCreator(Match match, Guid actorId)
        {
            if (actorId == Guid.Empty || match.CreatorId != actorId)
                throw new UnauthorizedAccessException("Só quem registou a partida pode alterar os jogadores.");
        }

        // 🔹 Método auxiliar: Garante que o usuário existe antes de buscar estatísticas
        private async Task ValidateUserExistsAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("ID do usuário inválido.");

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null)
                throw new KeyNotFoundException("Usuário não encontrado.");
        }

        // 🔹 Método auxiliar: Valida intervalo de datas
        private void ValidateDateRange(DateTime startDate, DateTime endDate)
        {
            if (startDate > endDate)
                throw new ArgumentException("A data inicial não pode ser maior que a data final.");
        }
    }
}