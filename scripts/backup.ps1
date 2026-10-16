<#
.SYNOPSIS
  Back up PostgreSQL (pg_dump custom format) and sync the S3 bucket into backups/<timestamp>/.
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
$s3Key = Get-EnvValue $envMap 'S3_ACCESS_KEY' 'blueprocure'
$s3Secret = Get-EnvValue $envMap 'S3_SECRET_KEY' 'blueprocure_s3_dev'
$bucket = Get-EnvValue $envMap 'S3_BUCKET' 'blueprocure'

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path (Join-Path $root $OutDir) $stamp
New-Item -ItemType Directory -Force -Path (Join-Path $target 'files') | Out-Null

Write-Host "Sao lưu PostgreSQL → $target\db.dump"
# Dump inside the container then copy out, so binary output is not mangled by the PowerShell pipeline.
docker compose exec -T postgres pg_dump -U $user -d $db -Fc -f /tmp/blueprocure.dump
if ($LASTEXITCODE -ne 0) { throw 'pg_dump thất bại' }
$pgContainer = (docker compose ps -q postgres).Trim()
docker cp "${pgContainer}:/tmp/blueprocure.dump" (Join-Path $target 'db.dump')
docker compose exec -T postgres rm -f /tmp/blueprocure.dump

Write-Host "Sao lưu bucket '$bucket' → $target\files"
$network = (docker inspect $pgContainer --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}').Trim()
docker run --rm --network $network -v "${target}\files:/backup" `
    -e "AWS_ACCESS_KEY_ID=$s3Key" -e "AWS_SECRET_ACCESS_KEY=$s3Secret" -e "AWS_DEFAULT_REGION=us-east-1" `
    amazon/aws-cli:2.27.0 s3 sync "s3://$bucket" /backup --endpoint-url http://storage:8333 --only-show-errors
if ($LASTEXITCODE -ne 0) { throw 'Sao lưu tệp thất bại' }

# Retention.
Get-ChildItem (Join-Path $root $OutDir) -Directory | Sort-Object Name -Descending | Select-Object -Skip $Keep |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force; Write-Host "Đã xóa bản cũ $($_.Name)" }

Write-Host "Sao lưu hoàn tất: $target" -ForegroundColor Green
