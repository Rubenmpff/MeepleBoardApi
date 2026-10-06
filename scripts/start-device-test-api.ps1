param([switch]$Verify, [switch]$AuditOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Verify -and -not $AuditOnly) {
    try {
        $health = Invoke-RestMethod -Uri 'http://127.0.0.1:5099/device-test/health' -TimeoutSec 2
        if ($health.environment -eq 'DeviceTests' -and $health.database -eq 'MeepleBoard_DeviceTests') {
            Write-Host 'A API de testes ja esta em execucao na porta 5099.'
            Write-Host "Contas: $(Join-Path $projectRoot '.device-tests/accounts.json')"
            return
        }
    } catch { }
}
if (-not $AuditOnly) {
    $instances = & SqlLocalDB.exe info
    if ($instances -notcontains 'MeepleBoardDeviceTests') { & SqlLocalDB.exe create MeepleBoardDeviceTests -s }
    else { & SqlLocalDB.exe start MeepleBoardDeviceTests }
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel iniciar a instancia LocalDB de testes.' }
}
$runArgs = @('run', '--project', (Join-Path $projectRoot 'tools/DeviceTestApi/DeviceTestApi.csproj'), '--configuration', 'DeviceTests', '--no-launch-profile', '--', "--data=$(Join-Path $projectRoot '.device-tests')")
if ($Verify) { $runArgs += '--verify' }
if ($AuditOnly) { $runArgs += '--audit-only' }
Write-Host 'Ambiente isolado: LocalDB MeepleBoardDeviceTests / MeepleBoard_DeviceTests; API porta 5099.'
Push-Location $projectRoot
try { & dotnet @runArgs; if ($LASTEXITCODE -ne 0) { throw 'A API/verificacao de testes terminou com erro.' } }
finally { Pop-Location }
