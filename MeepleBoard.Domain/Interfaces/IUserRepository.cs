using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IUserRepository
    {
        // 🔹 Verifica se um e-mail já está em uso
        Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

        // 🔹 Verifica se um nome de usuário já está em uso
        Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default);

        // 🔹 Obtém um usuário pelo ID
        Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        // 🔹 Obtém um usuário pelo e-mail (Case-insensitive)
        Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

        // 🔹 Obtém um usuário pelo nome de usuário (Case-insensitive)
        Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

        // 🔹 Obtém todos os usuários registrados
        Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default);

        // 🔹 Obtém os usuários com mais vitórias (ranking interno, com opção de filtro por período)
        Task<IReadOnlyList<User>> GetUsersWithMostWinsAsync(int count, DateTime? startDate = null, CancellationToken cancellationToken = default);

        // 🔹 Adiciona um novo usuário
        Task AddAsync(User user, CancellationToken cancellationToken = default);

        // 🔹 Atualiza os dados de um usuário (agora retorna o número de registros afetados)
        Task<int> UpdateAsync(User user, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes an account by anonymising it, all or nothing in one transaction (docs/proposta-eliminacao-conta.md in the app):
        /// personal data, sign-in, library, friendships, pending invitations and the person's diary (notes, photos, ratings,
        /// publications) go; matches nobody else active played go; shared matches keep the anonymised player and their
        /// structured data but lose the person's free text; sessions they organize are cancelled or ended, with a neutral
        /// title and no place; campaigns they created go when nobody else is active, otherwise stay under the chosen new
        /// manager (or with management read-only) with a neutral title. Recalculates the games whose rating counted.
        /// Throws ArgumentException for a new manager who is not an active member of a campaign the person created.
        /// </summary>
        Task<AccountDeletionResult> AnonymizeAsync(Guid userId, string deletedUserName, IReadOnlyDictionary<Guid, Guid>? campaignTransfers = null, CancellationToken cancellationToken = default);

        /// <summary>What deleting the account would affect, for the confirmation screen; null for an unknown or deleted account.</summary>
        Task<AccountDeletionSummary?> GetDeletionSummaryAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Clears the free text of reports made by accounts deleted on or before the given time (retention limit).</summary>
        Task<int> ClearDeletedReporterTextAsync(DateTime deletedBefore, IReadOnlyCollection<Guid>? reporters = null, CancellationToken cancellationToken = default);

        // 🔹 Salva as mudanças no banco
        Task<int> CommitAsync(CancellationToken cancellationToken = default);

        // 🔹 Atualiza a última atividade do utilizador (indicador "online" nos Amigos).
        // Não carrega a entidade — é um UPDATE direto, só quando o valor está desatualizado.
        Task TouchLastActiveAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Whether the account exists and was not deleted: checked on every authenticated request.</summary>
        Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Only the duration-sharing preference (false for an unknown account), without loading the account.</summary>
        Task<bool> ContributesDurationStatsAsync(Guid userId, CancellationToken cancellationToken = default);
    }

    /// <summary>What an account deletion left to do outside the database: photos to remove from storage and the push
    /// tokens of guests whose session was cancelled.</summary>
    public sealed record AccountDeletionResult(IReadOnlyList<Guid> RecalculatedGameIds, IReadOnlyList<string> PhotoUrls, IReadOnlyList<string> CancelledSessionGuestTokens);

    public sealed record AccountDeletionMember(Guid Id, string UserName);
    public sealed record AccountDeletionCampaign(Guid Id, string Name, string? GameName, bool HasNotes, IReadOnlyList<AccountDeletionMember> Members);
    public sealed record AccountDeletionSummary(int ScheduledSessions, int RunningSessions, IReadOnlyList<AccountDeletionCampaign> CampaignsWithMembers, int CampaignsOnlyYours);
}