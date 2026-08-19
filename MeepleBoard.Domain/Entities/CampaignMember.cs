using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Representa a relação entre um utilizador e uma campanha.
    ///
    /// Ciclo de vida do convite:
    ///   Pending → Accepted (membro ativo)
    ///           → Declined (recusou, pode ser re-convidado)
    ///   Accepted → Removed (removido pelo criador, pode ser re-convidado)
    ///            → Left    (saiu voluntariamente, pode ser re-convidado)
    ///
    /// Regras:
    ///   - Só vê a campanha depois de aceitar
    ///   - Qualquer membro ativo pode editar notas e adicionar entradas de diário
    ///   - O criador não pode ser removido
    ///   - Alguém removido/que saiu pode ser re-convidado (novo registo)
    /// </summary>
    public class CampaignMember
    {
        private CampaignMember() { }

        public CampaignMember(Guid campaignId, Guid userId, bool isCreator = false)
        {
            if (campaignId == Guid.Empty)
                throw new ArgumentException("ID da campanha inválido.");
            if (userId == Guid.Empty)
                throw new ArgumentException("ID do utilizador inválido.");

            Id = Guid.NewGuid();
            CampaignId = campaignId;
            UserId = userId;
            IsCreator = isCreator;

            // Criador entra automaticamente como aceite
            Status = isCreator
                ? CampaignMemberStatus.Accepted
                : CampaignMemberStatus.Pending;

            InvitedAt = DateTime.UtcNow;
            RespondedAt = isCreator ? DateTime.UtcNow : null;
        }

        [Key]
        public Guid Id { get; private set; }

        [Required]
        public Guid CampaignId { get; private set; }

        [ForeignKey("CampaignId")]
        public virtual Campaign? Campaign { get; private set; }

        [Required]
        public Guid UserId { get; private set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; private set; }

        /// <summary>True apenas para o criador da campanha — não pode ser removido.</summary>
        public bool IsCreator { get; private set; }

        public CampaignMemberStatus Status { get; private set; }

        public DateTime InvitedAt { get; private set; }
        public DateTime? RespondedAt { get; private set; }
        public DateTime? RemovedAt { get; private set; }

        // ── Métodos ───────────────────────────────────────────────────────────

        public void Accept()
        {
            if (Status == CampaignMemberStatus.Accepted) return;
            Status = CampaignMemberStatus.Accepted;
            RespondedAt = DateTime.UtcNow;
        }

        public void Decline()
        {
            if (Status == CampaignMemberStatus.Declined) return;
            Status = CampaignMemberStatus.Declined;
            RespondedAt = DateTime.UtcNow;
        }

        public void Remove()
        {
            if (IsCreator)
                throw new InvalidOperationException("O criador da campanha não pode ser removido.");
            Status = CampaignMemberStatus.Removed;
            RemovedAt = DateTime.UtcNow;
        }

        public void Leave()
        {
            if (IsCreator)
                throw new InvalidOperationException("O criador não pode sair da campanha. Usa Abandon() na campanha.");
            Status = CampaignMemberStatus.Left;
            RemovedAt = DateTime.UtcNow;
        }
    }

    public enum CampaignMemberStatus
    {
        Pending = 0,  // convidado, ainda não respondeu
        Accepted = 1,  // membro ativo
        Declined = 2,  // recusou
        Removed = 3,  // removido pelo criador
        Left = 4   // saiu voluntariamente
    }
}