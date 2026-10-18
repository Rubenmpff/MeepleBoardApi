using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Services.Calculators;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.Implementations
{
    public class CommunityService(ICommunityRepository community, IUserRepository users, IPhotoStorageService photos, ILogger<CommunityService> logger,
        IExperienceRepository? experiences = null) : ICommunityService
    {
        public async Task<IReadOnlyList<BlockedPersonDto>> MyBlocksAsync(Guid userId, CancellationToken ct = default) =>
            (await community.MyBlocksAsync(userId, ct)).Select(b => new BlockedPersonDto(b.Id, b.UserName, b.BlockedAt)).ToList();

        public async Task BlockAsync(Guid userId, Guid targetId, CancellationToken ct = default)
        {
            await RequireOtherActiveAccountAsync(userId, targetId, ct);
            // Repeating it changes nothing; the other person is never told.
            await community.BlockAsync(userId, targetId, ct);
        }

        public async Task BlockPublicationAuthorAsync(Guid userId, Guid publicationId, CancellationToken ct = default)
        {
            var pub = experiences == null ? null : await experiences.GetWithReportsForUpdateAsync(publicationId, ct);
            if (pub?.JournalEntry == null || !pub.IsVisible)
                throw new KeyNotFoundException("Publicação não encontrada.");
            await BlockAsync(userId, pub.JournalEntry.UserId, ct);
        }

        public async Task UnblockAsync(Guid userId, Guid targetId, CancellationToken ct = default)
        {
            // Only the person's own block; the friendship does not come back and a block by the other person stays.
            if (!await community.UnblockAsync(userId, targetId, ct)) throw new KeyNotFoundException("Não bloqueaste esta pessoa.");
        }

        public async Task ReportPersonAsync(Guid reporterId, Guid targetId, ReportDto dto, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);
            await RequireOtherActiveAccountAsync(reporterId, targetId, ct);
            if (await community.HasOpenUserReportAsync(reporterId, targetId, ct)) throw new InvalidOperationException("Já denunciaste esta pessoa.");
            await community.AddUserReportAsync(new UserReport(reporterId, targetId, dto.Reason, dto.Detail), ct);
            await community.SaveChangesAsync(ct);
        }

        public async Task ReportPhotoAsync(Guid reporterId, Guid matchId, Guid entryId, string photoKey, ReportDto dto, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);
            // Only a photo the reporter can see: a diary photo of a match they played, in someone else's entry.
            var entry = await community.GetJournalEntryAsync(entryId, ct);
            if (entry == null || entry.MatchId != matchId) throw new KeyNotFoundException("Fotografia não encontrada.");
            if (entry.Match == null || !entry.Match.MatchPlayers.Any(p => p.UserId == reporterId)) throw new UnauthorizedAccessException("Não participaste nesta partida.");
            var url = PhotoByKey(entry, photoKey) ?? throw new KeyNotFoundException("Fotografia não encontrada.");
            if (entry.UserId == reporterId) throw new InvalidOperationException("Não podes denunciar a tua fotografia.");
            if (await community.HasOpenPhotoReportAsync(reporterId, entryId, url, ct)) throw new InvalidOperationException("Já denunciaste esta fotografia.");
            await community.AddPhotoReportAsync(new PhotoReport(reporterId, entryId, url, dto.Reason, dto.Detail), ct);
            await community.SaveChangesAsync(ct);
            await HideReportedCopiesAsync(entryId, url, ct);
        }

        public async Task ReportPublicPhotoAsync(Guid reporterId, Guid publicationId, Guid photoId, ReportDto dto, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);
            if (reporterId == Guid.Empty) throw new UnauthorizedAccessException();
            // Only a photo this person can see in the community now (blocks and hiding included); a second report is refused as such.
            var photo = experiences == null ? null : await experiences.GetPublicPhotoAsync(publicationId, photoId, reporterId, ct, evenIfReportedByViewer: true);
            if (photo == null) throw new KeyNotFoundException("Fotografia não encontrada.");
            if (photo.AuthorId == reporterId) throw new InvalidOperationException("Não podes denunciar a tua fotografia.");
            if (await community.HasOpenPhotoReportAsync(reporterId, photo.JournalEntryId, photo.PhotoUrl, ct)) throw new InvalidOperationException("Já denunciaste esta fotografia.");
            await community.AddPhotoReportAsync(new PhotoReport(reporterId, photo.JournalEntryId, photo.PhotoUrl, dto.Reason, dto.Detail), ct);
            await community.SaveChangesAsync(ct);
            await HideReportedCopiesAsync(photo.JournalEntryId, photo.PhotoUrl, ct);
        }

        // Distinct people with an open report (from the match or the community) hide the community copies until reviewed;
        // the diary photo stays with the players and an administrator can still decide at any time.
        async Task HideReportedCopiesAsync(Guid entryId, string url, CancellationToken ct)
        {
            if (experiences == null || await experiences.OpenPhotoReportersAsync(entryId, url, ct) < PublicationPhoto.AutoHideThreshold) return;
            var copies = await experiences.PhotosOfDiaryPhotoAsync(entryId, url, ct);
            if (copies.Count == 0) return;
            foreach (var copy in copies) copy.HideAutomatically();
            await experiences.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<ModerationPersonDto>> ModerationPeopleAsync(CancellationToken ct = default)
        {
            var reported = await community.ReportedPeopleAsync(ct);
            var now = DateTime.UtcNow;
            var list = reported.Select(p => new ModerationPersonDto(p.UserId, p.UserName, p.Reasons.ToDictionary(x => x.Key, x => x.Value), p.Details, p.LastReportAt,
                p.SuspendedAt != null && (p.SuspendedUntil == null || now < p.SuspendedUntil), p.SuspendedAt, p.SuspendedUntil, p.SuspensionReason)).ToList();
            // Suspended people without open reports stay listed, so the suspension can be lifted.
            foreach (var s in await community.SuspendedPeopleAsync(ct))
                if (list.All(p => p.UserId != s.UserId))
                    list.Add(new ModerationPersonDto(s.UserId, s.UserName, new(), [], null, true, s.SuspendedAt, s.SuspendedUntil, s.SuspensionReason));
            return list;
        }

        public async Task<IReadOnlyList<ModerationPhotoDto>> ModerationPhotosAsync(CancellationToken ct = default)
        {
            var result = new List<ModerationPhotoDto>();
            foreach (var p in await community.ReportedPhotosAsync(ct))
            {
                var entry = await community.GetJournalEntryAsync(p.JournalEntryId, ct);
                var key = JournalPhotoReference.Key(p.PhotoUrl);
                var copies = experiences == null ? [] : await experiences.PhotosOfDiaryPhotoAsync(p.JournalEntryId, p.PhotoUrl, ct);
                result.Add(new ModerationPhotoDto(p.JournalEntryId, p.MatchId, key, $"/MeepleBoard/admin/moderation/photos/{p.JournalEntryId}/{key}", p.OwnerName,
                    p.Reasons.ToDictionary(x => x.Key, x => x.Value), p.Details, p.LastReportAt, entry?.PhotoUrls.Contains(p.PhotoUrl) == true,
                    copies.Any(c => c.IsVisible || c.HiddenReason == PublicationPhoto.AutoHidden && !c.IsModerated),
                    copies.Any(c => c.HiddenReason == PublicationPhoto.AutoHidden && !c.IsModerated)));
            }
            return result;
        }

        public async Task ArchivePersonReportsAsync(Guid administratorId, Guid userId, CancellationToken ct = default)
        {
            if (await community.ResolveUserReportsAsync(userId, administratorId, "archived", ct) == 0) throw new KeyNotFoundException("Não há denúncias abertas sobre esta pessoa.");
        }

        public async Task SuspendAsync(Guid administratorId, Guid userId, SuspendDto dto, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);
            if (userId == administratorId) throw new ArgumentException("Não podes suspender a tua conta.");
            if (dto.Days is not (null or 7 or 30)) throw new ArgumentException("Escolhe 7 dias, 30 dias ou até revisão.");
            var reason = dto.Reason?.Trim() ?? "";
            if (reason.Length == 0) throw new ArgumentException("Indica o motivo da suspensão.");
            if (reason.Length > 300) throw new ArgumentException("Usa no máximo 300 caracteres.");
            DateTime? until = dto.Days is { } days ? DateTime.UtcNow.AddDays(days) : null;
            if (!await community.SuspendAsync(userId, until, reason, administratorId, ct)) throw new KeyNotFoundException("Utilizador não encontrado.");
            await community.ResolveUserReportsAsync(userId, administratorId, "suspended", ct);
            logger.LogInformation("User {UserId} suspended by {AdminId} until {Until}.", userId, administratorId, until?.ToString("O") ?? "review");
        }

        public async Task LiftSuspensionAsync(Guid administratorId, Guid userId, CancellationToken ct = default)
        {
            if (!await community.LiftSuspensionAsync(userId, administratorId, ct)) throw new KeyNotFoundException("Esta conta não está suspensa.");
            logger.LogInformation("Suspension of {UserId} lifted by {AdminId}.", userId, administratorId);
        }

        public async Task<string?> ReportedPhotoUrlAsync(Guid entryId, string photoKey, CancellationToken ct = default)
        {
            var entry = await community.GetJournalEntryAsync(entryId, ct);
            var url = entry == null ? null : PhotoByKey(entry, photoKey);
            return url != null && await community.PhotoHasOpenReportAsync(entryId, url, ct) ? url : null;
        }

        public async Task ModeratePhotoAsync(Guid administratorId, Guid entryId, string photoKey, string decision, CancellationToken ct = default, string? reason = null)
        {
            if (decision is not ("keep" or "withdraw" or "remove")) throw new ArgumentException("Ação inválida.");
            if (decision != "keep" && !PhotoReport.Reasons.Contains(reason ?? "")) throw new ArgumentException("Indica o motivo, que a pessoa vai ver.");
            var entry = await community.GetJournalEntryAsync(entryId, ct) ?? throw new KeyNotFoundException("Fotografia não encontrada.");
            // The reports keep the stored reference, so a photo its owner already removed can still be resolved.
            var reported = (await community.ReportedPhotosAsync(ct)).FirstOrDefault(p => p.JournalEntryId == entryId && JournalPhotoReference.Key(p.PhotoUrl) == photoKey)
                ?? throw new KeyNotFoundException("Não há denúncias abertas sobre esta fotografia.");
            // The community copies follow the decision: «Manter» lifts only an automatic hiding; «Retirar da comunidade» keeps the
            // photo with the players; «Remover da partida» also deletes it from the diary. The author sees the reason on each copy.
            var copies = experiences == null ? [] : await experiences.PhotosOfDiaryPhotoAsync(entryId, reported.PhotoUrl, ct);
            if (decision == "withdraw" && !copies.Any(c => c.RemovedAt == null)) throw new InvalidOperationException("Esta fotografia não está publicada na comunidade.");
            foreach (var copy in copies.Where(c => c.RemovedAt == null))
                if (decision == "keep") copy.Keep(); else copy.Moderate(reason!, administratorId, removed: decision == "remove");
            var removed = decision == "remove" && entry.PhotoUrls.Contains(reported.PhotoUrl);
            if (removed) entry.RemovePhotoUrl(reported.PhotoUrl);
            await community.ResolvePhotoReportsAsync(entryId, reported.PhotoUrl, administratorId, decision switch { "remove" => "removed", "withdraw" => "withdrawn", _ => "kept" }, ct);
            if (!removed) return;
            // Best effort, as when the owner removes it: the diary no longer shows it either way.
            try { await photos.DeleteAsync(reported.PhotoUrl, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete a moderated photo from storage (entry {EntryId}).", entryId); }
        }

        static string? PhotoByKey(MatchJournalEntry entry, string photoKey) =>
            entry.PhotoUrls.FirstOrDefault(u => JournalPhotoReference.IsProtected(u) && JournalPhotoReference.Key(u) == photoKey);

        async Task RequireOtherActiveAccountAsync(Guid userId, Guid targetId, CancellationToken ct)
        {
            if (targetId == Guid.Empty || targetId == userId) throw new ArgumentException("Pedido inválido.");
            if (!await users.IsActiveAsync(targetId, ct)) throw new KeyNotFoundException("Utilizador não encontrado.");
        }
    }
}
