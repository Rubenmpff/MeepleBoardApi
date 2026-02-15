// MeepleBoard.Services/Implementations/GameSessionService.cs
using AutoMapper;
using MeepleBoard.Application.DTOs;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Gestão de sessões de jogo: criação, fecho e gestão de jogadores.
    /// Devolve sempre DTOs para evitar ciclos de serialização.
    /// </summary>
    public class GameSessionService : IGameSessionService
    {
        private readonly IGameSessionRepository _sessionRepository;
        private readonly IGameSessionPlayerRepository _sessionPlayerRepository;
        private readonly IMapper _mapper;

        public GameSessionService(
            IGameSessionRepository sessionRepository,
            IGameSessionPlayerRepository sessionPlayerRepository,
            IMapper mapper)
        {
            _sessionRepository = sessionRepository;
            _sessionPlayerRepository = sessionPlayerRepository;
            _mapper = mapper;
        }

        public async Task<IEnumerable<GameSessionDto>> GetAllAsync(bool includeRelations = false)
        {
            var sessions = await _sessionRepository.GetAllAsync(includeRelations: includeRelations);
            return _mapper.Map<IEnumerable<GameSessionDto>>(sessions);
        }

        public async Task<GameSessionDto?> GetByIdAsync(Guid id, bool includeRelations = true)
        {
            var session = await _sessionRepository.GetByIdAsync(id, includeRelations: includeRelations);
            return session is null ? null : _mapper.Map<GameSessionDto>(session);
        }

        public async Task<GameSessionDto> CreateAsync(string name, Guid organizerId, string? location = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("O nome da sessão é obrigatório.", nameof(name));

            if (organizerId == Guid.Empty)
                throw new ArgumentException("OrganizerId inválido.", nameof(organizerId));

            // 1) Criar sessão e organizador
            var session = new GameSession(name, organizerId, location);
            var organizerPlayer = new GameSessionPlayer(session.Id, organizerId, isOrganizer: true);

            await _sessionRepository.AddAsync(session);
            await _sessionPlayerRepository.AddAsync(organizerPlayer);

            // 2) Commit único (ambos os repositórios devem partilhar o mesmo DbContext)
            await _sessionRepository.SaveChangesAsync();

            // 3) Recarregar com relações para devolver DTO completo
            var created = await _sessionRepository.GetByIdAsync(session.Id, includeRelations: true);
            return _mapper.Map<GameSessionDto>(created!);
        }

        public async Task AddPlayerAsync(Guid sessionId, Guid userId, bool isOrganizer = false)
        {
            if (sessionId == Guid.Empty || userId == Guid.Empty)
                throw new ArgumentException("IDs inválidos.");

            var existing = await _sessionPlayerRepository.GetBySessionAndUserAsync(sessionId, userId);
            if (existing != null)
                throw new InvalidOperationException("Jogador já está na sessão.");

            var player = new GameSessionPlayer(sessionId, userId, isOrganizer);
            await _sessionPlayerRepository.AddAsync(player);
            await _sessionPlayerRepository.SaveChangesAsync();
        }

        public async Task RemovePlayerAsync(Guid sessionId, Guid userId)
        {
            if (sessionId == Guid.Empty || userId == Guid.Empty)
                throw new ArgumentException("IDs inválidos.");

            var existing = await _sessionPlayerRepository.GetBySessionAndUserAsync(sessionId, userId);
            if (existing == null)
                throw new KeyNotFoundException("Jogador não encontrado na sessão.");

            await _sessionPlayerRepository.RemoveAsync(existing.Id);
            await _sessionPlayerRepository.SaveChangesAsync();
        }

        public async Task CloseSessionAsync(Guid sessionId)
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId);
            if (session == null)
                throw new KeyNotFoundException("Sessão não encontrada.");

            session.CloseSession();
            await _sessionRepository.UpdateAsync(session);
            await _sessionRepository.SaveChangesAsync();
        }
    }
}
