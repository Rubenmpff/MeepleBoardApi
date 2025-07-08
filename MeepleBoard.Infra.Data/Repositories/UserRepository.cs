using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly MeepleBoardDbContext _context;

        public UserRepository(MeepleBoardDbContext context)
        {
            _context = context;
        }

        // 🔹 Verifica se um e-mail já está em uso
        public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;

            return await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Email != null && u.Email.ToLower().Trim() == email.ToLower().Trim(), cancellationToken);
        }

        // 🔹 Verifica se um nome de usuário já está em uso
        public async Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;

            return await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.UserName != null && u.UserName.ToLower().Trim() == username.ToLower().Trim(), cancellationToken);
        }

        // 🔹 Obtém um usuário pelo ID (inclui partidas do usuário)
        public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Users
                .Include(u => u.Matches)
                    .ThenInclude(m => m.Match) // 🔹 Carrega os detalhes das partidas
                .AsSplitQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        }

        // 🔹 Obtém um usuário pelo e-mail (Case-insensitive corrigido)
        public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;

            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Email != null && user.Email.ToLower().Trim() == email.ToLower().Trim(), cancellationToken);
        }

        // 🔹 Obtém um usuário pelo nome de usuário (Case-insensitive corrigido)
        public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserName != null && u.UserName.ToLower().Trim() == username.ToLower().Trim(), cancellationToken);
        }

        // 🔹 Retorna todos os usuários registrados
        public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Users
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        // 🔹 Retorna os usuários com mais vitórias
        public async Task<IReadOnlyList<User>> GetUsersWithMostWinsAsync(int count, DateTime? startDate = null, CancellationToken cancellationToken = default)
        {
            var query = _context.MatchPlayers
                .Where(mp => mp.IsWinner && (!startDate.HasValue || mp.Match!.MatchDate >= startDate.Value))
                .GroupBy(mp => mp.UserId)
                .Select(g => new { UserId = g.Key, TotalWins = g.Count() })
                .OrderByDescending(u => u.TotalWins)
                .Take(count)
                .Join(_context.Users, stat => stat.UserId, u => u.Id, (stat, u) => new { u, stat.TotalWins })
                .OrderByDescending(u => u.TotalWins)
                .Select(u => u.u) // 🔹 Retorna apenas o usuário
                .AsNoTracking();

            return await query.ToListAsync(cancellationToken);
        }

        // 🔹 Adiciona um novo usuário (sem SaveChanges)
        public async Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            await _context.Users.AddAsync(user, cancellationToken);
        }

        // 🔹 Atualiza os dados de um usuário (agora retorna número de registros afetados)
        public async Task<int> UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            var existingUser = await _context.Users.FindAsync(new object[] { user.Id }, cancellationToken);
            if (existingUser == null) return 0;

            _context.Entry(existingUser).CurrentValues.SetValues(user);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        // 🔹 Remove um usuário pelo ID (agora retorna número de registros afetados)
        public async Task<int> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var user = await _context.Users.FindAsync(new object[] { id }, cancellationToken);
            if (user == null) return 0;

            _context.Users.Remove(user);
            return await _context.SaveChangesAsync(cancellationToken);
        }

        // 🔹 Salva as mudanças no banco
        public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
    }
}