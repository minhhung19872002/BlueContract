<#
.SYNOPSIS
  Start the full BlueProcure stack with Docker Compose (Windows / PowerShell).
.PARAMETER Seq
  Also start the optional Seq log server.
.PARAMETER NoBuild
  Skip rebuilding api/web images.
#>
param(
    [switch]$Seq,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
function Get-EnvValue([hashtable]$Map, [string]$Key, [string]$Default) {
    if ($Map.ContainsKey($Key) -and $Map[$Key]) { return $Map[$Key] } else { return $Default }
}
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path '.env')) {
    Copy-Item '.env.example' '.env'
    Write-Host 'Đã tạo .env từ .env.example' -ForegroundColor Yellow
}

# Use repo git hooks (pre-commit format checks).
if (Test-Path '.git') { git config core.hooksPath .githooks }

$composeArgs = @('compose')
if ($Seq) { $composeArgs += @('--profile', 'seq') }
$composeArgs += @('up', '-d')
if (-not $NoBuild) { $composeArgs += '--build' }

docker @composeArgs
if ($LASTEXITCODE -ne 0) { throw 'docker compose up thất bại' }

$envMap = @{}
Get-Content '.env' | Where-Object { $_ -match '^\s*([A-Z_]+)=(.*)$' } | ForEach-Object { $envMap[$Matches[1]] = $Matches[2] }
$webPort = Get-EnvValue $envMap 'WEB_PORT' '47080'

Write-Host "Đang chờ API sẵn sàng..." -NoNewline
for ($i = 0; $i -lt 60; $i++) {
    try {
        # Throws on non-2xx in Windows PowerShell 5.1 and PowerShell 7 alike.
        $null = Invoke-WebRequest "http://127.0.0.1:$webPort/health/ready" -UseBasicParsing -TimeoutSec 3
        Write-Host ' OK' -ForegroundColor Green; break
    } catch { }
    Write-Host '.' -NoNewline
    Start-Sleep -Seconds 2
}

Write-Host ''
Write-Host "Web:            http://localhost:$webPort"
Write-Host "API docs:       http://localhost:$webPort/docs"
Write-Host "Mailpit:        http://localhost:$(Get-EnvValue $envMap 'MAILPIT_UI_PORT' '47025')"
Write-Host "MinIO console:  http://localhost:$(Get-EnvValue $envMap 'MINIO_CONSOLE_PORT' '47901')"
Write-Host "ONLYOFFICE:     http://localhost:$(Get-EnvValue $envMap 'ONLYOFFICE_PORT' '47082')"
if ($Seq) { Write-Host "Seq:            http://localhost:$(Get-EnvValue $envMap 'SEQ_PORT' '47341')" }
