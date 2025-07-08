using MeepleBoard.Domain.Entities;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

public class EmailResendLogRepository : IEmailResendLogRepository
{
    private readonly MeepleBoardDbContext _context;

    public EmailResendLogRepository(MeepleBoardDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(EmailResendLog log)
    {
        await _context.Set<EmailResendLog>().AddAsync(log);
        await _context.SaveChangesAsync();
    }

    public async Task<int> CountResendsTodayAsync(Guid userId, string reason)
    {
        var today = DateTime.UtcNow.Date;
        return await _context.Set<EmailResendLog>()
            .CountAsync(l => l.UserId == userId && l.Reason == reason && l.SentAt.Date == today);
    }

    public async Task<DateTime?> GetLastResendTimeAsync(Guid userId, string reason)
    {
        return await _context.Set<EmailResendLog>()
            .Where(l => l.UserId == userId && l.Reason == reason)
            .OrderByDescending(l => l.SentAt)
            .Select(l => (DateTime?)l.SentAt)
            .FirstOrDefaultAsync();
    }
}