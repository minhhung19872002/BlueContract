<#
.SYNOPSIS
  DESTROYS the local database and object storage, then recreates them (migrate + seed run on API start).
.PARAMETER Force
  Skip the confirmation prompt.
#>
param([switch]$Force)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

if (-not $Force) {
    $answer = Read-Host 'Thao tác này XÓA TOÀN BỘ dữ liệu PostgreSQL và MinIO cục bộ. Gõ "yes" để tiếp tục'
    if ($answer -ne 'yes') { Write-Host 'Đã hủy.'; exit 0 }
}

docker compose stop api postgres minio
docker compose rm -f -v postgres minio
foreach ($v in 'blueprocure_postgres-data', 'blueprocure_minio-data') {
    docker volume rm $v 2>$null | Out-Null
}
docker compose up -d postgres minio
docker compose up -d api
Write-Host 'Đã reset. API sẽ tự chạy migration và seed khi khởi động.' -ForegroundColor Green
