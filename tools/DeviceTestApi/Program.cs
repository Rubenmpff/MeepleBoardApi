using System.Security.Cryptography;
using System.Text.Json;
using AutoMapper;
using MeepleBoard.CrossCutting.IoC;
using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Enums;
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
// No Hangfire server, production seeders, OAuth providers, auto-refresh or external clients started.
// The only job that runs is the real session auto-close job, on a timer (Hangfire runs it in production).
if (!args.Contains("--verify") && !args.Contains("--audit-only")) builder.Services.AddHostedService<DeviceSessionAutoClose>();
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
            var outcomesMigration = migrations.CreateMigration(migrations.Migrations["20261006150000_AddExplicitMatchOutcomes"], review.Database.ProviderName!);
            var expected = new HashSet<string> { "Matches.GameMode", "Matches.Result", "Matches.SharedVictoryAllowed", "MatchPlayers.Outcome" };
            if (outcomesMigration.UpOperations.Count != 4 || outcomesMigration.UpOperations.Any(op => op is not Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation col || !col.IsNullable || !expected.Remove(col.Table + "." + col.Name) || (col.Name == "SharedVictoryAllowed" ? col.ClrType != typeof(bool) || col.ColumnType != "bit" : col.ClrType != typeof(string) || col.ColumnType != "nvarchar(16)")) || expected.Count != 0) throw new Exception("Unexpected outcome migration changes");
            var closeTime = migrations.CreateMigration(migrations.Migrations["20261012080128_AddSessionCloseTime"], review.Database.ProviderName!);
            if (closeTime.UpOperations.Count != 2 || closeTime.UpOperations.Any(op => op is not Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation col || col.Table != "GameSessions"
                || !(col.Name == "ClosesAt" && col.IsNullable && col.ColumnType == "datetime2" || col.Name == "ClosedAutomatically" && !col.IsNullable && col.ColumnType == "bit" && Equals(col.DefaultValue, false)))) throw new Exception("Unexpected session close-time migration changes");
            var resources = migrations.CreateMigration(migrations.Migrations["20261012081445_AddGameResources"], review.Database.ProviderName!);
            var tables = resources.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().Select(o => o.Name).OrderBy(n => n).ToArray();
            if (!tables.SequenceEqual(new[] { "GameResourceReports", "GameResources" }) || resources.UpOperations.Count != 6
                || resources.UpOperations.Skip(2).Any(op => op is not Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation)) throw new Exception("Unexpected game resources migration changes");
            var resourceExtras = migrations.CreateMigration(migrations.Migrations["20261014100853_AddGameResourceExpansionThumbnail"], review.Database.ProviderName!);
            if (resourceExtras.UpOperations.Count != 2 || resourceExtras.UpOperations.Any(op => op is not Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation col || col.Table != "GameResources" || !col.IsNullable
                || !(col.Name == "Expansion" && col.ColumnType == "nvarchar(100)" || col.Name == "ThumbnailUrl" && col.ColumnType == "nvarchar(500)"))) throw new Exception("Unexpected resource expansion/thumbnail migration changes");
            var sheet = migrations.CreateMigration(migrations.Migrations["20261014164836_AddGameSheetAndSessionPlannedGame"], review.Database.ProviderName!);
            var sheetColumns = sheet.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>().Select(c => c.IsNullable ? c.Table + "." + c.Name + ":" + c.ColumnType : "NOT NULL").OrderBy(n => n).ToArray();
            if (sheet.UpOperations.Count != 7 || !sheetColumns.SequenceEqual(new[] { "Games.AverageWeight:float", "Games.MaxPlayTime:int", "Games.MinAge:int", "Games.MinPlayTime:int", "GameSessions.PlannedGameId:uniqueidentifier" })
                || sheet.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>().SingleOrDefault()?.Name != "IX_GameSessions_PlannedGameId"
                || sheet.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddForeignKeyOperation>().SingleOrDefault() is not { Table: "GameSessions", PrincipalTable: "Games", OnDelete: ReferentialAction.SetNull }) throw new Exception("Unexpected game sheet/planned game migration changes");
            var durations = migrations.CreateMigration(migrations.Migrations["20261016083156_AddDurationMethodAndSharing"], review.Database.ProviderName!);
            if (durations.UpOperations.Count != 2 || durations.UpOperations.Any(op => op is not Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation col
                || !(col.Table == "Matches" && col.Name == "DurationMethod" && col.IsNullable && col.ColumnType == "int"
                    || col.Table == "AspNetUsers" && col.Name == "ContributesDurationStats" && !col.IsNullable && col.ColumnType == "bit" && Equals(col.DefaultValue, false)))) throw new Exception("Unexpected duration method/sharing migration changes");
            var ratings2 = migrations.CreateMigration(migrations.Migrations["20261016183656_AddRatingCountsAndExperiencePublications"], review.Database.ProviderName!);
            var ratingColumns = ratings2.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>().Select(c => $"{c.Table}.{c.Name}:{c.ColumnType}:{c.IsNullable}:{c.DefaultValue}").OrderBy(n => n).ToArray();
            var ratingTables = ratings2.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().Select(t => t.Name).OrderBy(n => n).ToArray();
            if (ratings2.UpOperations.Count != 7 || !ratingColumns.SequenceEqual(new[] { "Games.MeepleBoardRaters:int:False:0", "Games.MeepleBoardRatingsCount:int:False:0" })
                || !ratingTables.SequenceEqual(new[] { "ExperienceReports", "MatchExperiencePublications" })
                || ratings2.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>().Count() != 3) throw new Exception("Unexpected rating counts/experience migration changes");
            var deletion = migrations.CreateMigration(migrations.Migrations["20261016201449_AddUserDeletedAt"], review.Database.ProviderName!);
            if (deletion.UpOperations.Count != 1 || deletion.UpOperations[0] is not Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation { Table: "AspNetUsers", Name: "DeletedAt", IsNullable: true, ColumnType: "datetime2" })
                throw new Exception("Unexpected account deletion migration changes");
            var sql = review.GetService<IMigrator>().GenerateScript("20260824063025_OptimizeGameSearchTokenKey");
            // EF drops only a possible default constraint on this exact column before widening it.
            const string approvedDefaultDrop = "IF @var IS NOT NULL EXEC(N'ALTER TABLE [MatchJournalEntries] DROP CONSTRAINT [' + @var + '];');";
            if (!sql.Contains("WHERE ([d].[parent_object_id] = OBJECT_ID(N'[MatchJournalEntries]') AND [c].[name] = N'PersonalRating');") || !sql.Contains("ALTER TABLE [MatchJournalEntries] ALTER COLUMN [PersonalRating] float NULL;")) throw new Exception("Unexpected rating conversion SQL");
            // Referential actions on the new resource tables' foreign keys: no existing row is touched.
            string[] approvedResourceKeys = [
                "REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL", "REFERENCES [Games] ([Id]) ON DELETE CASCADE",
                "REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION", "REFERENCES [GameResources] ([Id]) ON DELETE CASCADE",
                // Deleting a game leaves sessions in place with no planned game.
                "REFERENCES [Games] ([Id]) ON DELETE SET NULL",
                // A publication goes with its diary entry; reports go with the publication.
                "REFERENCES [MatchJournalEntries] ([Id]) ON DELETE CASCADE", "REFERENCES [MatchExperiencePublications] ([Id]) ON DELETE CASCADE"];
            var auditedSql = approvedResourceKeys.Aggregate(sql.Replace(approvedDefaultDrop, ""), (text, clause) => text.Replace(clause, ""));
            if (auditedSql.Contains("DROP ") || auditedSql.Contains("UPDATE [Matches]") || auditedSql.Contains("DELETE ")) throw new Exception("Unexpected destructive migration SQL");
            Console.WriteLine("Migration audit passed: model matches snapshot; nullable CreatorId, one filtered index and nullable rating widened to float, four nullable outcome columns, two session close-time columns, two new resource tables, two nullable resource columns, four nullable game sheet columns, a nullable planned game (set null on delete), a nullable duration method, an off-by-default duration sharing preference, two rating counts and the experience publication and report tables.");
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
        foreach (var role in new[] { "User", "Admin" })
            if (!await roles.RoleExistsAsync(role)) { var r = await roles.CreateAsync(new IdentityRole<Guid>(role)); if (!r.Succeeded) throw new Exception("Test role creation failed"); }
        var file = Path.Combine(dataPath, "accounts.json");
        var accounts = File.Exists(file) ? JsonSerializer.Deserialize<List<TestAccount>>(await File.ReadAllTextAsync(file))! : new List<TestAccount>();
        // "admin" is the only fictitious administrator (resource management); it is nobody's friend.
        foreach (var name in new[] { "autor", "participante", "alheio", "membro", "admin" }) {
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
            if (name == "admin" && !await users.IsInRoleAsync(user, "Admin")) await users.AddToRoleAsync(user, "Admin");
            account.Id = user.Id;
        }
        // «eliminar» is disposable and kept apart (other verifiers sign in with every account in accounts.json):
        // verify-account-deletion deletes it, and it comes back, new and empty, on the next start. Nobody's friend.
        accounts.RemoveAll(a => a.Email == "eliminar@meepleboard.test");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
        var disposableFile = Path.Combine(dataPath, "disposable-account.json");
        var disposable = File.Exists(disposableFile) ? JsonSerializer.Deserialize<TestAccount>(await File.ReadAllTextAsync(disposableFile))!
            : new TestAccount("eliminar@meepleboard.test", "aA1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)), Guid.Empty);
        var disposableUser = await users.FindByEmailAsync(disposable.Email);
        if (disposableUser == null) {
            disposableUser = new User("Teste-eliminar", disposable.Email, "Local") { EmailConfirmed = true };
            var r = await users.CreateAsync(disposableUser, disposable.Password); if (!r.Succeeded) throw new Exception("Disposable account creation failed: " + string.Join(",", r.Errors.Select(e => e.Code)));
            await users.AddToRoleAsync(disposableUser, "User");
        }
        disposable.Id = disposableUser.Id;
        await File.WriteAllTextAsync(disposableFile, JsonSerializer.Serialize(disposable, new JsonSerializerOptions { WriteIndented = true }));
        // Fictitious games carry no description: the app labels descriptions of games with a BGG id as original BGG text,
        // so an invented sentence would be attributed to BGG. Older rows are cleared on start.
        foreach (var fictitious in await db.Games.Where(g => g.BGGId >= 990001 && g.BGGId <= 990003 && g.Description != "").ToListAsync()) Set(fictitious, "Description", "");
        foreach (var (name, bgg) in new[] { ("Meeple Teste Competitivo", 990001), ("Meeple Teste Cooperativo", 990002), ("Meeple Teste Solo", 990003) }) {
            if (await db.Games.AnyAsync(g => g.BGGId == bgg)) continue;
            var game = new Game(name, "", null, supportsSoloMode: true);
            Set(game, "BGGId", bgg); Set(game, "MinPlayers", 1); Set(game, "MaxPlayers", 4); Set(game, "IsCooperative", bgg == 990002); Set(game, "SupportsCampaign", true); Set(game, "IsApproved", true);
            db.Games.Add(game);
            var catalog = new GameSearchCatalog(bgg, name, name.ToLowerInvariant(), 2026, null, false, 1, 4, bgg == 990002, true, null, 0, null);
            Set(catalog, "DetailsSyncedAt", DateTime.UtcNow);
            db.GameSearchCatalog.Add(catalog);
        }
        // One real game, with facts verified on the publisher's site (BGG 266192, Stonemaier Games, 2019, 1–5 players),
        // so the resource catalogue can hold real links. No BGG description, image or ratings are copied.
        if (!await db.Games.AnyAsync(g => g.BGGId == 266192)) {
            var wingspan = new Game("Wingspan", "", null, supportsSoloMode: true);
            Set(wingspan, "BGGId", 266192); Set(wingspan, "YearPublished", 2019); Set(wingspan, "MinPlayers", 1); Set(wingspan, "MaxPlayers", 5); Set(wingspan, "IsApproved", true);
            db.Games.Add(wingspan);
            var catalog = new GameSearchCatalog(266192, "Wingspan", "wingspan", 2019, null, false, 1, 5, false, false, null, null, null);
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
        string[] authorFriends = ["participante@meepleboard.test", "alheio@meepleboard.test", "membro@meepleboard.test"];
        foreach (var other in accounts.Where(a => authorFriends.Contains(a.Email))) {
            var author = accounts[0].Id; var a = author.CompareTo(other.Id) < 0 ? author : other.Id; var b = a == author ? other.Id : author;
            if (!await db.Friendships.AnyAsync(f => f.UserAId == a && f.UserBId == b)) db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserAId = a, UserBId = b, InitiatorId = author, Status = FriendshipStatus.Accepted });
        }
        await db.SaveChangesAsync();
        await SeedGameFriends(db, users, accounts, file);
        await SeedWingspanSheetAndExpansions(db, accounts);
    }

    // Game page facts and expansions for Wingspan, reset on every start.
    // Duration 40–70 min and age 10+ as the BGG page showed them on 14/10/2026 (user's screenshots); the BGG
    // complexity is a community statistic and is not copied, so the page also shows a partial sheet.
    // The two expansions are real Stonemaier products (names only); "participante" owns the European one.
    static async Task SeedWingspanSheetAndExpansions(MeepleBoardDbContext db, List<TestAccount> accounts) {
        var wingspan = await db.Games.SingleAsync(g => g.BGGId == 266192);
        wingspan.SetBggSheet(40, 70, null, 10, null);
        var viewer = accounts.Single(a => a.Email == "participante@meepleboard.test").Id;
        foreach (var (name, owned) in new[] { ("Wingspan: European Expansion", true), ("Wingspan: Oceania Expansion", false) }) {
            var expansion = await db.Games.SingleOrDefaultAsync(g => g.Name == name);
            if (expansion == null) {
                expansion = new Game(name, "", null);
                Set(expansion, "IsApproved", true);
                db.Games.Add(expansion);
            }
            Set(expansion, "BaseGameId", wingspan.Id);
            var entry = await db.UserGameLibraries.SingleOrDefaultAsync(l => l.UserId == viewer && l.GameId == expansion.Id);
            if (owned && entry == null) db.UserGameLibraries.Add(new UserGameLibrary(viewer, expansion.Id, GameLibraryStatus.Owned));
            else if (owned && entry!.Status != GameLibraryStatus.Owned) entry.UpdateStatus(GameLibraryStatus.Owned);
            else if (!owned && entry != null) db.UserGameLibraries.Remove(entry);
        }
        await db.SaveChangesAsync();
    }

    // «Amigos que têm este jogo» (Wingspan), seen by "participante": who may appear and who must not.
    // Fictitious accounts named Teste-…; libraries and privacy are reset to these values on every start.
    static async Task SeedGameFriends(MeepleBoardDbContext db, UserManager<User> users, List<TestAccount> accounts, string file) {
        var viewer = accounts.Single(a => a.Email == "participante@meepleboard.test").Id;
        var wingspan = await db.Games.SingleAsync(g => g.BGGId == 266192);
        (string Name, LibraryPrivacy Privacy, GameLibraryStatus Status, FriendshipStatus? Friendship)[] people = [
            ("amiga-colecionadora", LibraryPrivacy.Public, GameLibraryStatus.Owned, FriendshipStatus.Accepted),
            ("amigo-com-um-nome-de-utilizador-bastante-comprido", LibraryPrivacy.FriendsOnly, GameLibraryStatus.Owned, FriendshipStatus.Accepted),
            ("amiga-tabuleiro", LibraryPrivacy.FriendsOnly, GameLibraryStatus.Owned, FriendshipStatus.Accepted),
            ("amigo-dados", LibraryPrivacy.Public, GameLibraryStatus.Owned, FriendshipStatus.Accepted),
            ("amiga-meeples", LibraryPrivacy.Public, GameLibraryStatus.Owned, FriendshipStatus.Accepted),
            ("amigo-privado", LibraryPrivacy.Private, GameLibraryStatus.Owned, FriendshipStatus.Accepted),   // private library: hidden
            ("amiga-quer-jogar", LibraryPrivacy.Public, GameLibraryStatus.Wishlist, FriendshipStatus.Accepted), // wishlist is not ownership
            ("amigo-ja-jogou", LibraryPrivacy.Public, GameLibraryStatus.Played, FriendshipStatus.Accepted),    // «Já joguei» is not ownership
            ("pedido-pendente", LibraryPrivacy.Public, GameLibraryStatus.Owned, FriendshipStatus.Pending),     // not yet a friend
            ("desconhecido", LibraryPrivacy.Public, GameLibraryStatus.Owned, null)];                           // not a friend
        foreach (var (name, privacy, status, friendship) in people) {
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
            user.SetLibraryPrivacy(privacy);
            var entry = await db.UserGameLibraries.SingleOrDefaultAsync(l => l.UserId == user.Id && l.GameId == wingspan.Id);
            if (entry == null) db.UserGameLibraries.Add(new UserGameLibrary(user.Id, wingspan.Id, status));
            else if (entry.Status != status) entry.UpdateStatus(status);
            var a = viewer.CompareTo(user.Id) < 0 ? viewer : user.Id; var b = a == viewer ? user.Id : viewer;
            var link = await db.Friendships.SingleOrDefaultAsync(f => f.UserAId == a && f.UserBId == b);
            if (friendship == null) { if (link != null) db.Friendships.Remove(link); }
            else if (link == null) db.Friendships.Add(new Friendship { Id = Guid.NewGuid(), UserAId = a, UserBId = b, InitiatorId = user.Id, Status = friendship.Value });
            else link.Status = friendship.Value;
        }
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
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
// Runs the production SessionAutoCloseJob every minute; state is in SQL, so restarts resume correctly.
public class DeviceSessionAutoClose(IServiceScopeFactory scopes, ILogger<DeviceSessionAutoClose> logger) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do {
            try {
                using var scope = scopes.CreateScope();
                var closed = await scope.ServiceProvider.GetRequiredService<MeepleBoard.Services.Job.SessionAutoCloseJob>().ExecuteAsync(stoppingToken);
                if (closed > 0) Console.WriteLine($"Session auto-close: {closed} session(s) ended.");
            } catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Session auto-close failed; retrying next minute."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
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
