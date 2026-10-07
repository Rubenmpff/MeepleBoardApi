using AutoMapper;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Mapping.Dtos;

namespace MeepleBoard.Services.Implementations
{
    public class MatchService : IMatchService
    {
        private readonly IMatchRepository _matchRepository;
        private readonly IMatchPlayerRepository _matchPlayerRepository;
        private readonly IGameSessionRepository _gameSessionRepository;
        private readonly IGameSessionPlayerRepository _gameSessionPlayerRepository;
        private readonly IUserRepository _userRepository;
        private readonly IGameRepository _gameRepository;
        private readonly ICampaignRepository _campaignRepository;
        private readonly IBGGService _bggService;
        private readonly IGameService _gameService;
        private readonly INotificationService _notificationService;
        private readonly IMapper _mapper;

        public MatchService(
            IMatchRepository matchRepository,
            IMatchPlayerRepository matchPlayerRepository,
            IGameSessionRepository gameSessionRepository,
            IGameSessionPlayerRepository gameSessionPlayerRepository,
            IUserRepository userRepository,
            IGameRepository gameRepository,
            ICampaignRepository campaignRepository,
            IBGGService bggService,
            IGameService gameService,
            INotificationService notificationService,
            IMapper mapper)
        {
            _matchRepository = matchRepository;
            _matchPlayerRepository = matchPlayerRepository;
            _gameSessionRepository = gameSessionRepository;
            _gameSessionPlayerRepository = gameSessionPlayerRepository;
            _userRepository = userRepository;
            _gameRepository = gameRepository;
            _campaignRepository = campaignRepository;
            _bggService = bggService;
            _gameService = gameService;
            _notificationService = notificationService;
            _mapper = mapper;
        }

        /* ── Leitura ─────────────────────────────────────────────────────────── */

        public async Task<IEnumerable<MatchDto>> GetAllAsync(
            Guid viewerId, int pageIndex = 0, int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            if (viewerId == Guid.Empty) throw new UnauthorizedAccessException();
            var matches = await _matchRepository.GetPageForUserAsync(viewerId, pageIndex, pageSize, cancellationToken);
            return _mapper.Map<IEnumerable<MatchDto>>(matches);
        }

        public async Task<IEnumerable<MatchDto>> GetRecentMatchesAsync(
            int count, CancellationToken cancellationToken = default)
        {
            var matches = await _matchRepository.GetRecentMatchesSinceAsync(count, cancellationToken: cancellationToken);
            return _mapper.Map<IEnumerable<MatchDto>>(matches);
        }

