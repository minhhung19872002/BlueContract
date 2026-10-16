#!/usr/bin/env bash
# Run migrations + idempotent seed inside the running api container, then exit.
# Usage: scripts/seed.sh [--reset-demo]
set -euo pipefail
cd "$(dirname "$0")/.."

args=(BlueContract.Api.dll --seed-only)
[ "${1:-}" = "--reset-demo" ] && args+=(--reset-demo)

docker compose exec -T api dotnet "${args[@]}"
echo "Seed hoàn tất."
