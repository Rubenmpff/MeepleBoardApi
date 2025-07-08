using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class UserGameLibraryRepository : IUserGameLibraryRepository
    {
        private readonly MeepleBoardDbContext _context;

        public UserGameLibraryRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        // 🔹 Verifica se um jogo já está na biblioteca do usuário
        public async Task<bool> ExistsAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default)
        {
            return await _context.UserGameLibraries
                .AsNoTracking()
                .AnyAsync(ugl => ugl.UserId == userId && ugl.GameId == gameId, cancellationToken);
        }

        // 🔹 Obtém todos os jogos da biblioteca de um usuário
        public async Task<IReadOnlyList<UserGameLibrary>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.UserGameLibraries
                .Where(ugl => ugl.UserId == userId)
                .Include(ugl => ugl.Game!)
                .AsSplitQuery()
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        // 🔹 Obtém um jogo específico na biblioteca do usuário
        public async Task<UserGameLibrary?> GetByUserAndGameAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default)
        {
            return await _context.UserGameLibraries
                .AsNoTracking()
                .FirstOrDefaultAsync(ugl => ugl.UserId == userId && ugl.GameId == gameId, cancellationToken);
        }

        // 💰 Obtém o total gasto pelo usuário na coleção
        public async Task<decimal> GetTotalAmountSpentByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.UserGameLibraries
                .Where(ugl => ugl.UserId == userId && ugl.PricePaid.HasValue)
                .SumAsync(ugl => ugl.PricePaid ?? 0, cancellationToken);
        }

        // 🔹 Obtém o total de jogos possuídos pelo usuário
        public async Task<int> GetTotalGamesOwnedByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.UserGameLibraries
                .AsNoTracking()
                .CountAsync(ugl => ugl.UserId == userId && ugl.Status == GameLibraryStatus.Owned, cancellationToken);
        }

        // ⏱️ Obtém o total de horas jogadas pelo usuário
        public async Task<int> GetTotalHoursPlayedByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.UserGameLibraries
                .AsNoTracking()
                .Where(ugl => ugl.UserId == userId)
                .SumAsync(ugl => ugl.TotalHoursPlayed, cancellationToken);
        }

        // 🔹 Obtém os jogos mais jogados pelo usuário (Top N)
        public async Task<IReadOnlyDictionary<string, int>> GetMostPlayedGamesByUserAsync(Guid userId, int topN, CancellationToken cancellationToken = default)
        {
            var result = await _context.UserGameLibraries
                .Where(ugl => ugl.UserId == userId && ugl.Game != null)
                .Include(ugl => ugl.Game!)
                .GroupBy(ugl => ugl.Game!.Name)
                .Select(g => new { GameName = g.Key, TimesPlayed = g.Sum(ugl => ugl.TotalTimesPlayed) })
                .OrderByDescending(g => g.TimesPlayed)
                .Take(topN)
                .ToListAsync(cancellationToken);

            return result.ToDictionary(r => r.GameName, r => r.TimesPlayed);
        }

        // 🔹 Adiciona um jogo à biblioteca do usuário
        public async Task AddAsync(UserGameLibrary userGameLibrary, CancellationToken cancellationToken = default)
        {
            await _context.UserGameLibraries.AddAsync(userGameLibrary, cancellationToken);
        }

        // 🔹 Atualiza um jogo da biblioteca do usuário
        public async Task UpdateAsync(UserGameLibrary userGameLibrary, CancellationToken cancellationToken = default)
        {
            var existingLibrary = await _context.UserGameLibraries.FindAsync(new object[] { userGameLibrary.Id }, cancellationToken);
            if (existingLibrary != null)
            {
                _context.Entry(existingLibrary).CurrentValues.SetValues(userGameLibrary);
            }
        }

        // 🔹 Remove um jogo da biblioteca do usuário pelo ID do jogo e do usuário
        public async Task RemoveAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default)
        {
            var entity = await _context.UserGameLibraries
                .FirstOrDefaultAsync(ugl => ugl.UserId == userId && ugl.GameId == gameId, cancellationToken);

            if (entity != null)
            {
                _context.UserGameLibraries.Remove(entity);
            }
        }

        // 🔹 Salva as mudanças no banco
        public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
    }
}