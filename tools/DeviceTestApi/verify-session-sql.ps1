$ErrorActionPreference = 'Stop'
$dataPath = Join-Path $PSScriptRoot '../../.device-tests'
$fixture = Get-Content -LiteralPath (Join-Path $dataPath 'session-write-fixture.json') -Raw | ConvertFrom-Json
$accounts = Get-Content -LiteralPath (Join-Path $dataPath 'accounts.json') -Raw | ConvertFrom-Json
$connection = New-Object System.Data.SqlClient.SqlConnection 'Server=(localdb)\MeepleBoardDeviceTests;Database=MeepleBoard_DeviceTests;Integrated Security=True;TrustServerCertificate=True'
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class=0 AND name=N'MeepleBoardDeviceTestsOwner'"
    if ($command.ExecuteScalar() -ne 'MeepleBoardDeviceTestApi-v1') { throw 'Not the owned disposable database' }
    [void]$command.Parameters.AddWithValue('@match', [Guid]$fixture.matchId)
    [void]$command.Parameters.AddWithValue('@session', [Guid]$fixture.sessionId)
    [void]$command.Parameters.AddWithValue('@author', [Guid]$accounts[0].Id)
    [void]$command.Parameters.AddWithValue('@peer', [Guid]$accounts[1].Id)
    [void]$command.Parameters.AddWithValue('@member', [Guid]$accounts[3].Id)
    foreach ($check in @(
        @('match creator, session, result and no personal note copy', "SELECT COUNT(*) FROM Matches WHERE Id=@match AND CreatorId=@author AND GameSessionId=@session AND WinnerId=@author AND ScoreSummary=N'Atualizado sem 404' AND Notes IS NULL"),
        @('author score 17', 'SELECT COUNT(*) FROM MatchPlayers WHERE MatchId=@match AND UserId=@author AND Score=17'),
        @('peer score zero', 'SELECT COUNT(*) FROM MatchPlayers WHERE MatchId=@match AND UserId=@peer AND Score=0'),
        @('edited author diary', "SELECT COUNT(*) FROM MatchJournalEntries WHERE MatchId=@match AND UserId=@author AND PersonalRating=9 AND Notes=N'SESSION_PRIVATE_AUTHOR_EDITED' AND Tags=N'editado'"),
        @('preserved peer diary zero', "SELECT COUNT(*) FROM MatchJournalEntries WHERE MatchId=@match AND UserId=@peer AND PersonalRating=0 AND Notes=N'SESSION_PRIVATE_PEER'"),
        @('accepted invitation', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@peer AND Status=1'),
        @('declined invitation', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@member AND Status=2')
    )) {
        $command.CommandText = $check[1]
        if ($command.ExecuteScalar() -ne 1) { throw "SQL check failed: $($check[0])" }
        Write-Output "PASS SQL $($check[0])"
    }
    $command.CommandText = 'SELECT MeepleBoardScore FROM Games WHERE Id=(SELECT GameId FROM Matches WHERE Id=@match)'
    $stored = $command.ExecuteScalar()
    $command.CommandText = @'
SELECT AVG(r.Rating) FROM (
 SELECT CAST(e.PersonalRating AS float) AS Rating FROM MatchJournalEntries e JOIN Matches m ON m.Id=e.MatchId
 WHERE m.GameId=(SELECT GameId FROM Matches WHERE Id=@match) AND e.PersonalRating IS NOT NULL
 UNION ALL
 SELECT CAST(m.PersonalRating AS float) FROM Matches m
 WHERE m.GameId=(SELECT GameId FROM Matches WHERE Id=@match) AND m.PersonalRating IS NOT NULL
 AND NOT EXISTS (SELECT 1 FROM MatchJournalEntries e WHERE e.MatchId=m.Id)
) r
'@
    $expected = [Math]::Round([double]$command.ExecuteScalar() * 10)
    if ($stored -ne $expected) { throw 'SQL rating aggregate is stale' }
    Write-Output 'PASS SQL game rating aggregate matches persisted diary ratings'
} finally { $connection.Dispose() }
