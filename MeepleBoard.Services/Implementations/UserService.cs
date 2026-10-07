using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;

namespace MeepleBoard.Services.Implementations
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IMapper _mapper;

        public UserService(IUserRepository userRepository, IMatchRepository matchRepository, IMapper mapper)
        {
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
            if (user == null) throw new KeyNotFoundException("Usuário não encontrado.");

            await _userRepository.DeleteAsync(id, cancellationToken);
            await _userRepository.CommitAsync(cancellationToken);
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
    }
}