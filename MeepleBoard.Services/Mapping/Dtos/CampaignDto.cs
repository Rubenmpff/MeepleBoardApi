using System.ComponentModel.DataAnnotations;
using MeepleBoard.Services.DTOs.MatchJournal;

namespace MeepleBoard.Services.DTOs.Campaign
{
    // ── Campaign ──────────────────────────────────────────────────────────────

    public class CampaignDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid GameId { get; set; }
        public string? GameName { get; set; }
        public string? GameImageUrl { get; set; }
        public Guid CreatorId { get; set; }
        public string? CreatorUserName { get; set; }
        public string Status { get; set; } = "Active";
        public string? Notes { get; set; }
        public int MemberCount { get; set; }
        public int MatchCount { get; set; }
        public double? AveragePersonalRating { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<CampaignMemberDto> Members { get; set; } = new();
        public List<CampaignMatchDto> Matches { get; set; } = new();
    }

    public class CreateCampaignDto
    {
        [Required]
        [MinLength(2)]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public Guid GameId { get; set; }

        public string? Notes { get; set; }
    }

    public class UpdateCampaignDto
    {
        [Required]
        [MinLength(2)]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Notes { get; set; }
    }

    // ── CampaignMember ────────────────────────────────────────────────────────

    public class CampaignMemberDto
    {
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public bool IsCreator { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime InvitedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
    }

    // ── CampaignMatch ─────────────────────────────────────────────────────────

    public class CampaignMatchDto
    {
        public Guid Id { get; set; }
        public Guid MatchId { get; set; }
        public string? GameName { get; set; }
        public DateTime MatchDate { get; set; }
        public int? SessionNumber { get; set; }
        public string? SessionTitle { get; set; }
        public bool IsSoloGame { get; set; }
        public int? DurationInMinutes { get; set; }
        public string? Location { get; set; }
        public List<JournalEntryDto> JournalEntries { get; set; } = new();
    }

    public class AddMatchToCampaignDto
    {
        [Required]
        public Guid MatchId { get; set; }

        public int? SessionNumber { get; set; }

        [MaxLength(200)]
        public string? SessionTitle { get; set; }
    }

    // ── Invite ────────────────────────────────────────────────────────────────

    public class InviteMemberDto
    {
        [Required]
        public Guid UserId { get; set; }
    }
}