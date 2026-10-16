<#
.SYNOPSIS
  Chạy kiểm thử E2E (Playwright) trên stack docker compose đang chạy (scripts/dev-up.ps1 trước).
  Stack cần AUTH_LOGIN_RATE_LIMIT=1000 trong .env (E2E đăng nhập nhiều tài khoản từ cùng một IP).
.PARAMETER BaseUrl
  Địa chỉ web (mặc định http://127.0.0.1:47080).
#>
param([string]$BaseUrl = 'http://127.0.0.1:47080')
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path (Split-Path -Parent $PSScriptRoot) 'e2e')
if (-not (Test-Path node_modules)) { npm ci; npx playwright install chromium }
$env:BASE_URL = $BaseUrl
npx playwright test
if ($LASTEXITCODE -ne 0) { throw 'E2E thất bại — xem e2e/playwright-report' }
Write-Host 'E2E đạt.' -ForegroundColor Green
