using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace MeepleBoard.Infra.Data.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly MeepleBoardDbContext _context;

        public RefreshTokenRepository(MeepleBoardDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Obtém um Refresh Token pelo seu hash e verifica se ainda é válido.
        /// </summary>
        public async Task<RefreshToken?> GetByTokenAsync(string hashedToken)
        {
            return await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.HashedToken == hashedToken && !rt.IsRevoked && rt.ExpiryDate > DateTime.UtcNow);
        }

        /// <summary>
        /// Adiciona um novo Refresh Token ao banco de dados.
        /// </summary>
        public async Task AddAsync(RefreshToken token)
        {
            await _context.RefreshTokens.AddAsync(token);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Revoga um Refresh Token específico no banco de dados.
        /// </summary>
        public async Task InvalidateTokenAsync(string hashedToken)
        {
            var refreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.HashedToken == hashedToken);
            if (refreshToken != null)
            {
                refreshToken.IsRevoked = true;
                await _context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Revoga todos os Refresh Tokens válidos de um usuário (Logout Global).
        /// </summary>
        public async Task InvalidateAllTokensForUserAsync(Guid userId)
        {
            var tokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == userId && !rt.IsRevoked)
                .ToListAsync();

            foreach (var token in tokens)
            {
                token.IsRevoked = true;
            }

            await _context.SaveChangesAsync();
        }
    }
}