$ErrorActionPreference = 'Stop'
$fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../.device-tests/session-invite-friends-fixture.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$connection = New-Object System.Data.SqlClient.SqlConnection 'Server=(localdb)\MeepleBoardDeviceTests;Database=MeepleBoard_DeviceTests;Integrated Security=True;TrustServerCertificate=True'
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class=0 AND name=N'MeepleBoardDeviceTestsOwner'"
    if ($command.ExecuteScalar() -ne 'MeepleBoardDeviceTestApi-v1') { throw 'Wrong database' }
    foreach ($field in @('sessionId','restrictedId','author','peer','outsider','member')) {
        [void]$command.Parameters.AddWithValue("@$field", [Guid]$fixture.$field)
    }
    foreach ($check in @(
        @('accepted friendship invitations stored pending', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@sessionId AND IsOrganizer=0 AND Status=0', 3),
        @('single accepted organizer', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@sessionId AND UserId=@author AND IsOrganizer=1 AND Status=1', 1),
        @('no duplicate rows', 'SELECT COUNT(*) FROM (SELECT UserId FROM GameSessionPlayers WHERE SessionId=@sessionId GROUP BY UserId HAVING COUNT(*)>1) d', 0),
        @('nonfriend invitations absent', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@restrictedId AND UserId IN (@peer,@outsider)', 0),
        @('declined invitation unchanged after resend attempt', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@restrictedId AND UserId=@author AND Status=2', 1),
        @('both new sessions preserved', 'SELECT COUNT(*) FROM GameSessions WHERE Id IN (@sessionId,@restrictedId)', 2)
    )) {
        $command.CommandText=$check[1]
        if ($command.ExecuteScalar() -ne $check[2]) { throw "SQL failure: $($check[0])" }
        Write-Output "PASS SQL $($check[0])"
    }
} finally { $connection.Dispose() }
