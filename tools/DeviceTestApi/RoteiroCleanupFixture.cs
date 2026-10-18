using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

// DeviceTests only: removing named test sessions and matches with everything that hangs on them, after a recoverable
// copy. Used by roteiro-cleanup.cjs for rows that verifiers left on the iPhone walkthrough accounts (2026-10-18).
// - plan: read-only; every dependent row by table, and refusals (a campaign still links a match, a match of a listed
//   session is not listed). The plan hash covers every row that would go.
// - backup: read-only on SQL; writes every affected row, all columns (FOR JSON), to .device-tests/<name>.json.
// - remove: only with a backup whose rows still match SQL exactly and whose hash the caller confirms; one transaction;
//   game averages recalculated.
// - restore: puts the rows of a backup back, in dependency order (used by the self-test; available to undo a removal).
// Endpoints answer only on the Mac itself.
public static class RoteiroCleanupFixture {
    // Dependency order: parents first. Removal walks it backwards.
    static readonly (string Table, string Where)[] Tables = [
        ("GameSessions", "Id IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@sessions))"),
        ("GameSessionPlayers", "SessionId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@sessions))"),
        ("Matches", "Id IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@matches))"),
        ("MatchPlayers", "MatchId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@matches))"),
        ("MatchJournalEntries", "MatchId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@matches))"),
        ("MatchExperiencePublications", "JournalEntryId IN (SELECT Id FROM MatchJournalEntries WHERE MatchId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@matches)))"),
        ("ExperienceReports", "PublicationId IN (SELECT p.Id FROM MatchExperiencePublications p JOIN MatchJournalEntries e ON e.Id = p.JournalEntryId WHERE e.MatchId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@matches)))"),
        ("PhotoReports", "JournalEntryId IN (SELECT Id FROM MatchJournalEntries WHERE MatchId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@matches)))"),
        ("PublicationPhotos", "PublicationId IN (SELECT p.Id FROM MatchExperiencePublications p JOIN MatchJournalEntries e ON e.Id = p.JournalEntryId WHERE e.MatchId IN (SELECT CAST(value AS uniqueidentifier) FROM OPENJSON(@matches)))"),
    ];

    public sealed record Selection(List<Guid> Sessions, List<Guid> Matches);
    public sealed record BackupRequest(List<Guid> Sessions, List<Guid> Matches, string Name);
    public sealed record RemoveRequest(string Backup, string ConfirmHash);
    public sealed record RestoreRequest(string Backup);

    public static void Map(WebApplication app, string dataPath) {
        var group = app.MapGroup("/device-test/roteiro-cleanup").AddEndpointFilter(async (context, next) =>
            context.HttpContext.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) ? await next(context) : Results.NotFound());
        string File(string name) => Path.Combine(dataPath, Path.GetFileName(name.EndsWith(".json") ? name : name + ".json"));

        group.MapPost("/plan", async (Selection s, MeepleBoardDbContext db) => Results.Ok(await Plan(db, s)));

        group.MapPost("/backup", async (BackupRequest r, MeepleBoardDbContext db) => {
            var plan = await Plan(db, new(r.Sessions, r.Matches));
            if (plan.Refusals.Count > 0) return Results.Conflict(plan);
            var rows = await Rows(db, r.Sessions, r.Matches);
            var backup = new { createdAt = DateTime.UtcNow, sessions = r.Sessions, matches = r.Matches, hash = plan.Hash, games = plan.Games, rows };
            var path = File(r.Name);
            if (System.IO.File.Exists(path)) return Results.Conflict(new { message = "A backup with this name already exists." });
            await System.IO.File.WriteAllTextAsync(path, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
            System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return Results.Ok(new { file = Path.GetFileName(path), plan.Hash, counts = rows.ToDictionary(t => t.Key, t => t.Value.Count) });
        });

        group.MapPost("/remove", async (RemoveRequest r, MeepleBoardDbContext db, IGameRepository games) => {
            var path = File(r.Backup);
            if (!System.IO.File.Exists(path)) return Results.NotFound(new { message = "Backup not found." });
            using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(path));
            var sessions = doc.RootElement.GetProperty("sessions").EnumerateArray().Select(e => e.GetGuid()).ToList();
            var matches = doc.RootElement.GetProperty("matches").EnumerateArray().Select(e => e.GetGuid()).ToList();
            var plan = await Plan(db, new(sessions, matches));
            if (plan.Refusals.Count > 0) return Results.Conflict(plan);
            // The confirmed hash, the backup's and the current rows' must all agree: nothing changed since the copy.
            if (r.ConfirmHash != plan.Hash || doc.RootElement.GetProperty("hash").GetString() != plan.Hash)
                return Results.Conflict(new { message = "The rows changed since the backup, or the hash was not confirmed.", plan.Hash });
            var current = JsonSerializer.Serialize(await Rows(db, sessions, matches));
            if (!SameRows(current, doc.RootElement.GetProperty("rows")))
                return Results.Conflict(new { message = "SQL no longer matches the backup." });
            var strategy = db.Database.CreateExecutionStrategy();
            var removed = await strategy.ExecuteAsync(async () => {
                await using var tx = await db.Database.BeginTransactionAsync();
                var counts = new Dictionary<string, int>();
                foreach (var (table, where) in Tables.Reverse())
                    counts[table] = await Execute(db, $"DELETE FROM [{table}] WHERE {where}", sessions, matches);
                await tx.CommitAsync();
                return counts;
            });
            foreach (var game in plan.Games) await games.RecomputeMeepleBoardRatingAsync(game);
            return Results.Ok(new { removed, recalculatedGames = plan.Games.Count });
        });

