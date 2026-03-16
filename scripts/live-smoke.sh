#!/usr/bin/env bash
set -euo pipefail

dotnet run --project Tests/RimBridgeServer.LiveSmoke/RimBridgeServer.LiveSmoke.csproj -- "$@"
