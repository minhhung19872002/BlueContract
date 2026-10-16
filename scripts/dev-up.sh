#!/usr/bin/env bash
# Start the full BlueProcure stack with Docker Compose.
# Usage: scripts/dev-up.sh [--seq] [--no-build]
set -euo pipefail
cd "$(dirname "$0")/.."

seq=false; build=true
for arg in "$@"; do
  case "$arg" in
    --seq) seq=true ;;
    --no-build) build=false ;;
    *) echo "Unknown option: $arg" >&2; exit 1 ;;
  esac
done

if [ ! -f .env ]; then
  cp .env.example .env
  echo "Đã tạo .env từ .env.example"
fi

[ -d .git ] && git config core.hooksPath .githooks

args=(compose)
$seq && args+=(--profile seq)
args+=(up -d)
$build && args+=(--build)
docker "${args[@]}"

# shellcheck disable=SC1091
set -a; . ./.env; set +a
web_port="${WEB_PORT:-47080}"

printf "Đang chờ API sẵn sàng..."
for _ in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:${web_port}/health/ready" >/dev/null 2>&1; then
    echo " OK"; break
  fi
  printf "."; sleep 2
done

echo
echo "Web:            http://localhost:${web_port}"
echo "API docs:       http://localhost:${web_port}/docs"
echo "Mailpit:        http://localhost:${MAILPIT_UI_PORT:-47025}"
echo "Kho tệp (filer): http://localhost:${S3_FILER_UI_PORT:-47901}"
echo "ONLYOFFICE:     http://localhost:${ONLYOFFICE_PORT:-47082}"
$seq && echo "Seq:            http://localhost:${SEQ_PORT:-47341}"
exit 0
