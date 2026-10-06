using System.Security.Cryptography;
using System.Text.Json;
using AutoMapper;
using MeepleBoard.CrossCutting.IoC;
using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Infra.Data.Context;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Settings;
using MeepleBoardApi.Controllers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

// Deliberately ignores appsettings, environment connection strings and user secrets.
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = "DeviceTests", Args = [] });
builder.WebHost.UseKestrel().UseUrls("http://0.0.0.0:5099");
builder.Services.AddLogging(log => log.AddConsole().SetMinimumLevel(LogLevel.Warning));
var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
// The launcher supplies a data directory, never a connection string.
var dataPath = args.FirstOrDefault(a => a.StartsWith("--data="))?[7..] ?? Path.Combine(repository, ".device-tests");
dataPath = Path.GetFullPath(dataPath);
Directory.CreateDirectory(dataPath);
var connection = DeviceTestSql.Resolve(args.Contains("--audit-only"));
if (args.Contains("--sql-probe")) { await DeviceTestSql.Probe(connection); return; }
var sqlCheck = args.FirstOrDefault(a => a.StartsWith("--verify-sql="));
if (sqlCheck != null) { await DeviceSqlChecks.Run(connection, dataPath, sqlCheck[13..]); return; }
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["ConnectionStrings:DefaultConnection"] = connection,
    ["JWT_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
    ["Jwt:Issuer"] = "MeepleBoardDeviceTests", ["Jwt:Audience"] = "MeepleBoardDeviceTests"
}).Build();
builder.Services.AddInfrastructure(config);
builder.Services.AddIdentityConfiguration(config);
builder.Services.Configure<JwtSettings>(o => { o.Key = config["JWT_KEY"]!; o.Issuer = config["Jwt:Issuer"]!; o.Audience = config["Jwt:Audience"]!; o.ExpiryHours = 24; });
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddApplicationPart(typeof(MatchController).Assembly);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.RemoveAll<IEmailService>(); builder.Services.AddSingleton<IEmailService>(new OfflineEmail(Path.Combine(dataPath, "outbox")));
builder.Services.RemoveAll<INotificationService>(); builder.Services.AddSingleton<INotificationService, OfflineNotifications>();
builder.Services.RemoveAll<IPhotoStorageService>(); builder.Services.AddSingleton<IPhotoStorageService>(new LocalPhotos(Path.Combine(dataPath, "photos")));
builder.Services.AddScoped<IBGGService, OfflineCatalog>();
builder.Services.AddSingleton<Hangfire.IBackgroundJobClient, DisabledBackgroundJobs>();
// No Hangfire server, jobs, production seeders, OAuth providers, auto-refresh or external clients started.
await using var app = builder.Build();
app.UseCors(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
app.MapGet("/device-test/health", () => new { environment = "DeviceTests", database = "MeepleBoard_DeviceTests", externalDelivery = false });
await DeviceDatabase.Prepare(app.Services, dataPath, connection);
if (args.Contains("--audit-only")) return;
Console.WriteLine("DeviceTests ready on port 5099; synthetic accounts: " + Path.Combine(dataPath, "accounts.json"));
if (args.Contains("--verify")) {
    await app.StartAsync();
    await SqlHttpChecks.Run(app.Services, dataPath);
    await app.StopAsync();
} else await app.RunAsync();

public static class DeviceDatabase {
    public static async Task Prepare(IServiceProvider services, string dataPath, string connection) {
        using (var reviewScope = services.CreateScope()) {
            var review = reviewScope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>();
            if (review.Database.HasPendingModelChanges()) throw new InvalidOperationException("Model drift: no SQL operation permitted.");
            var migrations = review.GetService<IMigrationsAssembly>();
            var creator = migrations.CreateMigration(migrations.Migrations["20261006120000_AddMatchCreator"], review.Database.ProviderName!);
            if (creator.UpOperations.Count != 1 || creator.UpOperations[0] is not Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation c || c.Table != "Matches" || c.Name != "CreatorId" || !c.IsNullable) throw new Exception("Unexpected creator migration changes");
            var index = migrations.CreateMigration(migrations.Migrations["20261006130000_AddCatalogPendingDetailsIndex"], review.Database.ProviderName!);
            if (index.UpOperations.Count != 1 || index.UpOperations[0] is not Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation i || i.Name != "IX_GameSearchCatalog_RatingsCount_BggRank_AverageRating_Name_BggId" || i.Filter != "[DetailsSyncedAt] IS NULL" || !i.IsDescending!.SequenceEqual(new[] { true, false, true, false, false })) throw new Exception("Unexpected catalogue migration changes");
            var ratings = migrations.CreateMigration(migrations.Migrations["20261006140000_PreserveJournalHalfRatings"], review.Database.ProviderName!);
            if (ratings.UpOperations.Count != 1 || ratings.UpOperations[0] is not Microsoft.EntityFrameworkCore.Migrations.Operations.AlterColumnOperation r || r.Table != "MatchJournalEntries" || r.Name != "PersonalRating" || r.ClrType != typeof(double) || r.ColumnType != "float" || !r.IsNullable || r.OldColumn.ClrType != typeof(int) || r.OldColumn.ColumnType != "int" || !r.OldColumn.IsNullable) throw new Exception("Unexpected journal rating migration changes");
            var sql = review.GetService<IMigrator>().GenerateScript("20260824063025_OptimizeGameSearchTokenKey");
            // EF drops only a possible default constraint on this exact column before widening it.
            const string approvedDefaultDrop = "IF @var IS NOT NULL EXEC(N'ALTER TABLE [MatchJournalEntries] DROP CONSTRAINT [' + @var + '];');";
            if (!sql.Contains("WHERE ([d].[parent_object_id] = OBJECT_ID(N'[MatchJournalEntries]') AND [c].[name] = N'PersonalRating');") || !sql.Contains("ALTER TABLE [MatchJournalEntries] ALTER COLUMN [PersonalRating] float NULL;")) throw new Exception("Unexpected rating conversion SQL");
            var auditedSql = sql.Replace(approvedDefaultDrop, "");
            if (auditedSql.Contains("DROP ") || auditedSql.Contains("UPDATE [Matches]") || auditedSql.Contains("DELETE ")) throw new Exception("Unexpected destructive migration SQL");
            Console.WriteLine("Migration audit passed: model matches snapshot; nullable CreatorId, one filtered index and nullable rating widened to float.");
            if (Environment.GetCommandLineArgs().Contains("--audit-only")) return;
        }
        // All SQL access derives from the same validated exclusive test connection.
        await using var master = new SqlConnection(DeviceTestSql.Master(connection));
        await master.OpenAsync();
        await using var check = master.CreateCommand();
        check.CommandText = "SELECT DB_ID(N'MeepleBoard_DeviceTests')";
        var exists = await check.ExecuteScalarAsync() is not DBNull;
        if (!exists) { await using var create = master.CreateCommand(); create.CommandText = "CREATE DATABASE [MeepleBoard_DeviceTests]"; await create.ExecuteNonQueryAsync(); }
        await using var dbConnection = new SqlConnection(connection);
        await dbConnection.OpenAsync();
        await using var marker = dbConnection.CreateCommand();
        marker.CommandText = "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'MeepleBoardDeviceTestsOwner'";
        var owner = await marker.ExecuteScalarAsync();
        if (exists && !Equals(owner, "MeepleBoardDeviceTestApi-v1")) throw new InvalidOperationException("Refusing to migrate a database without the device-test ownership marker.");
        if (!exists) { marker.CommandText = "EXEC sys.sp_addextendedproperty @name=N'MeepleBoardDeviceTestsOwner', @value=N'MeepleBoardDeviceTestApi-v1'"; await marker.ExecuteNonQueryAsync(); }
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>();
        if (db.Database.HasPendingModelChanges()) throw new InvalidOperationException("Model drift: migrations must match the model before any schema execution.");
        await db.Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync("User")) { var r = await roles.CreateAsync(new IdentityRole<Guid>("User")); if (!r.Succeeded) throw new Exception("Test role creation failed"); }
        var file = Path.Combine(dataPath, "accounts.json");
        var accounts = File.Exists(file) ? JsonSerializer.Deserialize<List<TestAccount>>(await File.ReadAllTextAsync(file))! : new List<TestAccount>();
        foreach (var name in new[] { "autor", "participante", "alheio", "membro" }) {
            var email = name + "@meepleboard.test";
            var account = accounts.SingleOrDefault(a => a.Email == email);
            if (account == null) { account = new(email, "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)), Guid.Empty); accounts.Add(account); }
            var user = await users.FindByEmailAsync(email);
            if (user == null) {
                await File.WriteAllTextAsync(file, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
                user = new User("Teste-" + name, email, "Local") { EmailConfirmed = true };
                var r = await users.CreateAsync(user, account.Password); if (!r.Succeeded) throw new Exception("Test account creation failed: " + string.Join(",", r.Errors.Select(e => e.Code)));
                await users.AddToRoleAsync(user, "User");
            }
            account.Id = user.Id;
        }
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var (name, bgg) in new[] { ("Meeple Teste Competitivo", 990001), ("Meeple Teste Cooperativo", 990002), ("Meeple Teste Solo", 990003) }) {
            if (await db.Games.AnyAsync(g => g.BGGId == bgg)) continue;
            var game = new Game(name, "Jogo fictício exclusivo da base descartável.", null, supportsSoloMode: true);
            Set(game, "BGGId", bgg); Set(game, "MinPlayers", 1); Set(game, "MaxPlayers", 4); Set(game, "IsCooperative", bgg == 990002); Set(game, "SupportsCampaign", true); Set(game, "IsApproved", true);
            db.Games.Add(game);
            var catalog = new GameSearchCatalog(bgg, name, name.ToLowerInvariant(), 2026, null, false, 1, 4, bgg == 990002, true, null, 0, null);
            Set(catalog, "DetailsSyncedAt", DateTime.UtcNow);
            db.GameSearchCatalog.Add(catalog);
        }
        // Repair search word tokens for fixtures created by earlier test-host versions.
        // Only the three synthetic catalog IDs are touched; existing tokens are preserved.
        foreach (var catalog in await db.GameSearchCatalog.Where(g => g.BggId >= 990001 && g.BggId <= 990003).ToListAsync()) {
            var words = catalog.NormalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (short position = 1; position < words.Length; position++) {
                if (!await db.GameSearchTokens.AnyAsync(t => t.BggId == catalog.BggId && t.Position == position))
                    db.GameSearchTokens.Add(new GameSearchToken(catalog.BggId, words[position], position));
            }
        }
        foreach (var other in accounts.Skip(1)) {
            var author = accounts[0].Id; var a = author.CompareTo(other.Id) < 0 ? author : other.Id; var b = a == author ? other.Id : author;
            if (!await db.Friendships.AnyAsync(f => f.UserAId == a && f.UserBId == b)) db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserAId = a, UserBId = b, InitiatorId = author, Status = FriendshipStatus.Accepted });
        }
        await db.SaveChangesAsync();
    }
    public static void Set(object entity, string property, object value) => entity.GetType().GetProperty(property)!.SetValue(entity, value);
}
public class TestAccount(string email, string password, Guid id) {
    public string Email { get; set; } = email;
    public string Password { get; set; } = password;
    public Guid Id { get; set; } = id;
}
public class OfflineEmail(string directory) : IEmailService {
    async Task Store(string recipient, string kind, string link) {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid() + ".json"), JsonSerializer.Serialize(new { recipient, kind, link }));
    }
    public Task SendConfirmationEmailAsync(string recipientEmail, string recipientName, string confirmationLink) => Store(recipientEmail, "confirmation", confirmationLink);
    public Task SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetLink) => Store(recipientEmail, "password-reset", resetLink);
}
public class DisabledBackgroundJobs : Hangfire.IBackgroundJobClient {
    public string Create(Hangfire.Common.Job job, Hangfire.States.IState state) => "device-test-jobs-disabled";
    public bool ChangeState(string jobId, Hangfire.States.IState state, string expectedState) => false;
}
public class OfflineNotifications : INotificationService {
    public Task SendAsync(string token, string title, string body, object? data = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task SendToManyAsync(IEnumerable<string> tokens, string title, string body, object? data = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyMatchCreatedAsync(Guid id, string name, IEnumerable<string> tokens, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyJournalEntryAddedAsync(Guid id, string name, string evaluator, IEnumerable<string> tokens, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyMatchJournalClosedAsync(Guid id, string name, IEnumerable<string> tokens, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyCampaignInviteAsync(string token, string name, string inviter, CancellationToken ct = default) => Task.CompletedTask;
}
public class LocalPhotos(string directory) : IPhotoStorageService {
    const string Prefix = "https://res.cloudinary.com/meepleboard-device-tests-local/image/authenticated/";
    string FileFor(string reference) {
        if (!reference.StartsWith(Prefix) || !Guid.TryParse(Path.GetFileNameWithoutExtension(reference), out var id)) throw new UnauthorizedAccessException();
        return Path.Combine(directory, id + ".jpg");
    }
    public async Task<string> UploadAsync(Stream stream, string fileName, CancellationToken ct = default) {
        Directory.CreateDirectory(directory); var reference = Prefix + Guid.NewGuid() + ".jpg";
        using var file = File.Create(FileFor(reference)); await stream.CopyToAsync(file, ct); return reference;
    }
    public Task DeleteAsync(string reference, CancellationToken ct = default) { File.Delete(FileFor(reference)); return Task.CompletedTask; }
    public async Task<(byte[] Content, string ContentType)> ReadProtectedAsync(string reference, CancellationToken ct = default) {
        var bytes = await File.ReadAllBytesAsync(FileFor(reference), ct);
        return (bytes, bytes.Length > 4 && bytes[0] == 137 && bytes[1] == 80 ? "image/png" : "image/jpeg");
    }
}
public class OfflineCatalog(MeepleBoardDbContext db, IMapper mapper) : IBGGService {
    public async Task<MeepleBoard.Services.DTOs.GameDto?> GetGameByNameAsync(string name, CancellationToken ct = default) => mapper.Map<MeepleBoard.Services.DTOs.GameDto>(await db.Games.FirstOrDefaultAsync(g => g.Name == name, ct));
    public async Task<MeepleBoard.Services.DTOs.GameDto?> GetGameByIdAsync(string id, CancellationToken ct = default) => mapper.Map<MeepleBoard.Services.DTOs.GameDto>(await db.Games.FirstOrDefaultAsync(g => g.BGGId.ToString() == id, ct));
    public async Task<List<MeepleBoard.Services.DTOs.GameDto>> GetHotGamesAsync(CancellationToken ct = default) => mapper.Map<List<MeepleBoard.Services.DTOs.GameDto>>(await db.Games.ToListAsync(ct));
    public async Task<List<MeepleBoard.Services.DTOs.GameDto>> GetGamesByIdsAsync(List<string> ids, CancellationToken ct = default) => mapper.Map<List<MeepleBoard.Services.DTOs.GameDto>>(await db.Games.Where(g => ids.Contains(g.BGGId.ToString()!)).ToListAsync(ct));
    public async Task<List<MeepleBoard.Services.DTOs.GameDto>> SearchGamesAsync(string name, CancellationToken ct = default) => mapper.Map<List<MeepleBoard.Services.DTOs.GameDto>>(await db.Games.Where(g => g.Name.Contains(name)).ToListAsync(ct));
    public Task<List<MeepleBoard.Services.Mapping.Dtos.GameSuggestionDto>> SearchGameSuggestionsAsync(string query, int offset = 0, int limit = 10, CancellationToken ct = default) => Task.FromResult(new List<MeepleBoard.Services.Mapping.Dtos.GameSuggestionDto>());
}
