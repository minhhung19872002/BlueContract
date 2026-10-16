<#
.SYNOPSIS
  Restores a backup made by the 'backup' container / scripts\backup.ps1: database recreated from db.dump, file bucket
  made identical to the saved files. Stops the API during the restore.
.EXAMPLE
  scripts\restore.ps1 -Stamp 20261017-013000
#>
param([Parameter(Mandatory)][string]$Stamp, [switch]$Yes)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$dir = './backups'
if (Test-Path '.env') {
    $line = Get-Content '.env' | Where-Object { $_ -match '^BACKUP_DIR=(.+)$' } | Select-Object -First 1
    if ($line) { $dir = ($line -split '=', 2)[1] }
}
if (-not (Test-Path (Join-Path $dir "$Stamp/db.dump"))) { throw "Không tìm thấy $dir/$Stamp/db.dump" }
if (-not $Yes) {
    $answer = Read-Host "Toàn bộ dữ liệu hiện tại sẽ bị thay bằng bản sao lưu $Stamp. Gõ KHOI PHUC để tiếp tục"
    if ($answer -ne 'KHOI PHUC') { Write-Host 'Đã hủy.'; exit 1 }
}
docker compose up -d backup | Out-Null
docker compose stop api
try {
    docker compose exec -T backup restore-once.sh $Stamp
    if ($LASTEXITCODE -ne 0) { throw 'Khôi phục thất bại' }
}
finally {
    docker compose start api | Out-Null
}
Write-Host 'API đã chạy lại. Vào Quản trị → Kiểm tra toàn vẹn → Kiểm tra ngay để đối chiếu SHA-256 của mọi tệp.'
