using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Ciclo de vida de uma sessão:
    ///
    ///   Criada → Upcoming → Active → Closed
    ///                  ↘ Cancelled (apagada pelo job de limpeza)
    ///
    /// Regras:
    ///   - ResponseDeadline: data limite para os convidados responderem.
    ///     Se não for definida, usa-se ScheduledStartDate como limite.
    ///   - Se chegar ao ResponseDeadline com menos de 1 aceitação (além do organizer),
    ///     o SessionCleanupJob cancela e apaga a sessão.
    ///   - Organizer pode cancelar manualmente enquanto Upcoming.
    ///   - Sessões canceladas são apagadas da BD (sem histórico).
    /// </summary>
    public class GameSession
    {
        [Key]
        public Guid Id { get; private set; }

        [Required, MaxLength(200)]
        public string Name { get; private set; } = string.Empty;

        [Required]
        public Guid OrganizerId { get; private set; }
        public virtual User Organizer { get; private set; } = null!;

        [MaxLength(200)]
        public string? Location { get; private set; }

        /// <summary>
        /// Data/hora planeada para começar (UTC).
        /// </summary>
        [Required]
        public DateTime ScheduledStartDate { get; private set; }

        /// <summary>
        /// Data limite para os convidados responderem ao convite.
        /// Se null, usa ScheduledStartDate como limite.
        /// O SessionCleanupJob cancela a sessão se chegar aqui
        /// com menos de 1 aceitação além do organizer.
        /// </summary>
        public DateTime? ResponseDeadline { get; private set; }

        /// <summary>
        /// Data de criação da sessão.
        /// </summary>
        public DateTime StartDate { get; private set; } = DateTime.UtcNow;

        /// <summary>
        /// Data de encerramento (Closed ou Cancelled).
        /// </summary>
        public DateTime? EndDate { get; private set; }

        /// <summary>
        /// True se a sessão foi cancelada (manualmente ou por falta de participantes).
        /// </summary>
        public bool IsCancelled { get; private set; } = false;

        public virtual ICollection<GameSessionPlayer> Players { get; private set; }
            = new HashSet<GameSessionPlayer>();

        public virtual ICollection<Match> Matches { get; private set; }
            = new HashSet<Match>();

        // ── Status calculado ───────────────────────────────────────────────────

        /// <summary>
        /// Status da sessão calculado com base no estado atual.
        ///   Cancelled → "Cancelled"
        ///   EndDate != null → "Closed"
        ///   UtcNow >= ScheduledStartDate E pelo menos 1 convidado aceite → "Active"
        ///   caso contrário → "Upcoming"
        ///
        ///   ⚠️ Uma sessão nunca fica "Active" sozinha (só com o organizer).
        ///   Se a hora chegar sem nenhum convidado ter aceite, mantém-se "Upcoming"
        ///   até o SessionCleanupJob a cancelar (ver ShouldAutoCancelNow).
        /// </summary>
        public string Status
        {
            get
            {
                if (IsCancelled) return "Cancelled";
                if (EndDate != null) return "Closed";
                if (DateTime.UtcNow >= ScheduledStartDate && AcceptedGuestCount >= 1) return "Active";
                return "Upcoming";
            }
        }

        /// <summary>
        /// A sessão está ativa quando chegou a hora marcada e ainda não foi encerrada/cancelada.
        /// </summary>
        public bool IsActive => Status == "Active";

        /// <summary>
        /// Data efectiva de deadline — ResponseDeadline se definida, senão ScheduledStartDate.
        /// </summary>
        public DateTime EffectiveDeadline => ResponseDeadline ?? ScheduledStartDate;

        /// <summary>
        /// Número de aceitações além do organizer.
        /// Usado pelo job para decidir se cancela automaticamente.
        /// </summary>
        public int AcceptedGuestCount => Players
            .Count(p => !p.IsOrganizer && p.Status == Domain.Enums.GameSessionInviteStatus.Accepted);

        // ── Construtores ───────────────────────────────────────────────────────

        // EF Core usa este construtor privado para materialização
        private GameSession() { }

        /// <summary>
        /// Cria uma nova sessão de jogo.
        /// </summary>
        /// <param name="name">Nome da sessão (obrigatório, mín. 3 chars).</param>
        /// <param name="organizerId">ID do organizador.</param>
        /// <param name="location">Local opcional.</param>
        /// <param name="scheduledStartDateUtc">Data/hora planeada (UTC).</param>
        /// <param name="responseDeadlineUtc">
        ///   Data limite para os convidados responderem (UTC).
        ///   Se null, usa ScheduledStartDate como limite.
        ///   Tem de ser anterior a ScheduledStartDate.
        /// </param>
        public GameSession(
            string name,
            Guid organizerId,
            string? location = null,
            DateTime? scheduledStartDateUtc = null,
            DateTime? responseDeadlineUtc = null)
        {
            if (organizerId == Guid.Empty)
                throw new ArgumentException("OrganizerId inválido.", nameof(organizerId));

            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3)
                throw new ArgumentException("O nome da sessão deve ter pelo menos 3 caracteres.", nameof(name));

            Id = Guid.NewGuid();
            Name = name.Trim();
            OrganizerId = organizerId;
            Location = location?.Trim();
            StartDate = DateTime.UtcNow;

            var sched = scheduledStartDateUtc?.ToUniversalTime() ?? StartDate;

            if (sched < StartDate.AddMinutes(-1))
                throw new ArgumentException("A data marcada não pode estar no passado.", nameof(scheduledStartDateUtc));

            ScheduledStartDate = sched;

            // ResponseDeadline: tem de ser antes de ScheduledStartDate
            if (responseDeadlineUtc.HasValue)
            {
                var deadline = responseDeadlineUtc.Value.ToUniversalTime();

                if (deadline < StartDate.AddMinutes(-1))
                    throw new ArgumentException("O prazo de resposta não pode estar no passado.", nameof(responseDeadlineUtc));

                if (deadline > ScheduledStartDate)
                    throw new ArgumentException("O prazo de resposta tem de ser antes da data da sessão.", nameof(responseDeadlineUtc));

                ResponseDeadline = deadline;
            }
        }

        // ── Métodos de negócio ─────────────────────────────────────────────────

        /// <summary>
        /// Encerra a sessão com sucesso (Closed).
        /// Só pode ser chamado quando a sessão está Active.
        /// </summary>
        public void CloseSession()
        {
            if (IsCancelled)
                throw new InvalidOperationException("Sessão já foi cancelada.");

            if (EndDate != null)
                return; // já encerrada, idempotente

            EndDate = DateTime.UtcNow;
        }

        /// <summary>
        /// Cancela a sessão (manualmente pelo organizer ou automaticamente pelo job).
        /// Sessões canceladas são apagadas pelo SessionCleanupJob.
        /// </summary>
        public void CancelSession()
        {
            if (EndDate != null && !IsCancelled)
                throw new InvalidOperationException("Sessão já foi encerrada e não pode ser cancelada.");

            if (IsCancelled)
                return; // idempotente

            if (Status == "Active")
                throw new InvalidOperationException("Não podes cancelar uma sessão que já está a decorrer. Usa CloseSession().");

            IsCancelled = true;
            EndDate = DateTime.UtcNow;
        }

        /// <summary>
        /// Reagenda a sessão (só quando Upcoming).
        /// </summary>
        public void Reschedule(DateTime newScheduledStartDateUtc, DateTime? newResponseDeadlineUtc = null)
        {
            if (IsCancelled)
                throw new InvalidOperationException("Sessão cancelada não pode ser reagendada.");

            if (EndDate != null)
                throw new InvalidOperationException("Sessão encerrada não pode ser reagendada.");

            if (DateTime.UtcNow >= ScheduledStartDate)
                throw new InvalidOperationException("Sessão já começou, não pode ser reagendada.");

            var utc = newScheduledStartDateUtc.ToUniversalTime();

            if (utc < DateTime.UtcNow.AddMinutes(-1))
                throw new ArgumentException("A nova data marcada não pode estar no passado.");

            ScheduledStartDate = utc;

            if (newResponseDeadlineUtc.HasValue)
            {
                var deadline = newResponseDeadlineUtc.Value.ToUniversalTime();
                if (deadline > ScheduledStartDate)
                    throw new ArgumentException("O prazo de resposta tem de ser antes da data da sessão.");
                ResponseDeadline = deadline;
            }
        }

        /// <summary>
        /// Atualiza o local da sessão.
        /// </summary>
        public void UpdateLocation(string? location)
        {
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        }

        /// <summary>
        /// Helper — só permite registar partidas quando Active.
        /// </summary>
        public bool CanRegisterMatches() => IsActive;

        /// <summary>
        /// Verifica se a sessão deve ser cancelada automaticamente pelo job.
        /// Condições:
        ///   - Ainda não começou (Upcoming)
        ///   - Passou o EffectiveDeadline
        ///   - Menos de 1 aceitação além do organizer
        /// </summary>
        public bool ShouldAutoCancelNow()
        {
            return Status == "Upcoming"
                && DateTime.UtcNow >= EffectiveDeadline
                && AcceptedGuestCount < 1;
        }
    }
}