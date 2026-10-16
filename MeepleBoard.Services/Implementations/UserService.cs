using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Interfaces;
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

        public UserService(IUserRepository userRepository, IMatchRepository matchRepository, IMapper mapper,
            IPhotoStorageService? photoStorageService = null, ILogger<UserService>? logger = null)
        {
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

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty) throw new ArgumentException("ID inválido.");

            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null || user.DeletedAt.HasValue) throw new KeyNotFoundException("Usuário não encontrado.");

            // Anonymised, not removed: shared matches keep their players. Ratings go with the account and the
            // games they counted in are recalculated in the same transaction.
            var result = await _userRepository.AnonymizeAsync(id, $"Conta eliminada {id.ToString("N")[..8]}", cancellationToken);

            // Storage is outside the transaction: a failure leaves an unreachable file, never a half-deleted account.
            if (_photoStorageService == null) return;
            foreach (var photoUrl in result.PhotoUrls)
            {
                try { await _photoStorageService.DeleteAsync(photoUrl, cancellationToken); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Falha ao apagar foto {PhotoUrl} de uma conta eliminada.", photoUrl); }
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