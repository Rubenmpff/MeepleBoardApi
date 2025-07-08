using MeepleBoard.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

public class UserCleanupJob
{
    private readonly UserManager<User> _userManager;
    private readonly ILogger<UserCleanupJob> _logger;

    public UserCleanupJob(UserManager<User> userManager, ILogger<UserCleanupJob> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var threshold = DateTime.UtcNow.AddDays(-7);
        var users = _userManager.Users
            .Where(u => !u.EmailConfirmed && u.CreatedAt < threshold)
            .ToList();

        _logger.LogInformation($"🔎 Encontrados {users.Count} usuários não confirmados para remoção.");

        foreach (var user in users)
        {
            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
                _logger.LogInformation($"🗑️ Usuário {user.Email} removido.");
            else
                _logger.LogWarning($"⚠️ Falha ao remover {user.Email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }
}