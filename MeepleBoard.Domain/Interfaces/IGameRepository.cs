using MeepleBoard.Domain.Entities;

namespace MeepleBoard.Domain.Interfaces
{
    public interface IGameRepository
    {
        // 🔹 CRUD básico
        Task<Game?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Vários jogos de uma vez, pelos IDs. Usado para montar listas (ex: jogos já jogados).</summary>
        Task<IReadOnlyList<Game>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Game>> GetAllAsync(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default);
        Task AddAsync(Game game, CancellationToken cancellationToken = default);
        Task UpdateAsync(Game game, CancellationToken cancellationToken = default);
        Task DeleteAsync(Game game, CancellationToken cancellationToken = default);
        Task<int> CommitAsync(CancellationToken cancellationToken = default);

        // 🔹 Consultas específicas
        Task<Game?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<Game?> GetGameByBggIdAsync(int bggId, CancellationToken cancellationToken = default);
        Task<bool> ExistsByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<bool> ExistsByBggIdAsync(int bggId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Game>> GetApprovedAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Game>> GetPendingApprovalAsync(CancellationToken cancellationToken = default);

        Task<List<Game>> SearchByNameAsync(string query, int offset, int limit, CancellationToken cancellationToken);
        Task<List<Game>> SearchBaseGamesByNameAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default);
        Task<List<Game>> SearchExpansionsByNameAsync(string query, int offset = 0, int limit = 10, CancellationToken cancellationToken = default);

        Task<List<Game>> GetExpansionsForBaseGameAsync(Guid baseGameId, CancellationToken cancellationToken);
        Task<List<Game>> GetExpansionsWithBaseGameBggIdAsync(int baseGameBggId, CancellationToken cancellationToken = default);

        // 🔥 Utilizados no Job de sincronização com o BGG
        Task<IReadOnlyList<Game>> GetRecentlyPlayedAsync(int limit, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Game>> GetMostSearchedAsync(int limit, CancellationToken cancellationToken = default);

        // 🏆 Ranking interno (MeepleBoardScore) — média das avaliações pessoais (diário) de todas as partidas deste jogo
        Task<double?> GetAveragePersonalRatingAsync(Guid gameId, CancellationToken cancellationToken = default);

        // 🏆 Jogos ordenados pelo ranking interno (MeepleBoardScore), com paginação.
        // Só devolve jogos aprovados e já com pelo menos 1 avaliação (score != null).
        Task<(IReadOnlyList<Game> Items, int TotalCount)> GetRankedByMeepleBoardScoreAsync(
            int pageIndex, int pageSize, CancellationToken cancellationToken = default);

        // 🏆 Ranking PESSOAL: só os jogos que ESTE utilizador avaliou, ordenados pela
        // média das SUAS PRÓPRIAS avaliações (não mistura com as dos outros jogadores).
        Task<(IReadOnlyList<(Game Game, double Rating)> Items, int TotalCount)> GetPersonalRankingsAsync(
            Guid userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

        // 🏆 Jogos ordenados pela nota média do BGG (averageRating), com paginação.
        Task<(IReadOnlyList<Game> Items, int TotalCount)> GetRankedByBggRatingAsync(
            int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    }
}