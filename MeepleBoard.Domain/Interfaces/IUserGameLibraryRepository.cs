using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IUserGameLibraryRepository
    {
        // 🔹 Verifica se um jogo já está na biblioteca do usuário
        Task<bool> ExistsAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default);

        // 🔹 Obtém todos os jogos da biblioteca de um usuário
        Task<IReadOnlyList<UserGameLibrary>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

        // 🔹 Obtém um jogo específico na biblioteca do usuário
        Task<UserGameLibrary?> GetByUserAndGameAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default);

        // 💰 Obtém o total gasto pelo usuário na coleção
        Task<decimal> GetTotalAmountSpentByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        // 🔹 Obtém o total de jogos possuídos pelo usuário
        Task<int> GetTotalGamesOwnedByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        // ⏱️ Obtém o total de horas jogadas pelo usuário
        Task<int> GetTotalHoursPlayedByUserAsync(Guid userId, CancellationToken cancellationToken = default);

        // 🔹 Obtém os jogos mais jogados pelo usuário (Top N)
        Task<IReadOnlyDictionary<string, int>> GetMostPlayedGamesByUserAsync(Guid userId, int topN, CancellationToken cancellationToken = default);

        // 🔹 Adiciona um jogo à biblioteca do usuário
        Task AddAsync(UserGameLibrary userGameLibrary, CancellationToken cancellationToken = default);

        // 🔹 Atualiza um jogo da biblioteca do usuário
        Task UpdateAsync(UserGameLibrary userGameLibrary, CancellationToken cancellationToken = default);

        // 🔹 Remove um jogo da biblioteca do usuário pelo ID do jogo e do usuário
        Task RemoveAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default);

        // 🔹 Salva as mudanças no banco
        Task<int> CommitAsync(CancellationToken cancellationToken = default);
    }
}