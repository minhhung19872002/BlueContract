#!/usr/bin/env bash
# Chạy kiểm thử E2E (Playwright) trên stack docker compose đang chạy (scripts/dev-up.sh trước).
set -euo pipefail
cd "$(dirname "$0")/../e2e"
[ -d node_modules ] || { npm ci; npx playwright install --with-deps chromium; }
BASE_URL="${BASE_URL:-http://127.0.0.1:47080}" npx playwright test