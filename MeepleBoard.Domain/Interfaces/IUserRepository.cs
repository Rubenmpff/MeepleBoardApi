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
        /// Deletes an account by anonymising it, all or nothing in one transaction: personal data, sign-in, library,
        /// friendships, pending invitations and the person's diary (notes, photos, ratings, publications) go; shared
        /// matches, sessions and campaigns keep the anonymised player. Games whose MeepleBoard rating counted the
        /// person are recalculated inside the same transaction.
        /// </summary>
        Task<AccountDeletionResult> AnonymizeAsync(Guid userId, string deletedUserName, CancellationToken cancellationToken = default);

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

    /// <summary>What an account deletion left to do outside the database: photos to remove from storage.</summary>
    public sealed record AccountDeletionResult(IReadOnlyList<Guid> RecalculatedGameIds, IReadOnlyList<string> PhotoUrls);
}