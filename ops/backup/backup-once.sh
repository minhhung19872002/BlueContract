#!/usr/bin/env bash
# One backup into /backups/<yyyymmdd-HHMMSS>/ : db.dump (pg_dump custom format) + files/ (S3 bucket), then retention.
set -euo pipefail
stamp="$(date +%Y%m%d-%H%M%S)"
target="/backups/$stamp"
partial="$target.partial"
mkdir -p "$partial/files"

echo "[$(date -Iseconds)] Sao lưu PostgreSQL ${PGDATABASE}@${PGHOST}"
pg_dump -Fc -f "$partial/db.dump"

echo "[$(date -Iseconds)] Sao lưu bucket ${S3_BUCKET}"
aws s3 sync "s3://${S3_BUCKET}" "$partial/files" --endpoint-url "${S3_ENDPOINT}" --only-show-errors

# A backup only counts once complete: rename at the end, so a failed run never looks like a good one.
mv "$partial" "$target"
date -Iseconds > /backups/last-success

keep="${BACKUP_KEEP:-30}"
ls -1d /backups/2*/ 2>/dev/null | sort -r | tail -n +"$((keep + 1))" | xargs -r rm -rf
rm -rf /backups/*.partial 2>/dev/null || true
echo "[$(date -Iseconds)] Sao lưu hoàn tất: $target ($(du -sh "$target" | cut -f1))"
