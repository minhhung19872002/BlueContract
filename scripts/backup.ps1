<#
.SYNOPSIS
  Back up PostgreSQL (pg_dump custom format) and mirror the MinIO bucket into backups/<timestamp>/.
  Keeps the newest $Keep backups (default 30). Schedule daily with Windows Task Scheduler.
#>
param(
    [string]$OutDir = 'backups',
    [int]$Keep = 30
)
$ErrorActionPreference = 'Stop'
function Get-EnvValue([hashtable]$Map, [string]$Key, [string]$Default) {
    if ($Map.ContainsKey($Key) -and $Map[$Key]) { return $Map[$Key] } else { return $Default }
}
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$envMap = @{}
if (Test-Path '.env') {
    Get-Content '.env' | Where-Object { $_ -match '^\s*([A-Z_]+)=(.*)$' } | ForEach-Object { $envMap[$Matches[1]] = $Matches[2] }
}
$db = Get-EnvValue $envMap 'POSTGRES_DB' 'blueprocure'
$user = Get-EnvValue $envMap 'POSTGRES_USER' 'blueprocure'
$minioUser = Get-EnvValue $envMap 'MINIO_ROOT_USER' 'minioadmin'
$minioPass = Get-EnvValue $envMap 'MINIO_ROOT_PASSWORD' 'minioadmin_dev'
$bucket = Get-EnvValue $envMap 'MINIO_BUCKET' 'blueprocure'

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path (Join-Path $root $OutDir) $stamp
New-Item -ItemType Directory -Force -Path (Join-Path $target 'minio') | Out-Null

Write-Host "Sao lưu PostgreSQL → $target\db.dump"
# Dump inside the container then copy out, so binary output is not mangled by the PowerShell pipeline.
docker compose exec -T postgres pg_dump -U $user -d $db -Fc -f /tmp/blueprocure.dump
if ($LASTEXITCODE -ne 0) { throw 'pg_dump thất bại' }
$pgContainer = (docker compose ps -q postgres).Trim()
docker cp "${pgContainer}:/tmp/blueprocure.dump" (Join-Path $target 'db.dump')
docker compose exec -T postgres rm -f /tmp/blueprocure.dump

Write-Host "Sao lưu MinIO bucket '$bucket' → $target\minio"
$network = (docker inspect $pgContainer --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}').Trim()
docker run --rm --network $network -v "${target}\minio:/backup" `
    -e "MC_HOST_src=http://${minioUser}:${minioPass}@minio:9000" `
    --entrypoint mc minio/minio:RELEASE.2025-04-22T22-12-26Z mirror --overwrite "src/$bucket" /backup
if ($LASTEXITCODE -ne 0) { throw 'mc mirror thất bại' }

# Retention.
Get-ChildItem (Join-Path $root $OutDir) -Directory | Sort-Object Name -Descending | Select-Object -Skip $Keep |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force; Write-Host "Đã xóa bản cũ $($_.Name)" }

Write-Host "Sao lưu hoàn tất: $target" -ForegroundColor Green
