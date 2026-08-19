using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs.MatchJournal
{
    // ── MatchJournalEntry ────────────────────────────────────────────────────
    //
    // Nota: apesar de viverem ao lado dos DTOs de campanha por razões
    // históricas, estes DTOs servem o diário de QUALQUER partida — rápida,
    // de sessão, ou de campanha. A entidade MatchJournalEntry e o respetivo
    // CRUD continuam, por agora, em ICampaignRepository/CampaignService
    // (não fazem parte deste passo de arrumação).

    public class JournalEntryDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int? PersonalRating { get; set; }
        public string? Notes { get; set; }
        public string? Tags { get; set; }
        public List<string> PhotoUrls { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpsertJournalEntryDto
    {
        [Range(0, 10, ErrorMessage = "O rating deve ser entre 0 e 10.")]
        public int? PersonalRating { get; set; }

        [MaxLength(3000)]
        public string? Notes { get; set; }

        [MaxLength(500)]
        public string? Tags { get; set; }
    }
}