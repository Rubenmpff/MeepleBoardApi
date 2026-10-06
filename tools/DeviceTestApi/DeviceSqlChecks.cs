using Microsoft.Data.SqlClient;
using System.Text.Json;

public static class DeviceSqlChecks {
    public static async Task Run(string connection, string dataPath, string mode) {
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var cmd = sql.CreateCommand();
        cmd.CommandText = "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class=0 AND name=N'MeepleBoardDeviceTestsOwner'";
        if (!Equals(await cmd.ExecuteScalarAsync(), "MeepleBoardDeviceTestApi-v1")) throw new Exception("Refusing SQL checks on an unowned database.");
        async Task Check(string name, string query, int expected) {
            cmd.CommandText = query;
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != expected) throw new Exception("SQL check failed: " + name);
            Console.WriteLine("PASS SQL " + name);
        }
        await Check("24 migrations applied", "SELECT COUNT(*) FROM __EFMigrationsHistory", 24);
        await Check("CreatorId exists", "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('Matches') AND name='CreatorId'", 1);
        await Check("catalogue filtered index exists", "SELECT COUNT(*) FROM sys.indexes WHERE name='IX_GameSearchCatalog_RatingsCount_BggRank_AverageRating_Name_BggId' AND has_filter=1", 1);
        await Check("journal half-point nullable column", "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('MatchJournalEntries') AND name='PersonalRating' AND system_type_id=TYPE_ID('float') AND is_nullable=1", 1);
        if (mode == "schema") return;
        var file = mode switch { "writes" => "session-write-fixture.json", "rule" => "session-rule-fixture.json", "invites" => "session-invite-friends-fixture.json", "dates" => "session-date-fixture.json", _ => throw new Exception("Unknown SQL verification mode") };
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dataPath, file)));
        void Id(string parameter, JsonElement element) => cmd.Parameters.AddWithValue(parameter, element.GetGuid());
        if (mode == "writes") {
            using var accounts = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dataPath, "accounts.json")));
            Id("@match", fixture.RootElement.GetProperty("matchId")); Id("@session", fixture.RootElement.GetProperty("sessionId"));
            Id("@author", accounts.RootElement[0].GetProperty("Id")); Id("@peer", accounts.RootElement[1].GetProperty("Id")); Id("@member", accounts.RootElement[3].GetProperty("Id"));
            await Check("match creator, session, result and no personal note copy", """SELECT COUNT(*) FROM Matches WHERE Id=@match AND CreatorId=@author AND GameSessionId=@session AND WinnerId=@author AND ScoreSummary=N'Atualizado sem 404' AND Notes IS NULL""", 1);
            await Check("author score 17", """SELECT COUNT(*) FROM MatchPlayers WHERE MatchId=@match AND UserId=@author AND Score=17""", 1);
            await Check("peer score zero", """SELECT COUNT(*) FROM MatchPlayers WHERE MatchId=@match AND UserId=@peer AND Score=0""", 1);
            await Check("edited author diary", """SELECT COUNT(*) FROM MatchJournalEntries WHERE MatchId=@match AND UserId=@author AND PersonalRating=9 AND Notes=N'SESSION_PRIVATE_AUTHOR_EDITED' AND Tags=N'editado'""", 1);
            await Check("preserved peer diary zero", """SELECT COUNT(*) FROM MatchJournalEntries WHERE MatchId=@match AND UserId=@peer AND PersonalRating=0 AND Notes=N'SESSION_PRIVATE_PEER'""", 1);
            await Check("accepted invitation", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@peer AND Status=1""", 1);
            await Check("declined invitation", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@member AND Status=2""", 1);
            cmd.CommandText = "SELECT MeepleBoardScore FROM Games WHERE Id=(SELECT GameId FROM Matches WHERE Id=@match)";
            var stored = Convert.ToDouble(await cmd.ExecuteScalarAsync());
            cmd.CommandText = """
SELECT AVG(r.Rating) FROM (
 SELECT CAST(e.PersonalRating AS float) AS Rating FROM MatchJournalEntries e JOIN Matches m ON m.Id=e.MatchId
 WHERE m.GameId=(SELECT GameId FROM Matches WHERE Id=@match) AND e.PersonalRating IS NOT NULL
 UNION ALL
 SELECT CAST(m.PersonalRating AS float) FROM Matches m
 WHERE m.GameId=(SELECT GameId FROM Matches WHERE Id=@match) AND m.PersonalRating IS NOT NULL
 AND NOT EXISTS (SELECT 1 FROM MatchJournalEntries e WHERE e.MatchId=m.Id)
) r
""";
            var expected = Math.Round(Convert.ToDouble(await cmd.ExecuteScalarAsync()) * 10);
            if (stored != expected) throw new Exception("Rating aggregate mismatch");
            Console.WriteLine("PASS SQL game rating aggregate matches persisted diary ratings");
            return;
        }
        if (mode == "rule") {
            Id("@session", fixture.RootElement.GetProperty("sessionId"));
            Id("@author", fixture.RootElement.GetProperty("author"));
            Id("@peer", fixture.RootElement.GetProperty("peer"));
            Id("@member", fixture.RootElement.GetProperty("member"));
            cmd.Parameters.AddWithValue("@rejected", fixture.RootElement.GetProperty("rejectedName").GetString()!);
            await Check("rejected requests saved no session", """SELECT COUNT(*) FROM GameSessions WHERE Name=@rejected""", 0);
            await Check("session exists without custom deadline", """SELECT COUNT(*) FROM GameSessions WHERE Id=@session AND IsCancelled=0 AND ResponseDeadline IS NULL""", 1);
            await Check("one accepted organizer", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@author AND IsOrganizer=1 AND Status=1""", 1);
            await Check("original friend declined", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@peer AND Status=2""", 1);
            await Check("replacement friend pending", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@member AND Status=0""", 1);
            await Check("no duplicate memberships", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session""", 3);
            cmd.CommandText = "SELECT ScheduledStartDate FROM GameSessions WHERE Id=@session";
            if (((DateTime)(await cmd.ExecuteScalarAsync())!).Ticks != DateTimeOffset.Parse(fixture.RootElement.GetProperty("scheduledStartDate").GetString()!).UtcDateTime.Ticks) throw new Exception("Scheduled UTC mismatch");
            Console.WriteLine("PASS SQL original scheduled UTC preserved");
            return;
        }
        if (mode == "invites") {
            Id("@sessionId", fixture.RootElement.GetProperty("sessionId"));
            Id("@restrictedId", fixture.RootElement.GetProperty("restrictedId"));
            Id("@author", fixture.RootElement.GetProperty("author"));
            Id("@peer", fixture.RootElement.GetProperty("peer"));
            Id("@outsider", fixture.RootElement.GetProperty("outsider"));
            Id("@member", fixture.RootElement.GetProperty("member"));
            await Check("accepted friendship invitations stored pending", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@sessionId AND IsOrganizer=0 AND Status=0""", 3);
            await Check("single accepted organizer", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@sessionId AND UserId=@author AND IsOrganizer=1 AND Status=1""", 1);
            await Check("no duplicate rows", """SELECT COUNT(*) FROM (SELECT UserId FROM GameSessionPlayers WHERE SessionId=@sessionId GROUP BY UserId HAVING COUNT(*)>1) d""", 0);
            await Check("nonfriend invitations absent", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@restrictedId AND UserId IN (@peer,@outsider)""", 0);
            await Check("declined invitation unchanged after resend attempt", """SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@restrictedId AND UserId=@author AND Status=2""", 1);
            await Check("both new sessions preserved", """SELECT COUNT(*) FROM GameSessions WHERE Id IN (@sessionId,@restrictedId)""", 2);
            return;
        }
        if (mode == "dates") {
            using var accounts = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dataPath, "accounts.json")));
            var peer = accounts.RootElement.EnumerateArray().Single(a => a.GetProperty("Email").GetString() == "participante@meepleboard.test").GetProperty("Id").GetGuid();
            foreach (var item in fixture.RootElement.EnumerateArray()) {
                cmd.Parameters.Clear(); Id("@session", item.GetProperty("id"));
                cmd.CommandText = "SELECT Name,ScheduledStartDate,ResponseDeadline FROM GameSessions WHERE Id=@session";
                var request = item.GetProperty("request");
                await using (var reader = await cmd.ExecuteReaderAsync()) {
                    if (!await reader.ReadAsync() || reader.GetString(0) != request.GetProperty("name").GetString()
                        || reader.GetDateTime(1).Ticks != DateTimeOffset.Parse(request.GetProperty("scheduledStartDate").GetString()!).UtcDateTime.Ticks) throw new Exception("Session identity/UTC mismatch");
                    var deadline = request.TryGetProperty("responseDeadline", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                    if (deadline == null ? !reader.IsDBNull(2) : reader.IsDBNull(2) || reader.GetDateTime(2).Ticks != DateTimeOffset.Parse(deadline).UtcDateTime.Ticks) throw new Exception("Deadline mismatch");
                }
                cmd.Parameters.AddWithValue("@peer", peer);
                await Check("same session name, UTC time, null/custom deadline and pending friend", "SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@peer AND Status=0", 1);
            }
        }
    }
}
