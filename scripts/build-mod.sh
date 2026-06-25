#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
RUN_TESTS=false
NO_RESTORE=false
EXTRA_ARGS=()

usage() {
	cat >&2 <<'USAGE'
usage: scripts/build-mod.sh [options] [-- <dotnet build args>]

Build RimBridgeServer without deploying it to any RimWorld Mods folder.

Options:
  -c, --configuration <name>  Build configuration. Default: Release
      --debug                 Shortcut for --configuration Debug
      --release               Shortcut for --configuration Release
      --test                  Run dotnet test after a successful build
      --no-restore            Pass --no-restore to dotnet build
  -h, --help                  Show this help
USAGE
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		-c|--configuration)
			[[ $# -ge 2 ]] || { echo "$1 requires a value" >&2; exit 2; }
			CONFIGURATION="$2"
			shift 2
			;;
		--debug)
			CONFIGURATION="Debug"
			shift
			;;
		--release)
			CONFIGURATION="Release"
			shift
			;;
		--test)
			RUN_TESTS=true
			shift
			;;
		--no-restore)
			NO_RESTORE=true
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		--)
			shift
			EXTRA_ARGS+=("$@")
			break
			;;
		*)
			EXTRA_ARGS+=("$1")
			shift
			;;
	esac
done

BUILD_ARGS=(
	"$ROOT/RimBridgeServer.sln"
	-c "$CONFIGURATION"
	-p:RIMWORLD_MOD_DIR=
	-p:RIMWORLD_MOD_TARGET_DIR=
	-p:RIMWORLD_MOD_ZIP_PATH=
)

if [[ "$NO_RESTORE" == true ]]; then
	BUILD_ARGS+=(--no-restore)
fi

BUILD_COMMAND=(dotnet build "${BUILD_ARGS[@]}")
if [[ ${#EXTRA_ARGS[@]} -gt 0 ]]; then
	BUILD_COMMAND+=("${EXTRA_ARGS[@]}")
fi
"${BUILD_COMMAND[@]}"

if [[ "$RUN_TESTS" == true ]]; then
	dotnet test "$ROOT/RimBridgeServer.sln" -c "$CONFIGURATION" --no-build
fi
