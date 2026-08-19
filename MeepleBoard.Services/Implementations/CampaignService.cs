using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.DTOs.Campaign;
using MeepleBoard.Services.DTOs.MatchJournal;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.Implementations
{
    public class CampaignService : ICampaignService
    {
        private const int MaxPhotosPerJournalEntry = 5;

        private readonly ICampaignRepository _campaignRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IUserRepository _userRepository;
        private readonly IGameRepository _gameRepository;
        private readonly INotificationService _notificationService;
        private readonly IPhotoStorageService _photoStorageService;
        private readonly ILogger<CampaignService> _logger;

        public CampaignService(
            ICampaignRepository campaignRepository,
            IMatchRepository matchRepository,
            IUserRepository userRepository,
            IGameRepository gameRepository,
            INotificationService notificationService,
            IPhotoStorageService photoStorageService,
            ILogger<CampaignService> logger)
        {
            _campaignRepository = campaignRepository;
            _matchRepository = matchRepository;
            _userRepository = userRepository;
            _gameRepository = gameRepository;
            _notificationService = notificationService;
            _photoStorageService = photoStorageService;
            _logger = logger;
        }

        /* ── Campanhas ────────────────────────────────────────────────────── */

        public async Task<IEnumerable<CampaignDto>> GetMineAsync(
            Guid userId, CancellationToken ct = default)
        {
            var campaigns = await _campaignRepository.GetListByUserAsync(userId, ct);
            return campaigns.Select(MapToDto);
        }

        public async Task<IEnumerable<CampaignDto>> GetByGameAsync(
            Guid gameId, Guid userId, CancellationToken ct = default)
        {
            var campaigns = await _campaignRepository.GetListByGameAsync(gameId, userId, ct);
            return campaigns.Select(MapToDto);
        }

        public async Task<CampaignDto?> GetByIdAsync(
            Guid id, Guid userId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdWithDetailsAsync(id, ct);
            if (campaign == null) return null;

            var isMember = campaign.CreatorId == userId ||
                campaign.Members.Any(m =>
                    m.UserId == userId &&
                    m.Status == CampaignMemberStatus.Accepted);

            if (!isMember)
                throw new UnauthorizedAccessException("Não tens acesso a esta campanha.");

            return MapToDtoWithDetails(campaign);
        }

        public async Task<CampaignDto> CreateAsync(
            CreateCampaignDto dto, Guid userId, CancellationToken ct = default)
        {
            var game = await _gameRepository.GetByIdAsync(dto.GameId, ct);
            if (game == null)
                throw new KeyNotFoundException("Jogo não encontrado.");

            var campaign = new Campaign(dto.Name, dto.GameId, userId);

            if (!string.IsNullOrWhiteSpace(dto.Notes))
                campaign.UpdateNotes(dto.Notes);

            await _campaignRepository.AddAsync(campaign, ct);

            var creatorMember = new CampaignMember(campaign.Id, userId, isCreator: true);
            await _campaignRepository.AddMemberAsync(creatorMember, ct);

            await _campaignRepository.SaveChangesAsync(ct);

            _logger.LogInformation(
                "✅ Campanha criada: {Name} (Id: {Id}) por {UserId}", campaign.Name, campaign.Id, userId);

            var created = await _campaignRepository.GetByIdWithDetailsAsync(campaign.Id, ct);
            return MapToDtoWithDetails(created!);
        }

        public async Task UpdateAsync(
            Guid id, UpdateCampaignDto dto, Guid userId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdForUpdateAsync(id, ct);
            if (campaign == null) throw new KeyNotFoundException("Campanha não encontrada.");

            await AssertIsMemberAsync(campaign, userId);

            campaign.UpdateName(dto.Name);
            campaign.UpdateNotes(dto.Notes);

            await _campaignRepository.SaveChangesAsync(ct);
        }

        public async Task CompleteAsync(Guid id, Guid userId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdForUpdateAsync(id, ct);
            if (campaign == null) throw new KeyNotFoundException("Campanha não encontrada.");

            await AssertIsMemberAsync(campaign, userId);
            campaign.Complete();
            await _campaignRepository.SaveChangesAsync(ct);
        }

        public async Task AbandonAsync(Guid id, Guid userId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdForUpdateAsync(id, ct);
            if (campaign == null) throw new KeyNotFoundException("Campanha não encontrada.");

            await AssertIsMemberAsync(campaign, userId);
            campaign.Abandon();
            await _campaignRepository.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdForUpdateAsync(id, ct);
            if (campaign == null) throw new KeyNotFoundException("Campanha não encontrada.");

            if (campaign.CreatorId != userId)
                throw new UnauthorizedAccessException("Só o criador pode apagar a campanha.");

            await _campaignRepository.DeleteAsync(id, ct);
            await _campaignRepository.SaveChangesAsync(ct);
        }

        /* ── Membros ─────────────────────────────────────────────────────── */

        public async Task InviteMemberAsync(
            Guid campaignId, Guid targetUserId, Guid requesterId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdForUpdateAsync(campaignId, ct);
            if (campaign == null) throw new KeyNotFoundException("Campanha não encontrada.");

            await AssertIsMemberAsync(campaign, requesterId);

            var target = await _userRepository.GetByIdAsync(targetUserId, ct);
            if (target == null) throw new KeyNotFoundException("Utilizador não encontrado.");

            var existing = await _campaignRepository.GetMemberAsync(campaignId, targetUserId, ct);
            if (existing != null)
            {
                var activeStatuses = new[] { CampaignMemberStatus.Pending, CampaignMemberStatus.Accepted };
                if (activeStatuses.Contains(existing.Status))
                    throw new InvalidOperationException("Este utilizador já foi convidado ou já é membro.");
            }

            var member = new CampaignMember(campaignId, targetUserId, isCreator: false);
            await _campaignRepository.AddMemberAsync(member, ct);
            await _campaignRepository.SaveChangesAsync(ct);

            // ── Notificação push ao utilizador convidado ───────────────────
            if (!string.IsNullOrWhiteSpace(target.ExpoPushToken))
            {
                var requester = await _userRepository.GetByIdAsync(requesterId, ct);
                await _notificationService.NotifyCampaignInviteAsync(
                    target.ExpoPushToken,
                    campaign.Name,
                    requester?.UserName ?? "Alguém",
                    ct);
            }

            _logger.LogInformation(
                "📩 Convite enviado: campanha {CampaignId} → utilizador {TargetId}", campaignId, targetUserId);
        }

        public async Task RespondInviteAsync(
            Guid campaignId, Guid userId, bool accept, CancellationToken ct = default)
        {
            var member = await _campaignRepository.GetMemberAsync(campaignId, userId, ct);
            if (member == null) throw new KeyNotFoundException("Convite não encontrado.");

            if (member.Status != CampaignMemberStatus.Pending)
                throw new InvalidOperationException("Já respondeste a este convite.");

            if (accept) member.Accept();
            else member.Decline();

            await _campaignRepository.SaveChangesAsync(ct);

            // ── Notificação ao criador da campanha ─────────────────────────
            if (accept)
            {
                var campaign = await _campaignRepository.GetByIdForUpdateAsync(campaignId, ct);
                if (campaign != null)
                {
                    var creator = await _userRepository.GetByIdAsync(campaign.CreatorId, ct);
                    var responder = await _userRepository.GetByIdAsync(userId, ct);

                    if (creator != null && !string.IsNullOrWhiteSpace(creator.ExpoPushToken))
                    {
                        await _notificationService.SendAsync(
                            creator.ExpoPushToken,
                            title: "🎉 Convite aceite!",
                            body: $"{responder?.UserName ?? "Alguém"} aceitou o convite para \"{campaign.Name}\".",
                            ct: ct);
                    }
                }
            }
        }

        public async Task RemoveMemberAsync(
            Guid campaignId, Guid targetUserId, Guid requesterId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdForUpdateAsync(campaignId, ct);
            if (campaign == null) throw new KeyNotFoundException("Campanha não encontrada.");

            await AssertIsMemberAsync(campaign, requesterId);

            if (targetUserId == campaign.CreatorId)
                throw new InvalidOperationException("O criador da campanha não pode ser removido.");

            var member = await _campaignRepository.GetMemberAsync(campaignId, targetUserId, ct);
            if (member == null) throw new KeyNotFoundException("Membro não encontrado.");

            member.Remove();
            await _campaignRepository.SaveChangesAsync(ct);

            // ── Notificação ao utilizador removido ─────────────────────────
            var target = await _userRepository.GetByIdAsync(targetUserId, ct);
            if (target != null && !string.IsNullOrWhiteSpace(target.ExpoPushToken))
            {
                await _notificationService.SendAsync(
                    target.ExpoPushToken,
                    title: "ℹ️ Removido de campanha",
                    body: $"Foste removido da campanha \"{campaign.Name}\".",
                    ct: ct);
            }
        }

        public async Task LeaveCampaignAsync(
            Guid campaignId, Guid userId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdForUpdateAsync(campaignId, ct);
            if (campaign == null) throw new KeyNotFoundException("Campanha não encontrada.");

            if (campaign.CreatorId == userId)
                throw new InvalidOperationException("O criador não pode sair. Usa Abandon() para abandonar a campanha.");

            var member = await _campaignRepository.GetMemberAsync(campaignId, userId, ct);
            if (member == null) throw new KeyNotFoundException("Não és membro desta campanha.");

            member.Leave();
            await _campaignRepository.SaveChangesAsync(ct);
        }

        /* ── Partidas ─────────────────────────────────────────────────────── */

        public async Task AddMatchAsync(
            Guid campaignId, AddMatchToCampaignDto dto, Guid userId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdForUpdateAsync(campaignId, ct);
            if (campaign == null) throw new KeyNotFoundException("Campanha não encontrada.");

            await AssertIsMemberAsync(campaign, userId);

            var match = await _matchRepository.GetByIdAsync(dto.MatchId, ct);
            if (match == null) throw new KeyNotFoundException("Partida não encontrada.");

            var existing = await _campaignRepository.GetCampaignMatchAsync(campaignId, dto.MatchId, ct);
            if (existing != null)
                throw new InvalidOperationException("Esta partida já está associada a esta campanha.");

            var campaignMatch = new CampaignMatch(campaignId, dto.MatchId, dto.SessionNumber, dto.SessionTitle);
            await _campaignRepository.AddCampaignMatchAsync(campaignMatch, ct);
            await _campaignRepository.SaveChangesAsync(ct);
        }

        public async Task RemoveMatchAsync(
            Guid campaignId, Guid matchId, Guid userId, CancellationToken ct = default)
        {
            var campaign = await _campaignRepository.GetByIdForUpdateAsync(campaignId, ct);
            if (campaign == null) throw new KeyNotFoundException("Campanha não encontrada.");

            await AssertIsMemberAsync(campaign, userId);

            await _campaignRepository.RemoveCampaignMatchAsync(campaignId, matchId, ct);
            await _campaignRepository.SaveChangesAsync(ct);
        }

        /* ── Diário ──────────────────────────────────────────────────────── */

        public async Task<JournalEntryDto> UpsertJournalEntryAsync(
            Guid matchId, UpsertJournalEntryDto dto, Guid userId, CancellationToken ct = default)
        {
            // ── Só jogadores que participaram na partida podem avaliar ─────
            var match = await _matchRepository.GetByIdAsync(matchId, ct);
            if (match == null)
                throw new KeyNotFoundException("Partida não encontrada.");

            var isPlayer = match.MatchPlayers.Any(p => p.UserId == userId);
            if (!isPlayer)
                throw new UnauthorizedAccessException(
                    "Só jogadores que participaram nesta partida podem avaliá-la.");

            var existing = await _campaignRepository.GetJournalEntryAsync(matchId, userId, ct);

            if (existing != null)
            {
                existing.Update(dto.PersonalRating, dto.Notes, dto.Tags);
                await _campaignRepository.UpdateJournalEntryAsync(existing, ct);
            }
            else
            {
                existing = new MatchJournalEntry(matchId, userId);
                existing.Update(dto.PersonalRating, dto.Notes, dto.Tags);
                await _campaignRepository.AddJournalEntryAsync(existing, ct);
            }

            await _campaignRepository.SaveChangesAsync(ct);

            // ── Recalcular o ranking interno (MeepleBoardScore) do jogo ─────
            // Média de todas as avaliações pessoais (1–10) de todas as partidas
            // deste jogo, escalada para 0–100 (mesma escala do campo).
            if (match.Game != null)
            {
                var avgRating = await _gameRepository.GetAveragePersonalRatingAsync(match.GameId, ct);
                var score = avgRating.HasValue ? (int?)Math.Round(avgRating.Value * 10) : null;
                match.Game.SetMeepleBoardScore(score);
                await _gameRepository.UpdateAsync(match.Game, ct);
            }

            // ── Notificar outros jogadores da partida ──────────────────────
            {
                var evaluator = await _userRepository.GetByIdAsync(userId, ct);
                var gameName = match.Game?.Name ?? "jogo";

                // Tokens dos outros jogadores (excluindo quem avaliou)
                var otherPlayerIds = match.MatchPlayers
                    .Where(p => p.UserId != userId)
                    .Select(p => p.UserId)
                    .ToList();

                var otherTokens = new List<string>();
                foreach (var pid in otherPlayerIds)
                {
                    var player = await _userRepository.GetByIdAsync(pid, ct);
                    if (!string.IsNullOrWhiteSpace(player?.ExpoPushToken))
                        otherTokens.Add(player.ExpoPushToken);
                }

                if (otherTokens.Count > 0)
                {
                    await _notificationService.NotifyJournalEntryAddedAsync(
                        matchId,
                        gameName,
                        evaluator?.UserName ?? "Alguém",
                        otherTokens,
                        ct);
                }

                // Verifica se todos avaliaram e fecha o diário se sim
                match.TryAutoCloseIfAllEvaluated();
                if (match.JournalStatus == MatchJournalStatus.Closed)
                    await _matchRepository.UpdateAsync(match, ct);

                await _matchRepository.SaveChangesAsync(ct);
            }

            return MapJournalEntryToDto(existing);
        }

        public async Task<IEnumerable<JournalEntryDto>> GetJournalEntriesAsync(
            Guid matchId, CancellationToken ct = default)
        {
            var entries = await _campaignRepository.GetJournalEntriesForMatchAsync(matchId, ct);
            return entries.Select(MapJournalEntryToDto);
        }

        public async Task<JournalEntryDto> AddJournalPhotoAsync(
            Guid matchId, Guid userId, Stream fileStream, string fileName, CancellationToken ct = default)
        {
            // ── Mesma regra do UpsertJournalEntryAsync: só jogadores da partida ──
            var match = await _matchRepository.GetByIdAsync(matchId, ct);
            if (match == null)
                throw new KeyNotFoundException("Partida não encontrada.");

            if (!match.MatchPlayers.Any(p => p.UserId == userId))
                throw new UnauthorizedAccessException(
                    "Só jogadores que participaram nesta partida podem adicionar fotos.");

            var entry = await _campaignRepository.GetJournalEntryAsync(matchId, userId, ct);
            var isNew = entry == null;
            entry ??= new MatchJournalEntry(matchId, userId);

            if (entry.PhotoUrls.Count >= MaxPhotosPerJournalEntry)
                throw new InvalidOperationException(
                    $"Só podes ter até {MaxPhotosPerJournalEntry} fotos por partida.");

            // Upload primeiro — só grava a entrada se a foto for enviada com sucesso
            var url = await _photoStorageService.UploadAsync(fileStream, fileName, ct);
            entry.AddPhotoUrl(url);

            if (isNew)
                await _campaignRepository.AddJournalEntryAsync(entry, ct);
            else
                await _campaignRepository.UpdateJournalEntryAsync(entry, ct);

            await _campaignRepository.SaveChangesAsync(ct);

            return MapJournalEntryToDto(entry);
        }

        public async Task<JournalEntryDto> RemoveJournalPhotoAsync(
            Guid matchId, Guid userId, string photoUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(photoUrl))
                throw new ArgumentException("URL da foto inválido.", nameof(photoUrl));

            var entry = await _campaignRepository.GetJournalEntryAsync(matchId, userId, ct);
            if (entry == null)
                throw new KeyNotFoundException("Ainda não tens uma entrada de diário nesta partida.");

            entry.RemovePhotoUrl(photoUrl);
            await _campaignRepository.UpdateJournalEntryAsync(entry, ct);
            await _campaignRepository.SaveChangesAsync(ct);

            // Best-effort: se falhar a apagar no Cloudinary, não bloqueia o utilizador
            // (fica só uma imagem órfã no storage — não afeta a app).
            try
            {
                await _photoStorageService.DeleteAsync(photoUrl, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao apagar foto {PhotoUrl} do storage.", photoUrl);
            }

            return MapJournalEntryToDto(entry);
        }

        /* ── Helpers de mapeamento ────────────────────────────────────────── */

        private static CampaignDto MapToDto(Campaign c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            GameId = c.GameId,
            GameName = c.Game?.Name,
            GameImageUrl = c.Game?.ImageUrl,
            CreatorId = c.CreatorId,
            CreatorUserName = c.Creator?.UserName,
            Status = c.Status.ToString(),
            Notes = c.Notes,
            MemberCount = c.Members?.Count(m => m.Status == CampaignMemberStatus.Accepted) ?? 0,
            MatchCount = c.CampaignMatches?.Count ?? 0,
            CreatedAt = c.CreatedAt,
            CompletedAt = c.CompletedAt,
        };

        private static CampaignDto MapToDtoWithDetails(Campaign c)
        {
            var dto = MapToDto(c);

            dto.Members = c.Members?.Select(m => new CampaignMemberDto
            {
                UserId = m.UserId,
                UserName = m.User?.UserName ?? "Desconhecido",
                IsCreator = m.IsCreator,
                Status = m.Status.ToString(),
                InvitedAt = m.InvitedAt,
                RespondedAt = m.RespondedAt,
            }).ToList() ?? new();

            dto.Matches = c.CampaignMatches?.Select(cm => new CampaignMatchDto
            {
                Id = cm.Id,
                MatchId = cm.MatchId,
                GameName = cm.Match?.Game?.Name,
                MatchDate = cm.Match?.MatchDate ?? DateTime.MinValue,
                SessionNumber = cm.SessionNumber,
                SessionTitle = cm.SessionTitle,
                IsSoloGame = cm.Match?.IsSoloGame ?? false,
                DurationInMinutes = cm.Match?.DurationInMinutes,
                Location = cm.Match?.Location,
            })
            .OrderBy(cm => cm.SessionNumber)
            .ThenBy(cm => cm.MatchDate)
            .ToList() ?? new();

            // Une as avaliações de todos os membros (MatchJournalEntry) com o valor
            // legado (Match.PersonalRating) só para partidas sem nenhuma entrada de
            // diário ainda — mesma lógica do GameRepository.GetAveragePersonalRatingAsync.
            var allRatings = c.CampaignMatches?
                .SelectMany(cm =>
                {
                    var match = cm.Match;
                    if (match == null) return Enumerable.Empty<double>();

                    var journalRatings = (match.JournalEntries ?? new List<MatchJournalEntry>())
                        .Where(e => e.PersonalRating.HasValue)
                        .Select(e => (double)e.PersonalRating!.Value);

                    if (journalRatings.Any()) return journalRatings;

                    // Legado: sem entradas de diário, mas com valor antigo em Match
                    return match.PersonalRating.HasValue
                        ? new[] { match.PersonalRating.Value }
                        : Enumerable.Empty<double>();
                })
                .ToList() ?? new List<double>();

            dto.AveragePersonalRating = allRatings.Count > 0 ? allRatings.Average() : null;

            return dto;
        }

        private static JournalEntryDto MapJournalEntryToDto(MatchJournalEntry e) => new()
        {
            Id = e.Id,
            UserId = e.UserId,
            UserName = e.User?.UserName ?? "Desconhecido",
            PersonalRating = e.PersonalRating,
            Notes = e.Notes,
            Tags = e.Tags,
            PhotoUrls = e.PhotoUrls,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
        };

        /* ── Helper de validação ─────────────────────────────────────────── */

        private async Task AssertIsMemberAsync(Campaign campaign, Guid userId)
        {
            if (campaign.CreatorId == userId) return;

            var member = await _campaignRepository.GetMemberAsync(campaign.Id, userId);
            if (member == null || member.Status != CampaignMemberStatus.Accepted)
                throw new UnauthorizedAccessException("Não és membro desta campanha.");
        }
    }
}