#!/usr/bin/env bash
# Restores a backup made by the 'backup' container / scripts/backup.*: database recreated from db.dump, file bucket made
# identical to the saved files. Stops the API during the restore. Usage: scripts/restore.sh <yyyymmdd-HHMMSS> [--yes]
set -euo pipefail
cd "$(dirname "$0")/.."
stamp="${1:?Cách dùng: scripts/restore.sh <thư mục sao lưu, VD 20261017-013000> [--yes]}"
dir="${BACKUP_DIR:-./backups}"
if [ -f .env ]; then dir="$(grep -E '^BACKUP_DIR=' .env | cut -d= -f2- || true)"; dir="${dir:-./backups}"; fi
[ -f "$dir/$stamp/db.dump" ] || { echo "Không tìm thấy $dir/$stamp/db.dump" >&2; exit 2; }
if [ "${2:-}" != "--yes" ]; then
  read -r -p "Toàn bộ dữ liệu hiện tại sẽ bị thay bằng bản sao lưu $stamp. Gõ KHOI PHUC để tiếp tục: " answer
  [ "$answer" = "KHOI PHUC" ] || { echo "Đã hủy."; exit 1; }
fi
docker compose up -d backup >/dev/null
docker compose stop api
trap 'docker compose start api >/dev/null' EXIT
docker compose exec -T backup restore-once.sh "$stamp"
docker compose start api >/dev/null
trap - EXIT
echo "API đã chạy lại. Vào Quản trị → Kiểm tra toàn vẹn → Kiểm tra ngay để đối chiếu SHA-256 của mọi tệp."
