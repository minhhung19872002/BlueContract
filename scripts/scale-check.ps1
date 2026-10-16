<#
.SYNOPSIS
  Runs the API on N instances behind nginx and checks that realtime notifications and signing sessions cross
  instances (E2E #6, ADR-061).
.EXAMPLE
  scripts\scale-check.ps1 -Instances 2
#>
param([int]$Instances = 2)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$port = if ($env:WEB_PORT) { $env:WEB_PORT } else { '47080' }
docker compose up -d --scale "api=$Instances" api
docker compose restart web   # nginx resolves "api" to every instance when it starts
Write-Host "Waiting for $Instances API instances…"
for ($i = 0; $i -lt 60; $i++) {
    try { Invoke-WebRequest "http://127.0.0.1:$port/health/ready" -UseBasicParsing | Out-Null; break } catch { Start-Sleep 2 }
}
Start-Sleep 10
Push-Location e2e
try {
    $env:BASE_URL = "http://127.0.0.1:$port"
    npx playwright test tests/e2e6-scale.spec.ts --reporter=line
    if ($LASTEXITCODE -ne 0) { throw 'scale check failed' }
}
finally { Pop-Location }
for ($i = 1; $i -le $Instances; $i++) {
    $count = (docker compose logs --index $i api --since 5m 2>&1 | Select-String 'HTTP').Count
    Write-Host "api-$i requests: $count"
}
