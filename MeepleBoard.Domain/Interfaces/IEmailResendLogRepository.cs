using MeepleBoard.Domain.Entities;

public interface IEmailResendLogRepository
{
    Task AddAsync(EmailResendLog log);

    Task<int> CountResendsTodayAsync(Guid userId, string reason);

    Task<DateTime?> GetLastResendTimeAsync(Guid userId, string reason);
}