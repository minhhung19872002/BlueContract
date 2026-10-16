<#
.SYNOPSIS
  Run migrations + idempotent seed inside the running api container, then exit.
.PARAMETER ResetDemo
  Also wipe and recreate demo business data (available once demo data exists, Phase 1+).
#>
param([switch]$ResetDemo)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$appArgs = @('BlueProcure.Api.dll', '--seed-only')
if ($ResetDemo) { $appArgs += '--reset-demo' }

docker compose exec -T api dotnet @appArgs
if ($LASTEXITCODE -ne 0) { throw 'Seed thất bại' }
Write-Host 'Seed hoàn tất.' -ForegroundColor Green
