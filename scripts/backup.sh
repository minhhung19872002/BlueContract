#!/usr/bin/env bash
# Backs up now (PostgreSQL pg_dump + file bucket) into BACKUP_DIR/<timestamp>/ through the 'backup' container, which
# also does it automatically every day at BACKUP_TIME. Keeps the newest BACKUP_KEEP copies (default 30).
# Usage: scripts/backup.sh
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose up -d backup >/dev/null
docker compose exec -T backup backup-once.sh
