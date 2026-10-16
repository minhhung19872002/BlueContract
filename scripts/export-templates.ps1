<#
.SYNOPSIS
  Ghi các mẫu biểu chuẩn (DOCX) và 2 biểu mẫu import Excel vào thư mục templates/ ở gốc repo.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'templates'
dotnet run --project (Join-Path $root 'backend/src/BlueContract.Api') -- --export-templates $target
if ($LASTEXITCODE -ne 0) { throw 'Xuất mẫu biểu thất bại' }
Write-Host "Đã xuất mẫu biểu vào $target" -ForegroundColor Green
