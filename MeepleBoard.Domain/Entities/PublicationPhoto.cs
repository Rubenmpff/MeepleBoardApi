using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// One of the author's own diary photos of the match, published with their experience (approved 18/10/2026). At most
    /// <see cref="MaxPerPublication"/>; nothing gets here automatically. Withdrawing it (or everything) leaves the photo in the
    /// diary, shared with the match's players; deleting it from the diary removes it from here. The public route uses only
    /// <see cref="Id"/> and the publication id — never the match, the diary entry or <see cref="PhotoUrl"/>, which stays on the server.
    /// Hidden by moderation (withdrawn or removed from the match, with a reason the author sees) or automatically after reports;
    /// the author cannot undo a moderation decision.
    /// </summary>
    public class PublicationPhoto
    {
        public const int MaxPerPublication = 3;
        public const string AutoHidden = "AutoReports";
        /// <summary>Distinct people with an open report that hide a public photo from the community until it is reviewed.</summary>
        public const int AutoHideThreshold = 3;
        private PublicationPhoto() { }

        public PublicationPhoto(Guid publicationId, string photoUrl, int position)
        {
            if (publicationId == Guid.Empty || string.IsNullOrWhiteSpace(photoUrl)) throw new ArgumentException("Fotografia inválida.");
            Id = Guid.NewGuid(); PublicationId = publicationId; PhotoUrl = photoUrl; Position = position; PublishedAt = DateTime.UtcNow;
        }

        public Guid Id { get; private set; }
        public Guid PublicationId { get; private set; }
        public virtual MatchExperiencePublication? Publication { get; private set; }
        /// <summary>The diary photo's stored reference (server only).</summary>
        [MaxLength(500)] public string PhotoUrl { get; private set; } = string.Empty;
        public int Position { get; private set; }
        public DateTime PublishedAt { get; private set; }
        public DateTime? HiddenAt { get; private set; }
        [MaxLength(32)] public string? HiddenReason { get; private set; }
        public Guid? HiddenById { get; private set; }
        /// <summary>Set when moderation removed the photo from the match (diary included); the row stays only as the author's notice.</summary>
        public DateTime? RemovedAt { get; private set; }

        public bool IsVisible => HiddenAt == null;
        /// <summary>Hidden by an administrator: the author's choices no longer change it.</summary>
        public bool IsModerated => HiddenById != null;

        public void MoveTo(int position) => Position = position;

        public void HideAutomatically() { if (HiddenAt != null) return; HiddenAt = DateTime.UtcNow; HiddenReason = AutoHidden; }

        /// <summary>«Retirar da comunidade» (the photo stays shared with the players) or, with <paramref name="removed"/>, «Remover da partida».</summary>
        public void Moderate(string reason, Guid administratorId, bool removed)
        {
            if (!PhotoReport.Reasons.Contains(reason)) throw new ArgumentException("Motivo inválido.");
            HiddenAt = DateTime.UtcNow; HiddenReason = reason; HiddenById = administratorId;
            if (removed) RemovedAt = HiddenAt;
        }

        /// <summary>«Manter»: lifts only an automatic hiding; a decision by an administrator stays.</summary>
        public void Keep() { if (HiddenReason == AutoHidden && HiddenById == null) { HiddenAt = null; HiddenReason = null; } }
    }
}
