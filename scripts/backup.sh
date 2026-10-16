#!/usr/bin/env bash
# Back up PostgreSQL (pg_dump custom format) and sync the S3 bucket into backups/<timestamp>/.
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
bucket="${S3_BUCKET:-blueprocure}"

stamp="$(date +%Y%m%d-%H%M%S)"
target="$root/$out_dir/$stamp"
mkdir -p "$target/files"

echo "Sao lưu PostgreSQL → $target/db.dump"
docker compose exec -T postgres pg_dump -U "$user" -d "$db" -Fc > "$target/db.dump"

echo "Sao lưu bucket '$bucket' → $target/files"
pg_container="$(docker compose ps -q postgres)"
network="$(docker inspect "$pg_container" --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}')"
docker run --rm --network "$network" -v "$target/files:/backup" \
  -e "AWS_ACCESS_KEY_ID=${S3_ACCESS_KEY:-blueprocure}" -e "AWS_SECRET_ACCESS_KEY=${S3_SECRET_KEY:-blueprocure_s3_dev}" -e AWS_DEFAULT_REGION=us-east-1 \
  amazon/aws-cli:2.27.0 s3 sync "s3://$bucket" /backup --endpoint-url http://storage:8333 --only-show-errors

# Retention.
ls -1d "$root/$out_dir"/*/ 2>/dev/null | sort -r | tail -n +"$((keep + 1))" | xargs -r rm -rf

echo "Sao lưu hoàn tất: $target"