        group.MapPost("/restore", async (RestoreRequest r, MeepleBoardDbContext db, IGameRepository games) => {
            var path = File(r.Backup);
            if (!System.IO.File.Exists(path)) return Results.NotFound(new { message = "Backup not found." });
            using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(path));
            var rows = doc.RootElement.GetProperty("rows");
            var strategy = db.Database.CreateExecutionStrategy();
            var restored = await strategy.ExecuteAsync(async () => {
                await using var tx = await db.Database.BeginTransactionAsync();
                var counts = new Dictionary<string, int>();
                foreach (var (table, _) in Tables) {
                    var json = rows.GetProperty(table).GetRawText();
                    counts[table] = json == "[]" ? 0 : await Insert(db, table, json);
                }
                await tx.CommitAsync();
                return counts;
            });
            foreach (var game in doc.RootElement.GetProperty("games").EnumerateArray()) await games.RecomputeMeepleBoardRatingAsync(game.GetGuid());
            return Results.Ok(new { restored });
        });
    }

    public sealed record PlanResult(Dictionary<string, int> Counts, List<string> Refusals, List<Guid> Games, string Hash, object Rows);

    static async Task<PlanResult> Plan(MeepleBoardDbContext db, Selection s) {
        var refusals = new List<string>();
        var foundSessions = await db.GameSessions.AsNoTracking().Where(x => s.Sessions.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        var foundMatches = await db.Matches.AsNoTracking().Where(x => s.Matches.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        refusals.AddRange(s.Sessions.Except(foundSessions).Select(id => $"session {id} not found"));
        refusals.AddRange(s.Matches.Except(foundMatches).Select(id => $"match {id} not found"));
        refusals.AddRange((await db.Matches.AsNoTracking().Where(m => m.GameSessionId != null && s.Sessions.Contains(m.GameSessionId.Value) && !s.Matches.Contains(m.Id))
            .Select(m => m.Id).ToListAsync()).Select(id => $"match {id} belongs to a listed session but is not listed"));
        refusals.AddRange((await db.CampaignMatches.AsNoTracking().Where(c => s.Matches.Contains(c.MatchId)).Select(c => c.MatchId).ToListAsync())
            .Select(id => $"match {id} is linked to a campaign"));
        var rows = await Rows(db, s.Sessions, s.Matches);
        var games = await db.Matches.AsNoTracking().Where(m => s.Matches.Contains(m.Id)).Select(m => m.GameId).Distinct().ToListAsync();
        // Hash: every table with the ids of the rows that would go (rows keep their full content in the backup).
        var keys = string.Join("\n", rows.OrderBy(t => t.Key).Select(t => t.Key + ":" + string.Join(",",
            t.Value.Select(r => r.TryGetValue("Id", out var id) ? id?.ToString() : JsonSerializer.Serialize(r)).OrderBy(x => x))));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keys))).ToLowerInvariant()[..16];
        // Evidence: who made it and when (no notes or private text).
        var evidence = new {
            sessions = await db.GameSessions.AsNoTracking().Where(x => s.Sessions.Contains(x.Id)).Select(x => new { x.Id, x.Name, organizer = x.Organizer.UserName, x.StartDate, x.ScheduledStartDate, x.IsCancelled, x.EndDate,
                players = x.Players.Select(p => new { p.User.UserName, p.Status }).ToList() }).ToListAsync(),
            matches = await db.Matches.AsNoTracking().Where(x => s.Matches.Contains(x.Id)).Select(x => new { x.Id, x.CreatedAt, x.ScoreSummary, x.Location, x.GameSessionId, game = x.Game!.Name,
                creator = db.Users.Where(u => u.Id == x.CreatorId).Select(u => u.UserName).FirstOrDefault(), players = x.MatchPlayers.Select(p => p.User.UserName).ToList() }).ToListAsync(),
        };
        return new(rows.ToDictionary(t => t.Key, t => t.Value.Count), refusals, games, hash, evidence);
    }

    static async Task<Dictionary<string, List<Dictionary<string, object?>>>> Rows(MeepleBoardDbContext db, List<Guid> sessions, List<Guid> matches) {
        var result = new Dictionary<string, List<Dictionary<string, object?>>>();
        foreach (var (table, where) in Tables) {
            var json = await Scalar(db, $"SELECT * FROM [{table}] WHERE {where} ORDER BY 1 FOR JSON PATH, INCLUDE_NULL_VALUES", sessions, matches);
            result[table] = string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json)!;
        }
        return result;
    }

    static bool SameRows(string current, JsonElement saved) =>
        JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(current)) == JsonSerializer.Serialize(saved);

    static DbCommand Command(MeepleBoardDbContext db, string sql, List<Guid>? sessions, List<Guid>? matches) {
        var c = db.Database.GetDbConnection().CreateCommand();
        c.CommandText = sql; c.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        void Add(string name, string value) { var p = c.CreateParameter(); p.ParameterName = name; p.Value = value; c.Parameters.Add(p); }
        Add("@sessions", JsonSerializer.Serialize(sessions ?? [])); Add("@matches", JsonSerializer.Serialize(matches ?? []));
        return c;
    }

    static async Task<string> Scalar(MeepleBoardDbContext db, string sql, List<Guid> sessions, List<Guid> matches) {
        await db.Database.OpenConnectionAsync();
        await using var c = Command(db, sql, sessions, matches);
        var sb = new StringBuilder();
        await using var reader = await c.ExecuteReaderAsync();
        while (await reader.ReadAsync()) sb.Append(reader.GetString(0)); // FOR JSON splits long results over rows
        return sb.ToString();
    }

    static async Task<int> Execute(MeepleBoardDbContext db, string sql, List<Guid>? sessions, List<Guid>? matches, string? json = null) {
        await db.Database.OpenConnectionAsync();
        await using var c = Command(db, sql, sessions, matches);
        if (json != null) { var p = c.CreateParameter(); p.ParameterName = "@json"; p.Value = json; c.Parameters.Add(p); }
        return await c.ExecuteNonQueryAsync();
    }

    // Rows back with every column as it was, typed from the table's own schema.
    static async Task<int> Insert(MeepleBoardDbContext db, string table, string json) {
        var columns = new List<(string Name, string Type, bool Identity)>();
        await db.Database.OpenConnectionAsync();
        await using (var c = Command(db, @"SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, c.is_identity
            FROM sys.columns c WHERE c.object_id = OBJECT_ID(@t) AND c.is_computed = 0 ORDER BY c.column_id", null, null)) {
            var p = c.CreateParameter(); p.ParameterName = "@t"; p.Value = table; c.Parameters.Add(p);
            await using var r = await c.ExecuteReaderAsync();
            while (await r.ReadAsync()) {
                var type = r.GetString(1); short max = r.GetInt16(2); byte precision = r.GetByte(3), scale = r.GetByte(4);
                var full = type switch {
                    "nvarchar" or "nchar" => $"{type}({(max == -1 ? "max" : (max / 2).ToString())})",
                    "varchar" or "char" or "varbinary" or "binary" => $"{type}({(max == -1 ? "max" : max.ToString())})",
                    "decimal" or "numeric" => $"{type}({precision},{scale})",
                    "datetime2" or "datetimeoffset" or "time" => $"{type}({scale})",
                    _ => type };
                columns.Add((r.GetString(0), full, r.GetBoolean(5)));
            }
        }
        var names = string.Join(", ", columns.Select(c => $"[{c.Name}]"));
        var with = string.Join(", ", columns.Select(c => $"[{c.Name}] {c.Type} '$.\"{c.Name}\"'"));
        var identity = columns.Any(c => c.Identity);
        var sql = (identity ? $"SET IDENTITY_INSERT [{table}] ON; " : "") + $"INSERT INTO [{table}] ({names}) SELECT {names} FROM OPENJSON(@json) WITH ({with});"
            + (identity ? $" SET IDENTITY_INSERT [{table}] OFF;" : "");
        return await Execute(db, sql, null, null, json);
    }
}
