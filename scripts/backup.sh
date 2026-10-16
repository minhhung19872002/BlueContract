#!/usr/bin/env bash
# Back up PostgreSQL (pg_dump custom format) and mirror the MinIO bucket into backups/<timestamp>/.
# Keeps the newest KEEP backups (default 30). Schedule daily with cron.
# Usage: scripts/backup.sh [out_dir] [keep]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

out_dir="${1:-backups}"
keep="${2:-30}"
if [ -f .env ]; then set -a; . ./.env; set +a; fi
db="${POSTGRES_DB:-blueprocure}"
user="${POSTGRES_USER:-blueprocure}"
bucket="${MINIO_BUCKET:-blueprocure}"

stamp="$(date +%Y%m%d-%H%M%S)"
target="$root/$out_dir/$stamp"
mkdir -p "$target/minio"

echo "Sao lưu PostgreSQL → $target/db.dump"
docker compose exec -T postgres pg_dump -U "$user" -d "$db" -Fc > "$target/db.dump"

echo "Sao lưu MinIO bucket '$bucket' → $target/minio"
pg_container="$(docker compose ps -q postgres)"
network="$(docker inspect "$pg_container" --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}')"
docker run --rm --network "$network" -v "$target/minio:/backup" \
  -e "MC_HOST_src=http://${MINIO_ROOT_USER:-minioadmin}:${MINIO_ROOT_PASSWORD:-minioadmin_dev}@minio:9000" \
  --entrypoint mc minio/minio:RELEASE.2025-04-22T22-12-26Z mirror --overwrite "src/$bucket" /backup

# Retention.
ls -1d "$root/$out_dir"/*/ 2>/dev/null | sort -r | tail -n +"$((keep + 1))" | xargs -r rm -rf

echo "Sao lưu hoàn tất: $target"
