<#
.SYNOPSIS
  Backs up now (PostgreSQL pg_dump + file bucket) into BACKUP_DIR/<timestamp>/ through the 'backup' container, which
  also does it automatically every day at BACKUP_TIME. Keeps the newest BACKUP_KEEP copies (default 30).
#>
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
docker compose up -d backup | Out-Null
docker compose exec -T backup backup-once.sh
if ($LASTEXITCODE -ne 0) { throw 'Sao lưu thất bại' }
