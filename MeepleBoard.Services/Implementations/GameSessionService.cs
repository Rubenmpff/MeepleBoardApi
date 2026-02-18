using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Application.DTOs;

namespace MeepleBoard.Services.Implementations
{
    public class GameSessionService : IGameSessionService
    {
        private readonly IGameSessionRepository _sessionRepository;
        private readonly IGameSessionPlayerRepository _sessionPlayerRepository;
        private readonly IUserRepository _userRepository;
        private readonly IMapper _mapper;

        public GameSessionService(
            IGameSessionRepository sessionRepository,
            IGameSessionPlayerRepository sessionPlayerRepository,
            IUserRepository userRepository,
            IMapper mapper)
        {
            _sessionRepository = sessionRepository;
            _sessionPlayerRepository = sessionPlayerRepository;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        public async Task<IEnumerable<GameSessionDto>> GetAllAsync(bool includeRelations = false)
        {
            // Repo já devolve lista "leve" (sem includes pesados)
            var sessions = await _sessionRepository.GetListAsync();
            return _mapper.Map<IEnumerable<GameSessionDto>>(sessions);
        }

        public async Task<GameSessionDto?> GetByIdAsync(Guid id, bool includeRelations = true)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("ID da sessão inválido.");

            // Para detalhe, usamos o método com detalhes
            var session = await _sessionRepository.GetByIdWithDetailsAsync(id);
            return session is null ? null : _mapper.Map<GameSessionDto>(session);
        }

        public async Task<GameSessionDto> CreateAsync(CreateGameSessionDto dto, Guid organizerId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (organizerId == Guid.Empty) throw new ArgumentException("Organizer inválido.");

            var organizer = await _userRepository.GetByIdAsync(organizerId);
            if (organizer == null) throw new KeyNotFoundException("Organizer não encontrado.");

            var session = new GameSession(dto.Name, organizerId, dto.Location);

            await _sessionRepository.AddAsync(session);

            // Organizer entra automaticamente como membro
            var organizerLink = new GameSessionPlayer(session.Id, organizerId, isOrganizer: true);
            await _sessionPlayerRepository.AddAsync(organizerLink);

            await _sessionRepository.SaveChangesAsync();

            // devolver detalhe completo
            var created = await _sessionRepository.GetByIdWithDetailsAsync(session.Id);
            return _mapper.Map<GameSessionDto>(created);
        }

        public async Task AddPlayerAsync(Guid sessionId, Guid userId, bool isOrganizer = false)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Sessão inválida.");

            if (userId == Guid.Empty)
                throw new ArgumentException("Utilizador inválido.");

            // Segurança: não permitir que o frontend escolha organizer
            // Organizer é definido apenas na criação da sessão
            isOrganizer = false;

            var session = await _sessionRepository.GetByIdForUpdateAsync(sessionId);
            if (session == null) throw new KeyNotFoundException("Sessão não encontrada.");

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new KeyNotFoundException("Utilizador não encontrado.");

            var existing = await _sessionPlayerRepository.GetBySessionAndUserAsync(sessionId, userId);
            if (existing != null)
                throw new InvalidOperationException("Este jogador já pertence à sessão.");

            var link = new GameSessionPlayer(sessionId, userId, isOrganizer: false);
            await _sessionPlayerRepository.AddAsync(link);

            await _sessionRepository.SaveChangesAsync();
        }

        public Task RemovePlayerAsync(Guid sessionId, Guid userId)
        {
            // Decisão de negócio: sessão só cresce
            throw new InvalidOperationException("Não é permitido remover jogadores da sessão. A sessão só cresce.");
        }

        public async Task CloseSessionAsync(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Sessão inválida.");

            var session = await _sessionRepository.GetByIdForUpdateAsync(sessionId);
            if (session == null) throw new KeyNotFoundException("Sessão não encontrada.");

            session.CloseSession();
            await _sessionRepository.SaveChangesAsync();
        }
    }
}
