#!/usr/bin/env bash
# Restores /backups/<stamp>: recreates the database from db.dump and makes the bucket exactly the saved files.
# The API must be stopped (no writes during the restore). Usage: restore-once.sh <stamp>
set -euo pipefail
src="/backups/${1:?Thiếu tên bản sao lưu (thư mục trong backups/)}"
[ -f "$src/db.dump" ] && [ -d "$src/files" ] || { echo "Không phải bản sao lưu đầy đủ: $src" >&2; exit 2; }

echo "[$(date -Iseconds)] Khôi phục cơ sở dữ liệu ${PGDATABASE} từ $src/db.dump"
psql -d postgres -v ON_ERROR_STOP=1 -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${PGDATABASE}' AND pid <> pg_backend_pid();" >/dev/null
dropdb --if-exists "${PGDATABASE}"
createdb "${PGDATABASE}"
pg_restore --no-owner --exit-on-error -d "${PGDATABASE}" "$src/db.dump"

echo "[$(date -Iseconds)] Khôi phục kho tệp ${S3_BUCKET}"
aws s3 mb "s3://${S3_BUCKET}" --endpoint-url "${S3_ENDPOINT}" >/dev/null 2>&1 || true
aws s3 sync "$src/files" "s3://${S3_BUCKET}" --endpoint-url "${S3_ENDPOINT}" --delete --only-show-errors

echo "[$(date -Iseconds)] Khôi phục xong: $(psql -tA -d "${PGDATABASE}" -c 'SELECT count(*) FROM contracts') hợp đồng, $(find "$src/files" -type f | wc -l) tệp."
