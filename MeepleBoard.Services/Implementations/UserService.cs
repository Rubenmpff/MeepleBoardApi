using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Interfaces;
using Hangfire;
using MeepleBoard.Services.Job;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.Implementations
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IMapper _mapper;

        private readonly IPhotoStorageService? _photoStorageService;
        private readonly ILogger<UserService>? _logger;
        private readonly INotificationService? _notifications;
        private readonly IBackgroundJobClient? _backgroundJobs;
        // Waits between immediate storage attempts; a test may shorten them.
        public static TimeSpan[] PhotoRetryDelays = [TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1)];

        public UserService(IUserRepository userRepository, IMatchRepository matchRepository, IMapper mapper,
            IPhotoStorageService? photoStorageService = null, ILogger<UserService>? logger = null,
            INotificationService? notifications = null, IBackgroundJobClient? backgroundJobs = null)
        {
            _notifications = notifications;
            _backgroundJobs = backgroundJobs;
            _photoStorageService = photoStorageService;
            _logger = logger;
            _userRepository = userRepository;
            _matchRepository = matchRepository;
            _mapper = mapper;
        }

        public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty) throw new ArgumentException("ID inválido.");

            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null) return null;

            var userDto = _mapper.Map<UserDto>(user);
            await PopulateResultStatistics(userDto, cancellationToken);

            return userDto;
        }

        public async Task<UserDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("O e-mail não pode estar vazio.");

            var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
            if (user == null) return null;

            var userDto = _mapper.Map<UserDto>(user);
            await PopulateResultStatistics(userDto, cancellationToken);

            return userDto;
        }

        public async Task<IEnumerable<UserDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var users = await _userRepository.GetAllAsync(cancellationToken);
            var userDtos = _mapper.Map<List<UserDto>>(users);

            foreach (var userDto in userDtos)
            {
                await PopulateResultStatistics(userDto, cancellationToken);
            }

            return userDtos;
        }

        public async Task AddAsync(UserDto userDto, CancellationToken cancellationToken = default)
        {
            if (userDto == null) throw new ArgumentNullException(nameof(userDto));

            var user = _mapper.Map<User>(userDto);
            user.SetCreatedAt(DateTime.UtcNow);

            await _userRepository.AddAsync(user, cancellationToken);
            await _userRepository.CommitAsync(cancellationToken);
        }

        public async Task UpdateAsync(UserDto userDto, CancellationToken cancellationToken = default)
        {
            if (userDto == null) throw new ArgumentNullException(nameof(userDto));

            var user = await _userRepository.GetByIdAsync(userDto.Id, cancellationToken);
            if (user == null) throw new KeyNotFoundException("Usuário não encontrado.");

            _mapper.Map(userDto, user);
            user.SetUpdatedAt();

            await _userRepository.UpdateAsync(user, cancellationToken);
            await _userRepository.CommitAsync(cancellationToken);
        }

        public async Task DeleteAsync(Guid id, IReadOnlyDictionary<Guid, Guid>? campaignTransfers = null, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty) throw new ArgumentException("ID inválido.");

            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null || user.DeletedAt.HasValue) throw new KeyNotFoundException("Utilizador não encontrado.");

            // Anonymised, not removed: shared matches keep their players (docs/proposta-eliminacao-conta.md in the app).
            var result = await _userRepository.AnonymizeAsync(id, User.DeletedUserName(id), campaignTransfers, cancellationToken);

            // Outside the transaction, and never able to undo it: the account is deleted whatever happens below.
            if (result.CancelledSessionGuestTokens.Count > 0 && _notifications != null)
            {
                try { await _notifications.SendToManyAsync(result.CancelledSessionGuestTokens, "Sessão cancelada", "Uma sessão para a qual foste convidado foi cancelada.", new { type = "session_cancelled" }, cancellationToken); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Aviso de sessão cancelada não enviado."); }
            }
            if (_photoStorageService == null) return;
            foreach (var photoUrl in result.PhotoUrls)
                if (!await TryDeletePhotoAsync(_photoStorageService, photoUrl, cancellationToken))
                {
                    // Still failing: a background job keeps trying (Hangfire retries); the reference carries no identity.
                    try { _backgroundJobs?.Enqueue<PhotoDeletionJob>(job => job.ExecuteAsync(photoUrl)); }
                    catch (Exception ex) { _logger?.LogError(ex, "Não foi possível agendar a remoção de uma foto de uma conta eliminada."); }
                }
        }

        public Task<AccountDeletionSummary?> GetDeletionSummaryAsync(Guid id, CancellationToken cancellationToken = default)
            => _userRepository.GetDeletionSummaryAsync(id, cancellationToken);

        // Three immediate attempts; storage answering «not found» counts as done.
        private async Task<bool> TryDeletePhotoAsync(IPhotoStorageService storage, string photoUrl, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                try { await storage.DeleteAsync(photoUrl, cancellationToken); return true; }
                catch (Exception ex) when (attempt < PhotoRetryDelays.Length)
                {
                    _logger?.LogWarning(ex, "Remoção de foto falhou (tentativa {Attempt}); a repetir.", attempt + 1);
                    await Task.Delay(PhotoRetryDelays[attempt], cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Remoção de foto falhou depois de {Attempts} tentativas; fica agendada.", attempt + 1);
                    return false;
                }
            }
        }

        /// <summary>
        /// Atualiza o Expo Push Token do utilizador.
        /// Só atualiza se o token for diferente do atual (evita writes desnecessários).
        /// </summary>
        public async Task UpdatePushTokenAsync(
            Guid userId, string expoPushToken, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty) throw new ArgumentException("ID inválido.");
            if (string.IsNullOrWhiteSpace(expoPushToken)) throw new ArgumentException("Token inválido.");

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null) throw new KeyNotFoundException("Utilizador não encontrado.");

            // Só guarda se o token mudou
            user.SetExpoPushToken(expoPushToken);

            await _userRepository.UpdateAsync(user, cancellationToken);
            await _userRepository.CommitAsync(cancellationToken);
        }

        public async Task<UserDto> GetUserStatisticsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await GetByIdAsync(userId, cancellationToken);
            if (user == null) throw new KeyNotFoundException("Usuário não encontrado.");
            return user;
        }

        private async Task<int> GetTotalGamesPlayedAsync(Guid userId, CancellationToken cancellationToken)
        {
            return await _matchRepository.GetByUserIdAsync(userId, cancellationToken: cancellationToken)
                .ContinueWith(task => task.Result.Count, cancellationToken);
        }

        private async Task<int> GetTotalWinsAsync(Guid userId, CancellationToken cancellationToken)
        {
            var matches = await _matchRepository.GetByUserIdAsync(userId, cancellationToken: cancellationToken);
            return matches.Count(m => m.MatchPlayers.Any(p => p.UserId == userId && p.Outcome == "Win"));
        }

        private async Task PopulateResultStatistics(UserDto dto, CancellationToken cancellationToken)
        {
            var matches = await _matchRepository.GetByUserIdAsync(dto.Id, cancellationToken: cancellationToken);
            var outcomes = matches.SelectMany(m => m.MatchPlayers.Where(p => p.UserId == dto.Id)).Select(p => p.Outcome).ToList();
            dto.TotalGamesPlayed = matches.Count;
            dto.TotalWins = outcomes.Count(o => o == "Win"); dto.TotalLosses = outcomes.Count(o => o == "Loss"); dto.TotalDraws = outcomes.Count(o => o == "Draw");
            dto.KnownResultMatches = dto.TotalWins + dto.TotalLosses + dto.TotalDraws;
            dto.MatchesWithoutResult = outcomes.Count(o => o == null || o == "Undefined");
            dto.LegacyResultMatches = matches.Count(m => m.Result == null);
            dto.WinRate = dto.KnownResultMatches > 0 ? Math.Round(100d * dto.TotalWins / dto.KnownResultMatches, 2) : null;
        }

        /// <summary>
        /// Atualiza quem pode ver a coleção de jogos do utilizador.
        /// </summary>
        public async Task UpdateLibraryPrivacyAsync(
            Guid userId, LibraryPrivacy privacy, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty) throw new ArgumentException("ID inválido.");

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null) throw new KeyNotFoundException("Utilizador não encontrado.");

            user.SetLibraryPrivacy(privacy);

            await _userRepository.UpdateAsync(user, cancellationToken);
            await _userRepository.CommitAsync(cancellationToken);
        }

        /// <summary>Only the account itself turns its contribution to community duration statistics on or off.</summary>
        public async Task UpdateDurationSharingAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty) throw new ArgumentException("Invalid id.");
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken) ?? throw new KeyNotFoundException("User not found.");
            user.SetContributesDurationStats(enabled);
            await _userRepository.UpdateAsync(user, cancellationToken);
            await _userRepository.CommitAsync(cancellationToken);
        }
    }
}