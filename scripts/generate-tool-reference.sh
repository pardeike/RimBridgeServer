#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)"

cd "$REPO_DIR"
dotnet run --project Tools/RimBridgeServer.ToolDocGen/RimBridgeServer.ToolDocGen.csproj -- "$REPO_DIR/Source/RimBridgeTools.cs" "$REPO_DIR/docs/tool-reference.md"