        // ✅ Usa projecção directa — 1 linha SQL, sem carregar entidades
        // ✅ Se imagem vazia, actualiza em background via BGG sem bloquear resposta
        public async Task<LastMatchDto?> GetLastMatchForUserAsync(Guid userId)
        {
            var result = await _matchRepository.GetLastMatchProjectionForUserAsync(userId, CancellationToken.None);
            if (result == null) return null;

            var imageUrl = result.Value.ImageUrl;

            // Se não tem imagem, vai ao BGG buscar em background
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var game = await _gameRepository.GetByNameAsync(result.Value.Name, CancellationToken.None);
                        if (game?.BGGId != null)
                        {
                            var bggGame = await _bggService.GetGameByIdAsync(
                                game.BGGId.Value.ToString(), CancellationToken.None);
                            if (!string.IsNullOrWhiteSpace(bggGame?.ImageUrl))
                            {
                                game.UpdateBggStats(null, bggGame.ImageUrl, null, null);
                                await _gameRepository.UpdateAsync(game, CancellationToken.None);
                                await _gameRepository.CommitAsync(CancellationToken.None);
                            }
                        }
                    }
                    catch
                    {
                        // Silencioso — não bloqueia a resposta ao utilizador
                    }
                });
            }

            return new LastMatchDto
            {
                Name = result.Value.Name,
                Date = result.Value.Date,
                Winner = result.Value.Winner, GameMode = result.Value.GameMode, Result = result.Value.Result, WinnerNames = result.Value.WinnerNames,
                ImageUrl = imageUrl,
            };
        }

        public async Task<MatchDto?> GetByIdAsync(
            Guid id, Guid viewerId, CancellationToken cancellationToken = default)
        {
            var match = await _matchRepository.GetByIdAsync(id, cancellationToken);
            if (match == null) return null;
            RequireParticipant(match, viewerId);
            return _mapper.Map<MatchDto>(match);
        }

        public async Task<IEnumerable<MatchDto>> GetByUserIdAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            await ValidateUserExistsAsync(userId, cancellationToken);
            var matches = await _matchRepository.GetByUserIdAsync(userId, cancellationToken);
            return _mapper.Map<IEnumerable<MatchDto>>(matches);
        }

        public async Task<IEnumerable<MatchDto>> GetByGameIdAsync(
            Guid gameId, CancellationToken cancellationToken = default)
        {
            await ValidateGameExistsAsync(gameId, cancellationToken);
            var matches = await _matchRepository.GetByGameIdAsync(gameId, cancellationToken);
            return _mapper.Map<IEnumerable<MatchDto>>(matches);
        }

        // ✅ Usa projecção directa no repositório
        public async Task<IEnumerable<MatchDto>> GetMatchHistoryByGameAsync(
            Guid gameId, Guid userId, CancellationToken cancellationToken = default)
        {
            if (gameId == Guid.Empty) throw new ArgumentException("ID do jogo inválido.");
            if (userId == Guid.Empty) throw new ArgumentException("ID do utilizador inválido.");
            var matches = await _matchRepository.GetMatchHistoryByGameForUserAsync(gameId, userId, cancellationToken);
            var result = _mapper.Map<List<MatchDto>>(matches);
            foreach (var item in result)
            {
                var ownEntry = await _campaignRepository.GetJournalEntryAsync(item.Id, userId, cancellationToken);
                item.PersonalNotes = ownEntry?.Notes;
            }
            return result;
        }

        // ✅ Usa projecção directa — só busca ratings na BD, calcula média no serviço
        public async Task<double?> GetUserRatingForGameAsync(
            Guid gameId, Guid userId, CancellationToken cancellationToken = default)
        {
            if (gameId == Guid.Empty) throw new ArgumentException("ID do jogo inválido.");
            if (userId == Guid.Empty) throw new ArgumentException("ID do utilizador inválido.");
            var ratings = await _matchRepository.GetRatingsForGameByUserAsync(gameId, userId, cancellationToken);
            return ratings.Count > 0 ? ratings.Average() : null;
        }

        /* ── Diário ──────────────────────────────────────────────────────────── */

        // ✅ Usa projecção directa — só os campos necessários
        public async Task<IEnumerable<MatchDto>> GetPendingJournalMatchesAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty) throw new ArgumentException("ID do utilizador inválido.");
            var matches = await _matchRepository.GetPendingJournalMatchesForUserAsync(userId, cancellationToken);
            return _mapper.Map<IEnumerable<MatchDto>>(matches);
        }

        public async Task CloseJournalAsync(
            Guid matchId, Guid userId, CancellationToken cancellationToken = default)
        {
            if (matchId == Guid.Empty) throw new ArgumentException("ID da partida inválido.");
            if (userId == Guid.Empty) throw new ArgumentException("ID do utilizador inválido.");

            var match = await _matchRepository.GetByIdAsync(matchId, cancellationToken);
            if (match == null) throw new KeyNotFoundException("Partida não encontrada.");

            RequireCreator(match, userId);

            match.CloseJournal();
            await _matchRepository.UpdateAsync(match, cancellationToken);
            await _matchRepository.SaveChangesAsync(cancellationToken);

            var pendingPlayerIds = match.MatchPlayers
                .Where(p => !match.JournalEntries.Any(e => e.UserId == p.UserId && e.PersonalRating.HasValue))
                .Select(p => p.UserId)
                .Where(id => id != userId)
                .ToList();

            if (pendingPlayerIds.Count > 0)
            {
                var tokens = new List<string>();
                foreach (var pid in pendingPlayerIds)
                {
                    var player = await _userRepository.GetByIdAsync(pid, cancellationToken);
                    if (player != null && !string.IsNullOrWhiteSpace(player.ExpoPushToken))
                        tokens.Add(player.ExpoPushToken);
                }
                if (tokens.Count > 0)
                    await _notificationService.NotifyMatchJournalClosedAsync(
                        matchId, match.Game?.Name ?? "jogo", tokens, cancellationToken);
            }
        }

        /* ── Criação ─────────────────────────────────────────────────────────── */

        public async Task<MatchDto> CreateAsync(
            CreateMatchDto dto, Guid authenticatedUserId,
            CancellationToken cancellationToken = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (authenticatedUserId == Guid.Empty)
                throw new ArgumentException("Utilizador autenticado inválido.");

            MatchJournalEntry.ValidateRating(dto.PersonalRating, required: true);
            if (dto.GameSessionId.HasValue && dto.IsSoloGame)
                throw new ArgumentException("Não são permitidas partidas Solo dentro de sessões.");

            // Validate before game lookup/import or any repository write.
            // Keep legacy PlayerIds normalization; score entries must be unambiguous.
            var scores = new Dictionary<Guid, int?>();
            foreach (var entry in dto.PlayerScores ?? new List<CreateMatchPlayerScoreDto>())
            {
                if (entry == null || entry.UserId == Guid.Empty ||
                    dto.PlayerIds == null || !dto.PlayerIds.Contains(entry.UserId))
                    throw new ArgumentException("A pontuação deve pertencer a um participante indicado em PlayerIds.");
                if (!scores.TryAdd(entry.UserId, entry.Score))
                    throw new ArgumentException("Não pode existir mais de uma pontuação para o mesmo participante.");
            }

            var playerIds = (dto.PlayerIds ?? new List<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            if (playerIds.Count == 0)
                throw new ArgumentException("A partida deve ter pelo menos um jogador.");

            // Quick registration includes the authenticated actor; session registration must select them.
            if (dto.GameSessionId is null && !playerIds.Contains(authenticatedUserId))
                playerIds.Add(authenticatedUserId);

            if (!dto.IsSoloGame && playerIds.Count < 2)
                throw new ArgumentException("Uma partida competitiva exige pelo menos dois participantes distintos.");

            var outcomes = dto.Result == null ? null : MatchOutcomeRules.Resolve(dto.GameMode, dto.Result, playerIds,
                dto.ResultPlayerIds, dto.SharedVictoryAllowed == true, dto.GameSessionId.HasValue);
            if (outcomes != null && dto.IsSoloGame != (dto.GameMode == "SOLO")) throw new ArgumentException("O modo e a indicação Solo devem ser coerentes.");
            if (outcomes != null && dto.WinnerId.HasValue) {
                var wins = outcomes.Where(p => p.Value == "Win").Select(p => p.Key).ToList();
                if (dto.GameMode == "COOPERATIVE" || wins.Count != 1 || wins[0] != dto.WinnerId) throw new ArgumentException("O vencedor individual é incompatível com o resultado indicado.");
            }

            var scoresEnabled = dto.ScoresEnabled ?? scores.Values.Any(score => score.HasValue);
            if (dto.ScoresEnabled == false && scores.Count > 0)
                throw new ArgumentException("Uma partida sem pontuação não pode enviar valores.");
            if (scoresEnabled && playerIds.Any(id => !scores.TryGetValue(id, out var score) || !score.HasValue))
                throw new ArgumentException("Indica uma pontuação válida para todos os jogadores.");

            var game = await ValidateOrFetchGameAsync(dto.GameId, dto.GameName, cancellationToken);

            if (dto.GameSessionId is not null)
            {
                var sessionId = dto.GameSessionId.Value;
                var session = await _gameSessionRepository.GetByIdWithDetailsAsync(sessionId, cancellationToken);
                if (session == null) throw new KeyNotFoundException("Sessão não encontrada.");

                if (!session.CanRegisterMatches())
                {
                    var reason = session.Status switch
                    {
                        "Upcoming" => "A sessão ainda não está ativa: precisa de pelo menos 1 convidado confirmado, além de ti.",
                        "Closed" => "Esta sessão já foi encerrada e não aceita novas partidas.",
                        "Cancelled" => "Esta sessão foi cancelada.",
                        _ => "Esta sessão não aceita novas partidas neste momento.",
                    };
                    throw new InvalidOperationException(reason);
                }

                var authLink = await _gameSessionPlayerRepository
                    .GetBySessionAndUserAsync(sessionId, authenticatedUserId);
                if (authLink == null)
                    throw new UnauthorizedAccessException("Não és membro desta sessão.");

                if (authLink.Status != MeepleBoard.Domain.Enums.GameSessionInviteStatus.Accepted)
                    throw new UnauthorizedAccessException("Ainda não aceitaste o convite desta sessão.");

                var sessionPlayers = await _gameSessionPlayerRepository.GetBySessionIdAsync(sessionId);
                var acceptedSet = sessionPlayers
                    .Where(p => p.Status == MeepleBoard.Domain.Enums.GameSessionInviteStatus.Accepted)
                    .Select(p => p.UserId)
                    .ToHashSet();

                if (playerIds.Any(pid => !acceptedSet.Contains(pid)))
                    throw new InvalidOperationException(
                        "Todos os jogadores têm de pertencer à sessão e ter aceite o convite.");

                if (!playerIds.Contains(authenticatedUserId))
                    throw new InvalidOperationException(
                        "O utilizador autenticado tem de participar no match da sessão.");
            }

            if (outcomes == null && !dto.IsSoloGame)
            {
                if (!dto.WinnerId.HasValue || dto.WinnerId.Value == Guid.Empty)
                    throw new ArgumentException("O vencedor é obrigatório em partidas multiplayer.");
                if (!playerIds.Contains(dto.WinnerId.Value))
                    throw new ArgumentException("O vencedor tem de estar incluído nos jogadores da partida.");
            }

            var match = new Match(game.Id, dto.MatchDate, dto.GameSessionId);
            match.SetCreator(authenticatedUserId);
            match.UpdateMatchDetails(dto.Location, dto.ScoreSummary, dto.DurationInMinutes);
            match.SetSoloGame(dto.IsSoloGame);

            if (outcomes != null) match.SetExplicitResult(dto.GameMode!, dto.Result!, dto.SharedVictoryAllowed == true, outcomes);
            else if (dto.WinnerId.HasValue && dto.WinnerId.Value != Guid.Empty) match.SetWinner(dto.WinnerId);

            if (dto.PersonalRating.HasValue ||
                !string.IsNullOrWhiteSpace(dto.Notes) ||
                !string.IsNullOrWhiteSpace(dto.Tags))
                match.SetJournalData(dto.PersonalRating, null, dto.Tags);

            if (!string.IsNullOrWhiteSpace(dto.UnofficialModeJustification))
                match.SetOfficialMode(false, dto.UnofficialModeJustification);

            await _matchRepository.AddAsync(match, cancellationToken);

            foreach (var userId in playerIds)
            {
                var mp = new MatchPlayer(match.Id, userId);
                if (scores.TryGetValue(userId, out var playerScore))
                    mp.UpdateScore(playerScore);
                if (outcomes != null) mp.SetOutcome(outcomes[userId]);
                else if (dto.WinnerId.HasValue && dto.WinnerId.Value == userId) mp.SetWinner(true);
                await _matchPlayerRepository.AddAsync(mp, cancellationToken);
            }

            await _matchRepository.SaveChangesAsync(cancellationToken);

            // ── Espelhar a avaliação do criador para o diário (MatchJournalEntry) ──
            // Match.PersonalRating/Tags remain legacy convenience copies; personal notes are never copied.
            // The previous Match.PersonalRating/Notes/Tags convenience copy
            // de conveniência no próprio registo da partida. A fonte de verdade para
            // "o que os outros jogadores acharam" e para o MeepleBoardScore do jogo
            // é sempre o MatchJournalEntry — sem isto, a avaliação dada aqui no
            // registo nunca entrava na média do jogo nem aparecia aos outros jogadores.
            if (dto.PersonalRating.HasValue ||
                !string.IsNullOrWhiteSpace(dto.Notes) ||
                !string.IsNullOrWhiteSpace(dto.Tags))
            {
                var entry = new MatchJournalEntry(match.Id, authenticatedUserId);
                entry.Update(dto.PersonalRating, dto.Notes, dto.Tags);
                await _campaignRepository.AddJournalEntryAsync(entry, cancellationToken);
                await _campaignRepository.SaveChangesAsync(cancellationToken);

                var avgRating = await _gameRepository.GetAveragePersonalRatingAsync(game.Id, cancellationToken);
                var score = avgRating.HasValue ? (int?)Math.Round(avgRating.Value * 10) : null;
                game.SetMeepleBoardScore(score);
                await _gameRepository.UpdateAsync(game, cancellationToken);
            }

            // ✅ Recarrega com GetByIdAsync — tem todos os includes necessários
            var created = await _matchRepository.GetByIdAsync(match.Id, cancellationToken)
                ?? throw new InvalidOperationException("Erro ao carregar partida criada.");

            var otherPlayerIds = playerIds.Where(id => id != authenticatedUserId).ToList();
            if (otherPlayerIds.Count > 0)
            {
                var tokens = new List<string>();
                foreach (var pid in otherPlayerIds)
                {
                    var player = await _userRepository.GetByIdAsync(pid, cancellationToken);
                    if (player != null && !string.IsNullOrWhiteSpace(player.ExpoPushToken))
                        tokens.Add(player.ExpoPushToken);
                }
                if (tokens.Count > 0)
                    await _notificationService.NotifyMatchCreatedAsync(
                        match.Id, game.Name, tokens, cancellationToken);
            }

            return _mapper.Map<MatchDto>(created);
        }

        /* ── Legacy ─────────────────────────────────────────────────────────── */

        public async Task<MatchDto> AddAsync(
            MatchDto matchDto, CancellationToken cancellationToken = default)
        {
            if (matchDto == null) throw new ArgumentNullException(nameof(matchDto));
            var game = await ValidateOrFetchGameAsync(matchDto.GameId, matchDto.GameName, cancellationToken);
            var match = new Match(game.Id, matchDto.MatchDate);
            match.UpdateMatchDetails(matchDto.Location, matchDto.ScoreSummary, matchDto.DurationInMinutes);
            match.SetSoloGame(matchDto.IsSoloGame);
            match.SetWinner(matchDto.WinnerId);
            await _matchRepository.AddAsync(match, cancellationToken);
            await _matchRepository.SaveChangesAsync(cancellationToken);
            var created = await _matchRepository.GetByIdAsync(match.Id, cancellationToken)
                ?? throw new InvalidOperationException("Erro ao carregar partida criada.");
            return _mapper.Map<MatchDto>(created);
        }

        public async Task<int> UpdateAsync(
            MatchDto matchDto, Guid userId, CancellationToken cancellationToken = default)
        {
            var match = await _matchRepository.GetByIdAsync(matchDto.Id, cancellationToken);
            if (match == null) throw new KeyNotFoundException("Partida não encontrada.");
            RequireCreator(match, userId);
            if (matchDto.Result != null) {
                var outcomes = MatchOutcomeRules.Resolve(matchDto.GameMode, matchDto.Result, match.MatchPlayers.Select(p => p.UserId), matchDto.ResultPlayerIds, matchDto.SharedVictoryAllowed == true, match.GameSessionId.HasValue);
                if (matchDto.IsSoloGame != (matchDto.GameMode == "SOLO")) throw new ArgumentException("O modo e a indicação Solo devem ser coerentes.");
                var individualWinners = outcomes.Where(p => p.Value == "Win").Select(p => p.Key).ToList();
                var expectedWinner = matchDto.GameMode != "COOPERATIVE" && individualWinners.Count == 1 ? (Guid?)individualWinners[0] : null;
                if (matchDto.WinnerId.HasValue && matchDto.WinnerId != expectedWinner) throw new ArgumentException("O vencedor indicado não corresponde ao resultado escolhido.");
                match.SetExplicitResult(matchDto.GameMode!, matchDto.Result, matchDto.SharedVictoryAllowed == true, outcomes);
                foreach (var player in match.MatchPlayers) player.SetOutcome(outcomes[player.UserId]);
            } else if (match.Result == null) {
                match.SetSoloGame(matchDto.IsSoloGame); match.SetWinner(matchDto.WinnerId);
            }
            match.SetGameId(matchDto.GameId);
            match.SetMatchDate(matchDto.MatchDate);
            match.UpdateMatchDetails(matchDto.Location, matchDto.ScoreSummary, matchDto.DurationInMinutes);
            await _matchRepository.UpdateAsync(match, cancellationToken);
            return await _matchRepository.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> DeleteAsync(
            Guid id, Guid userId, CancellationToken cancellationToken = default)
        {
            var match = await _matchRepository.GetByIdAsync(id, cancellationToken);
            if (match == null) return false;
            RequireCreator(match, userId);
            await _matchRepository.DeleteAsync(id, cancellationToken: cancellationToken);
            await _matchRepository.SaveChangesAsync(cancellationToken);
            return true;
        }

        public static void RequireParticipant(Match match, Guid viewerId)
        {
            if (viewerId == Guid.Empty || !match.MatchPlayers.Any(p => p.UserId == viewerId))
                throw new UnauthorizedAccessException("Não participaste nesta partida.");
        }

        public static void RequireCreator(Match match, Guid userId)
        {
            if (userId == Guid.Empty || match.CreatorId != userId)
                throw new UnauthorizedAccessException("Só o criador pode alterar ou eliminar a partida; autoria desconhecida nas partidas antigas.");
        }

        /* ── Helpers privados ───────────────────────────────────────────────── */

        private async Task ValidateGameExistsAsync(Guid gameId, CancellationToken cancellationToken)
        {
            if (gameId == Guid.Empty) throw new ArgumentException("ID do jogo inválido.");
            var game = await _gameRepository.GetByIdAsync(gameId, cancellationToken);
            if (game == null) throw new KeyNotFoundException("Jogo não encontrado.");
        }

        private async Task ValidateUserExistsAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty) throw new ArgumentException("ID do utilizador inválido.");
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null) throw new KeyNotFoundException("Utilizador não encontrado.");
        }

        private async Task<Game> ValidateOrFetchGameAsync(
            Guid gameId, string gameName, CancellationToken cancellationToken)
        {
            if (gameId != Guid.Empty)
            {
                var existingGame = await _gameRepository.GetByIdAsync(gameId, cancellationToken);
                if (existingGame != null) return existingGame;
            }
            if (!string.IsNullOrWhiteSpace(gameName))
            {
                var existingGame = await _gameRepository.GetByNameAsync(gameName, cancellationToken);
                if (existingGame != null) return existingGame;
            }
            if (!string.IsNullOrWhiteSpace(gameName))
            {
                var gameFromBGG = await _bggService.GetGameByNameAsync(gameName, cancellationToken);
                if (gameFromBGG?.BggId != null)
                    return await _gameService.GetOrCreateMinimalGameAsync(
                        gameFromBGG.BggId.Value, cancellationToken);
            }
            throw new KeyNotFoundException("Jogo não encontrado.");
        }
    }
}