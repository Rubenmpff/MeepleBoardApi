using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Interfaces;

namespace MeepleBoard.Services.Implementations
{
    public class GameSessionService : IGameSessionService
    {
        private readonly IGameSessionRepository _sessionRepository;
        private readonly IGameSessionPlayerRepository _sessionPlayerRepository;

        public GameSessionService(
            IGameSessionRepository sessionRepository,
            IGameSessionPlayerRepository sessionPlayerRepository)
        {
            _sessionRepository = sessionRepository;
            _sessionPlayerRepository = sessionPlayerRepository;
        }

        public async Task<IEnumerable<GameSession>> GetAllAsync()
        {
            return await _sessionRepository.GetAllAsync();
        }

        public async Task<GameSession?> GetByIdAsync(Guid id)
        {
            return await _sessionRepository.GetByIdAsync(id);
        }

        public async Task<GameSession> CreateAsync(string name, Guid organizerId, string? location = null)
        {
            var session = new GameSession(name, organizerId, location);
            await _sessionRepository.AddAsync(session);

            var organizerPlayer = new GameSessionPlayer(session.Id, organizerId, true);
            await _sessionPlayerRepository.AddAsync(organizerPlayer);

            // 🔹 Commit explícito
            await _sessionRepository.SaveChangesAsync();
            await _sessionPlayerRepository.SaveChangesAsync();

            return session;
        }

        public async Task AddPlayerAsync(Guid sessionId, Guid userId, bool isOrganizer = false)
        {
            var existingPlayers = await _sessionPlayerRepository.GetBySessionIdAsync(sessionId);
            if (existingPlayers.Any(p => p.UserId == userId))
                throw new Exception("Jogador já está na sessão.");

            var player = new GameSessionPlayer(sessionId, userId, isOrganizer);
            await _sessionPlayerRepository.AddAsync(player);
            await _sessionPlayerRepository.SaveChangesAsync();
        }

        public async Task RemovePlayerAsync(Guid sessionId, Guid userId)
        {
            var players = await _sessionPlayerRepository.GetBySessionIdAsync(sessionId);
            var player = players.FirstOrDefault(p => p.UserId == userId);
            if (player == null)
                throw new Exception("Jogador não encontrado na sessão.");

            await _sessionPlayerRepository.RemoveAsync(player.Id);
            await _sessionPlayerRepository.SaveChangesAsync();
        }

        public async Task CloseSessionAsync(Guid sessionId)
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId);
            if (session == null)
                throw new Exception("Sessão não encontrada.");

            session.CloseSession();
            await _sessionRepository.UpdateAsync(session);
            await _sessionRepository.SaveChangesAsync();
        }
    }
}
