using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

public class UserCleanupJob
{
    private readonly UserManager<User> _userManager;
    private readonly ILogger<UserCleanupJob> _logger;
    private readonly IUserRepository? _users;
    // Free text of reports made by deleted accounts is kept at most this long, even while the report is open.
    public static readonly TimeSpan DeletedReporterTextRetention = TimeSpan.FromDays(90);

    public UserCleanupJob(UserManager<User> userManager, ILogger<UserCleanupJob> logger, IUserRepository? users = null)
    {
        _userManager = userManager;
        _logger = logger;
        _users = users;
    }

    public async Task ExecuteAsync()
    {
        if (_users != null)
        {
            var cleared = await _users.ClearDeletedReporterTextAsync(DateTime.UtcNow - DeletedReporterTextRetention);
            if (cleared > 0) _logger.LogInformation("Texto de {Count} denúncias de contas eliminadas apagado (limite de conservação).", cleared);
        }
        var threshold = DateTime.UtcNow.AddDays(-7);
        var users = _userManager.Users
            // Deleted accounts stay anonymised (shared matches keep their players); they are never removed here.
            .Where(u => !u.EmailConfirmed && u.CreatedAt < threshold && u.DeletedAt == null)
            .ToList();

        _logger.LogInformation($"🔎 Encontrados {users.Count} usuários não confirmados para remoção.");

        foreach (var user in users)
        {
            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
                _logger.LogInformation("🗑️ Conta não confirmada {UserId} removida.", user.Id);
            else
                _logger.LogWarning($"⚠️ Falha ao remover a conta {user.Id}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }
}