$ErrorActionPreference = 'Stop'
$dataPath = Join-Path $PSScriptRoot '../../.device-tests'
$fixtures = Get-Content -LiteralPath (Join-Path $dataPath 'session-date-fixture.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$accounts = Get-Content -LiteralPath (Join-Path $dataPath 'accounts.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$peer = $accounts | Where-Object Email -eq 'participante@meepleboard.test'
$connection = New-Object System.Data.SqlClient.SqlConnection 'Server=(localdb)\MeepleBoardDeviceTests;Database=MeepleBoard_DeviceTests;Integrated Security=True;TrustServerCertificate=True'
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class=0 AND name=N'MeepleBoardDeviceTestsOwner'"
    if ($command.ExecuteScalar() -ne 'MeepleBoardDeviceTestApi-v1') { throw 'Not the owned disposable database' }
    foreach ($fixture in $fixtures) {
        $command.Parameters.Clear()
        [void]$command.Parameters.AddWithValue('@session', [Guid]$fixture.id)
        $command.CommandText = 'SELECT Name,ScheduledStartDate,ResponseDeadline FROM GameSessions WHERE Id=@session'
        $reader = $command.ExecuteReader()
        if (-not $reader.Read()) { throw 'Created session missing' }
        if ($reader.GetString(0) -ne $fixture.request.name) { throw 'Session identity mismatch' }
        $expected = [DateTimeOffset]::Parse($fixture.request.scheduledStartDate).UtcDateTime
        if ($reader.GetDateTime(1).Ticks -ne $expected.Ticks) { throw 'Scheduled UTC value mismatch' }
        if ($fixture.request.responseDeadline) {
            if ($reader.IsDBNull(2) -or $reader.GetDateTime(2).Ticks -ne [DateTimeOffset]::Parse($fixture.request.responseDeadline).UtcDateTime.Ticks) { throw 'Custom deadline mismatch' }
        } elseif (-not $reader.IsDBNull(2)) { throw 'Unexpected stored custom deadline' }
        $reader.Close()
        [void]$command.Parameters.AddWithValue('@peer', [Guid]$peer.Id)
        $command.CommandText = 'SELECT COUNT(*) FROM GameSessionPlayers WHERE SessionId=@session AND UserId=@peer AND Status=0'
        if ($command.ExecuteScalar() -ne 1) { throw 'Selected invitee was not stored pending' }
        Write-Output 'PASS SQL: same session name, UTC time, null/custom deadline and selected pending friend'
    }
} finally { $connection.Dispose() }
