#!/usr/bin/env bash
set -euo pipefail

SCENARIOS=(
  "save-load-roundtrip"
  "context-menu-cancel-roundtrip"
  "screen-target-click-roundtrip"
  "screen-target-clip"
  "screenshot-capture"
  "architect-floor-dropdown"
  "architect-wall-placement"
  "architect-zone-area-drag"
)

for scenario in "${SCENARIOS[@]}"; do
  echo "==> ${scenario}"
  dotnet run --project Tests/RimBridgeServer.LiveSmoke/RimBridgeServer.LiveSmoke.csproj -- \
    --scenario "${scenario}" \
    --human-verify \
    --stop-after \
    "$@"
done
