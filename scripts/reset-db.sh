#!/usr/bin/env bash
# DESTROYS the local database and object storage, then recreates them (migrate + seed run on API start).
# Usage: scripts/reset-db.sh [--force]
set -euo pipefail
cd "$(dirname "$0")/.."

if [ "${1:-}" != "--force" ]; then
  read -r -p 'Thao tác này XÓA TOÀN BỘ dữ liệu PostgreSQL và kho lưu trữ tệp (S3) cục bộ. Gõ "yes" để tiếp tục: ' answer
  [ "$answer" = "yes" ] || { echo "Đã hủy."; exit 0; }
fi

docker compose stop api postgres storage
docker compose rm -f -v postgres storage
docker volume rm blueprocure_postgres-data blueprocure_storage-data >/dev/null 2>&1 || true
docker compose up -d postgres storage
docker compose up -d api
echo "Đã reset. API sẽ tự chạy migration và seed khi khởi động."
