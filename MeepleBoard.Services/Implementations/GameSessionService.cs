using AutoMapper;
using MeepleBoard.Application.DTOs;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Interfaces;

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

        /* ── Leitura ─────────────────────────────────────────────────────────── */

        public async Task<IEnumerable<GameSessionDto>> GetAllAsync(bool includeRelations = false)
        {
            var sessions = await _sessionRepository.GetListAsync();
            return _mapper.Map<IEnumerable<GameSessionDto>>(sessions);
        }

        public async Task<IEnumerable<GameSessionDto>> GetMineAsync(Guid userId)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Utilizador inválido.");

            var sessions = await _sessionRepository.GetListAsync();

            // Exclui sessões canceladas da lista principal
            var mine = sessions.Where(s =>
                !s.IsCancelled &&
                (s.OrganizerId == userId || s.Players.Any(p => p.UserId == userId))
            );

            return _mapper.Map<IEnumerable<GameSessionDto>>(mine);
        }

        public async Task<GameSessionDto?> GetByIdAsync(Guid id, bool includeRelations = true)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("ID da sessão inválido.");

            var session = await _sessionRepository.GetByIdWithDetailsAsync(id);
            return session is null ? null : _mapper.Map<GameSessionDto>(session);
        }

        /* ── Criação ─────────────────────────────────────────────────────────── */

        public async Task<GameSessionDto> CreateAsync(CreateGameSessionDto dto, Guid organizerId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (organizerId == Guid.Empty) throw new ArgumentException("Organizer inválido.");

            var organizer = await _userRepository.GetByIdAsync(organizerId);
            if (organizer == null) throw new KeyNotFoundException("Organizer não encontrado.");

            var scheduledUtc = dto.ScheduledStartDate?.ToUniversalTime() ?? DateTime.UtcNow;
            var deadlineUtc = dto.ResponseDeadline?.ToUniversalTime();

            var session = new GameSession(
                dto.Name,
                organizerId,
                dto.Location,
                scheduledUtc,
                deadlineUtc
            );

            await _sessionRepository.AddAsync(session);

            // Organizer → Accepted automaticamente
            var organizerLink = new GameSessionPlayer(session.Id, organizerId, isOrganizer: true);
            await _sessionPlayerRepository.AddAsync(organizerLink);

            // Convites iniciais → Pending
            if (dto.PlayerIds != null && dto.PlayerIds.Count > 0)
            {
                var unique = dto.PlayerIds
                    .Where(id => id != Guid.Empty && id != organizerId)
                    .Distinct()
                    .ToList();

                foreach (var playerId in unique)
                {
                    var user = await _userRepository.GetByIdAsync(playerId);
                    if (user == null) continue;

                    var existing = await _sessionPlayerRepository.GetBySessionAndUserAsync(session.Id, playerId);
                    if (existing != null) continue;

                    var invite = new GameSessionPlayer(session.Id, playerId, isOrganizer: false);
                    await _sessionPlayerRepository.AddAsync(invite);
                }
            }

            await _sessionRepository.SaveChangesAsync();

            var created = await _sessionRepository.GetByIdWithDetailsAsync(session.Id);
            return _mapper.Map<GameSessionDto>(created!);
        }

        /* ── Convites ────────────────────────────────────────────────────────── */

        public async Task InvitePlayerAsync(Guid sessionId, Guid organizerId, Guid targetUserId)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Sessão inválida.");
            if (organizerId == Guid.Empty) throw new ArgumentException("Organizer inválido.");
            if (targetUserId == Guid.Empty) throw new ArgumentException("Utilizador inválido.");

            var session = await _sessionRepository.GetByIdForUpdateAsync(sessionId);
            if (session == null) throw new KeyNotFoundException("Sessão não encontrada.");

            if (session.IsCancelled)
                throw new InvalidOperationException("Sessão cancelada não aceita novos convites.");

            if (session.OrganizerId != organizerId)
                throw new InvalidOperationException("Apenas o organizer pode convidar jogadores.");

            var user = await _userRepository.GetByIdAsync(targetUserId);
            if (user == null) throw new KeyNotFoundException("Utilizador não encontrado.");

            var existing = await _sessionPlayerRepository.GetBySessionAndUserAsync(sessionId, targetUserId);
            if (existing != null)
                throw new InvalidOperationException("Este utilizador já foi convidado ou já pertence à sessão.");

            var invite = new GameSessionPlayer(sessionId, targetUserId, isOrganizer: false);
            await _sessionPlayerRepository.AddAsync(invite);

            await _sessionRepository.SaveChangesAsync();
        }

        public async Task RespondInviteAsync(Guid sessionId, Guid userId, bool accept)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Sessão inválida.");
            if (userId == Guid.Empty) throw new ArgumentException("Utilizador inválido.");

            var session = await _sessionRepository.GetByIdForUpdateAsync(sessionId);
            if (session == null) throw new KeyNotFoundException("Sessão não encontrada.");

            if (session.IsCancelled)
                throw new InvalidOperationException("Esta sessão foi cancelada.");

            var link = await _sessionPlayerRepository.GetBySessionAndUserAsync(sessionId, userId);
            if (link == null) throw new KeyNotFoundException("Convite não encontrado.");

            if (link.IsOrganizer)
                throw new InvalidOperationException("Organizer não precisa de responder a convite.");

            if (accept) link.Accept();
            else link.Decline();

            await _sessionRepository.SaveChangesAsync();
        }

        /* ── Encerrar / Cancelar ─────────────────────────────────────────────── */

        public async Task CloseSessionAsync(Guid sessionId, Guid organizerId)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Sessão inválida.");
            if (organizerId == Guid.Empty) throw new ArgumentException("Organizer inválido.");

            var session = await _sessionRepository.GetByIdForUpdateAsync(sessionId);
            if (session == null) throw new KeyNotFoundException("Sessão não encontrada.");

            if (session.OrganizerId != organizerId)
                throw new InvalidOperationException("Apenas o organizer pode encerrar a sessão.");

            session.CloseSession();
            await _sessionRepository.SaveChangesAsync();
        }

        /// <summary>
        /// Cancelamento manual pelo organizer.
        /// Só possível enquanto Upcoming.
        /// A sessão é apagada da BD imediatamente.
        /// </summary>
        public async Task CancelSessionAsync(Guid sessionId, Guid organizerId)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Sessão inválida.");
            if (organizerId == Guid.Empty) throw new ArgumentException("Organizer inválido.");

            var session = await _sessionRepository.GetByIdForUpdateAsync(sessionId);
            if (session == null) throw new KeyNotFoundException("Sessão não encontrada.");

            if (session.OrganizerId != organizerId)
                throw new InvalidOperationException("Apenas o organizer pode cancelar a sessão.");

            if (session.Status == "Active")
                throw new InvalidOperationException("Não podes cancelar uma sessão que já está a decorrer. Usa encerrar.");

            if (session.Status == "Closed")
                throw new InvalidOperationException("Sessão já foi encerrada.");

            // Apaga diretamente — sem histórico
            await _sessionRepository.DeleteAsync(sessionId);
            await _sessionRepository.SaveChangesAsync();
        }
    }
}