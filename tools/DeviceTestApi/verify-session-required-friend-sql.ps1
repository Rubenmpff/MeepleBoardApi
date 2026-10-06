param([switch]$Cancelled)
$ErrorActionPreference = 'Stop'
$fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../.device-tests/session-rule-fixture.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$connection = New-Object System.Data.SqlClient.SqlConnection 'Server=(localdb)\MeepleBoardDeviceTests;Database=MeepleBoard_DeviceTests;Integrated Security=True;TrustServerCertificate=True'
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class=0 AND name=N'MeepleBoardDeviceTestsOwner'"
    if ($command.ExecuteScalar() -ne 'MeepleBoardDeviceTestApi-v1') { throw 'Wrong database' }
    [void]$command.Parameters.AddWithValue('@session', [Guid]$fixture.sessionId)
    [void]$command.Parameters.AddWithValue('@rejected', $fixture.rejectedName)
    [void]$command.Parameters.AddWithValue('@peer', [Guid]$fixture.peer)
    [void]$command.Parameters.AddWithValue('@member', [Guid]$fixture.member)
    [void]$command.Parameters.AddWithValue('@author', [Guid]$fixture.author)
    $checks = if ($Cancelled) { @(
        @('cancelled fixture deleted', 'SELECT COUNT(*) FROM GameSessions WHERE Id=@session', 0),
        @('no orphaned invitations', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session', 0)
    ) } else { @(
        @('rejected requests saved no session', 'SELECT COUNT(*) FROM GameSessions WHERE Name=@rejected', 0),
        @('session exists without custom deadline', 'SELECT COUNT(*) FROM GameSessions WHERE Id=@session AND IsCancelled=0 AND ResponseDeadline IS NULL', 1),
        @('one accepted organizer', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@author AND IsOrganizer=1 AND Status=1', 1),
        @('original friend declined', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@peer AND Status=2', 1),
        @('replacement friend pending', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@member AND Status=0', 1),
        @('no duplicate memberships', 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session', 3)
    ) }
    foreach ($check in $checks) {
        $command.CommandText = $check[1]
        if ($command.ExecuteScalar() -ne $check[2]) { throw "SQL failure: $($check[0])" }
        Write-Output "PASS SQL $($check[0])"
    }
    if (-not $Cancelled) {
    $command.CommandText = 'SELECT ScheduledStartDate FROM GameSessions WHERE Id=@session'
    if ($command.ExecuteScalar().Ticks -ne [DateTimeOffset]::Parse($fixture.scheduledStartDate).UtcDateTime.Ticks) { throw 'Scheduled UTC mismatch' }
    Write-Output 'PASS SQL original scheduled UTC preserved'
    }
} finally { $connection.Dispose() }
