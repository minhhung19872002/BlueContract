#!/usr/bin/env bash
# Regenerates the endpoint table of docs/API.md from the running API's endpoint metadata (routes, permissions, types,
# XML comments). ApiDocTests fails in CI when the committed table is stale.
# Usage: scripts/gen-api-doc.sh   (needs Docker for the test containers, like the integration tests)
set -euo pipefail
cd "$(dirname "$0")/.."
BLUEPROCURE_UPDATE_API_DOC=1 dotnet test backend/tests/BlueContract.Api.IntegrationTests --filter "FullyQualifiedName~ApiDocTests" --nologo
echo "docs/API.md updated"
