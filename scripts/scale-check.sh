#!/usr/bin/env bash
# Runs the API on N instances behind nginx and checks that realtime notifications and signing sessions cross
# instances (E2E #6, ADR-061). Usage: scripts/scale-check.sh [instances=2]
set -euo pipefail
cd "$(dirname "$0")/.."
n="${1:-2}"
docker compose up -d --scale api="$n" api
docker compose restart web   # nginx resolves "api" to every instance when it starts
echo "Waiting for $n API instances…"
for _ in $(seq 1 60); do
  if curl -fs http://127.0.0.1:${WEB_PORT:-47080}/health/ready >/dev/null; then break; fi
  sleep 2
done
sleep 10
(cd e2e && BASE_URL="http://127.0.0.1:${WEB_PORT:-47080}" npx playwright test tests/e2e6-scale.spec.ts --reporter=line)
for i in $(seq 1 "$n"); do
  echo "api-$i requests: $(docker compose logs --index "$i" api --since 5m 2>&1 | grep -c 'HTTP' || true)"
done
