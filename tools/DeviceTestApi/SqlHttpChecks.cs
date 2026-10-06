using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeepleBoard.Domain.Entities;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

public static class SqlHttpChecks {
    public static async Task Run(IServiceProvider services, string dataPath) {
        var accounts = JsonSerializer.Deserialize<List<TestAccount>>(await File.ReadAllTextAsync(Path.Combine(dataPath, "accounts.json")))!;
        var tokens = new Dictionary<Guid, string>();
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5099") };
        var passed = 0;
        void Assert(bool value) { if (!value) throw new Exception("SQL/HTTP assertion failed"); }
        async Task Check(string name, Func<Task> body) { await body(); passed++; Console.WriteLine("PASS SQL/HTTP " + name); }
        async Task<string> Send(HttpMethod method, string path, Guid? user, HttpStatusCode status, object? body = null) {
            using var req = new HttpRequestMessage(method, path);
            if (user.HasValue) req.Headers.Authorization = new("Bearer", tokens[user.Value]);
            if (body != null) req.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(req);
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != status) throw new Exception($"{method} {path}: expected {status}, got {response.StatusCode}; response length {text.Length}");
            return text;
        }
        foreach (var a in accounts) {
            var text = await Send(HttpMethod.Post, "/MeepleBoard/auth/login", null, HttpStatusCode.OK, new { email = a.Email, password = a.Password, rememberMe = false, isMobile = true });
            using var json = JsonDocument.Parse(text); tokens[a.Id] = json.RootElement.GetProperty("token").GetString()!;
        }
        Console.WriteLine("Real JWT login verified for four synthetic SQL accounts.");
        var author = accounts[0].Id; var peer = accounts[1].Id; var outsider = accounts[2].Id; var member = accounts[3].Id;
        Guid gameId;
        using (var scope = services.CreateScope()) gameId = await scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>().Games.Where(g => g.BGGId == 990001).Select(g => g.Id).SingleAsync();
        Guid matchId = Guid.Empty;
        await Check("create match persists CreatorId, zero and author note without Matches.Notes copy", async () => {
            var text = await Send(HttpMethod.Post, "/MeepleBoard/matches", author, HttpStatusCode.Created, new {
                gameId, gameName = "Meeple Teste Competitivo", matchDate = DateTime.UtcNow.AddMinutes(-5), isSoloGame = true,
                playerIds = new[] { author, peer }, playerScores = new[] { new { userId = author, score = 17 }, new { userId = peer, score = 0 } }, personalRating = 8, notes = "PRIVATE_SQL_AUTHOR", tags = "teste", scoreSummary = "Resumo fictício"
            });
            using var json = JsonDocument.Parse(text); matchId = json.RootElement.GetProperty("id").GetGuid(); Assert(!text.Contains("PRIVATE_SQL_AUTHOR"));
            using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>();
            var saved = await db.Matches.AsNoTracking().Include(m => m.MatchPlayers).SingleAsync(m => m.Id == matchId);
            Assert(saved.CreatorId == author && saved.Notes == null && saved.MatchPlayers.Single(p => p.UserId == peer).Score == 0);
            Assert(await db.MatchJournalEntries.AnyAsync(e => e.MatchId == matchId && e.UserId == author && e.Notes == "PRIVATE_SQL_AUTHOR"));
        });
        Guid sessionId, campaignId;
        using (var scope = services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>(); var match = await db.Matches.SingleAsync(m => m.Id == matchId);
            var session = new GameSession("Sessão fictícia SQL", author);
            foreach (var id in new[] { author, peer, member }) { var p = new GameSessionPlayer(session.Id, id); p.Accept(); session.Players.Add(p); }
            session.Matches.Add(match); db.GameSessions.Add(session); sessionId = session.Id;
            var campaign = new Campaign("Campanha fictícia SQL", gameId, author); campaign.UpdateNotes("Notas gerais fictícias");
            foreach (var id in new[] { author, peer, member }) { var m = new CampaignMember(campaign.Id, id, id == author); m.Accept(); campaign.Members.Add(m); }
            campaign.CampaignMatches.Add(new CampaignMatch(campaign.Id, matchId, 1, "Encontro fictício")); db.Campaigns.Add(campaign); campaignId = campaign.Id;
            await db.SaveChangesAsync();
        }
        foreach (var origin in new Guid?[] { null, sessionId }) await Check("signed competitive scores and manual lower-score winner " + (origin.HasValue ? "session" : "quick"), async () => {
            var text = await Send(HttpMethod.Post, "/MeepleBoard/matches", author, HttpStatusCode.Created, new {
                gameId, gameName = "Meeple Teste Competitivo", gameSessionId = origin, matchDate = DateTime.UtcNow.AddMinutes(-2),
                isSoloGame = false, winnerId = author, scoresEnabled = true, playerIds = new[] { author, peer },
                playerScores = new[] { new { userId = author, score = -17 }, new { userId = peer, score = 0 } }
            });
            using var created = JsonDocument.Parse(text); var id = created.RootElement.GetProperty("id").GetGuid();
            using var read = JsonDocument.Parse(await Send(HttpMethod.Get, $"/MeepleBoard/matches/{id}", author, HttpStatusCode.OK));
            Assert(read.RootElement.GetProperty("winnerId").GetGuid() == author);
            var players = read.RootElement.GetProperty("players").EnumerateArray().ToArray();
            Assert(players.Single(p => p.GetProperty("userId").GetGuid() == author).GetProperty("score").GetInt32() == -17);
            Assert(players.Single(p => p.GetProperty("userId").GetGuid() == peer).GetProperty("score").GetInt32() == 0);
            using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>();
            Assert(await db.MatchPlayers.AnyAsync(p => p.MatchId == id && p.UserId == author && p.Score == -17));
            Assert(await db.MatchPlayers.AnyAsync(p => p.MatchId == id && p.UserId == peer && p.Score == 0));
            await Send(HttpMethod.Get, $"/MeepleBoard/matches/{id}", outsider, HttpStatusCode.Forbidden);
            if (origin.HasValue) {
                using var sessionRead = JsonDocument.Parse(await Send(HttpMethod.Get, $"/MeepleBoard/session/{sessionId}", author, HttpStatusCode.OK));
                var nested = sessionRead.RootElement.GetProperty("matches").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);
                Assert(nested.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("userId").GetGuid() == author).GetProperty("score").GetInt32() == -17);
            }
        });
        await Check("signed int32 endpoints round-trip through SQL and JSON", async () => {
            var text = await Send(HttpMethod.Post, "/MeepleBoard/matches", author, HttpStatusCode.Created, new {
                gameId, gameName = "Meeple Teste Competitivo", matchDate = DateTime.UtcNow.AddMinutes(-2),
                isSoloGame = false, winnerId = author, scoresEnabled = true, playerIds = new[] { author, peer },
                playerScores = new[] { new { userId = author, score = int.MinValue }, new { userId = peer, score = int.MaxValue } }
            });
            using var created = JsonDocument.Parse(text); var id = created.RootElement.GetProperty("id").GetGuid();
            using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>();
            Assert(await db.MatchPlayers.AnyAsync(p => p.MatchId == id && p.UserId == author && p.Score == int.MinValue));
            Assert(await db.MatchPlayers.AnyAsync(p => p.MatchId == id && p.UserId == peer && p.Score == int.MaxValue));
            using var read = JsonDocument.Parse(await Send(HttpMethod.Get, $"/MeepleBoard/matches/{id}", author, HttpStatusCode.OK));
            Assert(read.RootElement.GetProperty("players").EnumerateArray().Any(p => p.GetProperty("score").GetInt32() == int.MinValue));
        });
        await Check("incomplete/null/disabled/decimal/overflow score requests reject without SQL writes", async () => {
            using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>();
            var before = await db.Matches.CountAsync();
            foreach (var kind in new[] { "missing", "partial", "null", "disabled", "decimal", "positive overflow", "negative overflow" }) {
                object? score = kind switch { "null" => null, "decimal" => 1.5m, "positive overflow" => 2147483648L, "negative overflow" => -2147483649L, _ => 0 };
                var playerScores = kind == "missing" ? Array.Empty<object>() : kind == "partial" ? new object[] { new { userId = author, score = -17 } } :
                    new object[] { new { userId = author, score = -17 }, new { userId = peer, score } };
                await Send(HttpMethod.Post, "/MeepleBoard/matches", author, HttpStatusCode.BadRequest, new {
                    gameId, gameName = "Meeple Teste Competitivo", matchDate = DateTime.UtcNow.AddMinutes(-2), isSoloGame = false,
                    winnerId = author, scoresEnabled = kind != "disabled", playerIds = new[] { author, peer }, playerScores
                });
            }
            Assert(await db.Matches.CountAsync() == before);
        });
        await Check("competitive without scores saves/reloads null and explicit winner", async () => {
            var text = await Send(HttpMethod.Post, "/MeepleBoard/matches", author, HttpStatusCode.Created, new {
                gameId, gameName = "Meeple Teste Competitivo", matchDate = DateTime.UtcNow.AddMinutes(-2), isSoloGame = false,
                winnerId = author, scoresEnabled = false, playerIds = new[] { author, peer }
            });
            using var created = JsonDocument.Parse(text); var id = created.RootElement.GetProperty("id").GetGuid();
            using var read = JsonDocument.Parse(await Send(HttpMethod.Get, $"/MeepleBoard/matches/{id}", author, HttpStatusCode.OK));
            Assert(read.RootElement.GetProperty("winnerId").GetGuid() == author);
            Assert(read.RootElement.GetProperty("players").EnumerateArray().All(p => p.GetProperty("score").ValueKind == JsonValueKind.Null));
        });
        await Check("legacy partial scores stay readable and absent is never filled with zero", async () => {
            var text = await Send(HttpMethod.Post, "/MeepleBoard/matches", author, HttpStatusCode.Created, new {
                gameId, gameName = "Meeple Teste Competitivo", matchDate = DateTime.UtcNow.AddMinutes(-2), isSoloGame = false,
                winnerId = author, scoresEnabled = false, playerIds = new[] { author, peer }
            });
            using var created = JsonDocument.Parse(text); var id = created.RootElement.GetProperty("id").GetGuid();
            // Reproduce a pre-existing partial record only in the marked DeviceTests database.
            using (var scope = services.CreateScope()) {
                var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>();
                (await db.MatchPlayers.SingleAsync(p => p.MatchId == id && p.UserId == author)).UpdateScore(-5);
                await db.SaveChangesAsync();
            }
            using var read = JsonDocument.Parse(await Send(HttpMethod.Get, $"/MeepleBoard/matches/{id}", author, HttpStatusCode.OK));
            var players = read.RootElement.GetProperty("players").EnumerateArray().ToArray();
            Assert(players.Single(p => p.GetProperty("userId").GetGuid() == author).GetProperty("score").GetInt32() == -5);
            Assert(players.Single(p => p.GetProperty("userId").GetGuid() == peer).GetProperty("score").ValueKind == JsonValueKind.Null);
        });
        var detail = $"/MeepleBoard/matches/{matchId}"; var journal = $"/MeepleBoard/campaigns/matches/{matchId}/journal";
        await Check("journal writes use JWT actor even with forged user ID", async () => {
            await Send(HttpMethod.Put, journal, peer, HttpStatusCode.OK, new { userId = author, personalRating = 0, notes = "PRIVATE_SQL_PEER", tags = "partilhada" });
            using var scope = services.CreateScope(); Assert(await scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>().MatchJournalEntries.AnyAsync(e => e.MatchId == matchId && e.UserId == peer && e.Notes == "PRIVATE_SQL_PEER" && e.PersonalRating == 0));
        });
        foreach (var (name, id, allowed) in new (string, Guid?, bool)[] { ("visitor", null, false), ("author", author, true), ("participant", peer, true), ("outsider", outsider, false), ("non-participating session/campaign member", member, false) }) {
            await Check(name + " reads", async () => {
                var status = id == null ? HttpStatusCode.Unauthorized : allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
                var summary = await Send(HttpMethod.Get, detail, id, status); var entries = await Send(HttpMethod.Get, journal, id, status);
                Assert(!summary.Contains("PRIVATE_SQL_"));
                if (allowed) Assert(entries.Contains(id == author ? "PRIVATE_SQL_AUTHOR" : "PRIVATE_SQL_PEER") && !entries.Contains(id == author ? "PRIVATE_SQL_PEER" : "PRIVATE_SQL_AUTHOR"));
            });
            if (!allowed) await Check(name + " writes denied", async () => {
                var status = id == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
                await Send(HttpMethod.Put, journal, id, status, new { personalRating = 7, notes = "FORBIDDEN" });
                await Send(HttpMethod.Delete, detail, id, status);
            });
        }
        await Check("peer writes denied; creator update returns 204 and persists", async () => {
            var body = new { id = matchId, gameId, gameName = "Meeple Teste Competitivo", players = new[] { new { userId = author, userName = "Teste autor" } }, matchDate = DateTime.UtcNow.AddMinutes(-5), isSoloGame = true, scoreSummary = "Atualizado SQL" };
            await Send(HttpMethod.Put, detail, peer, HttpStatusCode.Forbidden, body); await Send(HttpMethod.Delete, detail, peer, HttpStatusCode.Forbidden);
            await Send(HttpMethod.Put, detail, author, HttpStatusCode.NoContent, body);
            using var scope = services.CreateScope();
            Assert((await scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>().Matches.AsNoTracking().SingleAsync(m => m.Id == matchId)).ScoreSummary == "Atualizado SQL");
            await Send(HttpMethod.Get, detail, author, HttpStatusCode.OK);
        });
        await Check("session/campaign membership does not leak matches or notes", async () => {
            foreach (var path in new[] { $"/MeepleBoard/session/{sessionId}", $"/MeepleBoard/campaigns/{campaignId}" }) {
                var text = await Send(HttpMethod.Get, path, member, HttpStatusCode.OK); Assert(!text.Contains("PRIVATE_SQL_"));
                Assert(path.Contains("/session/") ? !text.Contains(matchId.ToString()) : text.Contains("\"canReadJournal\":false"));
            }
            foreach (var path in new[] { "/MeepleBoard/matches", "/MeepleBoard/matches/pending-journal", $"/MeepleBoard/matches/history/game/{gameId}" }) {
                var text = await Send(HttpMethod.Get, path, outsider, HttpStatusCode.NoContent); Assert(!text.Contains(matchId.ToString()) && !text.Contains("PRIVATE_SQL_"));
            }
        });
        var library = $"/MeepleBoard/users/{author}/games";
        using (var scope = services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>(); db.UserGameLibraries.RemoveRange(await db.UserGameLibraries.Where(l => l.UserId == author && l.GameId == gameId).ToListAsync()); await db.SaveChangesAsync(); }
        await Check("library create with absent price", async () => { await Send(HttpMethod.Post, library, author, HttpStatusCode.Created, new { gameId, gameName = "Meeple Teste Competitivo", status = 1 }); });
        foreach (decimal? price in new decimal?[] { 35.50m, 0, null }) await Check("library persist/reload price " + (price?.ToString() ?? "null"), async () => {
            await Send(HttpMethod.Patch, library + "/" + gameId, author, HttpStatusCode.NoContent, new { status = 1, pricePaid = price });
            var text = await Send(HttpMethod.Get, library, author, HttpStatusCode.OK); using var json = JsonDocument.Parse(text); var entry = json.RootElement.EnumerateArray().Single(e => e.GetProperty("gameId").GetGuid() == gameId);
            Assert(price.HasValue ? entry.GetProperty("pricePaid").GetDecimal() == price : entry.GetProperty("pricePaid").ValueKind == JsonValueKind.Null);
            using var scope = services.CreateScope(); Assert((await scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>().UserGameLibraries.AsNoTracking().SingleAsync(e => e.UserId == author && e.GameId == gameId)).PricePaid == price);
        });
        await Check("invalid price and outsider edit denied", async () => {
            await Send(HttpMethod.Patch, library + "/" + gameId, author, HttpStatusCode.BadRequest, new { status = 1, pricePaid = -1 });
            await Send(HttpMethod.Patch, library + "/" + gameId, outsider, HttpStatusCode.Forbidden, new { status = 1, pricePaid = 999 });
        });
        await Check("library removal preserves SQL catalogue, match and score", async () => {
            await Send(HttpMethod.Delete, library + "/" + gameId, author, HttpStatusCode.NoContent);
            using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>();
            Assert(await db.Games.AnyAsync(g => g.Id == gameId) && await db.Matches.AnyAsync(m => m.Id == matchId) && await db.MatchPlayers.AnyAsync(p => p.MatchId == matchId && p.UserId == peer && p.Score == 0));
        });
        await Check("own note edit succeeds and peer is preserved", async () => {
            await Send(HttpMethod.Put, journal, author, HttpStatusCode.OK, new { personalRating = 8, notes = (string?)null, tags = "teste" });
            using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>();
            Assert((await db.MatchJournalEntries.SingleAsync(e => e.MatchId == matchId && e.UserId == author)).Notes == null);
            Assert((await db.MatchJournalEntries.SingleAsync(e => e.MatchId == matchId && e.UserId == peer)).Notes == "PRIVATE_SQL_PEER");
            var response = await Send(HttpMethod.Get, journal, author, HttpStatusCode.OK);
            Assert(!response.Contains("PRIVATE_SQL_PEER"));
        });
        await Check("creator deletes a separate SQL match", async () => {
            var text = await Send(HttpMethod.Post, "/MeepleBoard/matches", author, HttpStatusCode.Created, new { gameId, gameName = "Meeple Teste Competitivo", matchDate = DateTime.UtcNow.AddMinutes(-1), isSoloGame = true, playerIds = new[] { author } });
            using var json = JsonDocument.Parse(text); var id = json.RootElement.GetProperty("id").GetGuid(); await Send(HttpMethod.Delete, $"/MeepleBoard/matches/{id}", author, HttpStatusCode.NoContent);
            using var scope = services.CreateScope(); Assert(!await scope.ServiceProvider.GetRequiredService<MeepleBoardDbContext>().Matches.AnyAsync(m => m.Id == id));
        });
        Console.WriteLine($"{passed} real SQL/HTTP scenarios passed. Synthetic data only in MeepleBoard_DeviceTests.");
    }
}
