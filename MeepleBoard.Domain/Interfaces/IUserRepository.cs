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

        // 🔹 Remove um usuário pelo ID (agora retorna número de registros afetados)
        Task<int> DeleteAsync(Guid userId, CancellationToken cancellationToken = default);

        // 🔹 Salva as mudanças no banco
        Task<int> CommitAsync(CancellationToken cancellationToken = default);
    }
}