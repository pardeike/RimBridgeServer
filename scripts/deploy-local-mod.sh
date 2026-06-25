#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
MODS_DIR="${RIMWORLD_MOD_DIR:-}"
TARGET_DIR="${RIMWORLD_MOD_TARGET_DIR:-}"
ZIP_PATH="${RIMWORLD_MOD_ZIP_PATH:-}"
RUN_TESTS=false
NO_RESTORE=false
EXTRA_ARGS=()

usage() {
	cat >&2 <<'USAGE'
usage: scripts/deploy-local-mod.sh [options] [-- <dotnet build args>]

Build RimBridgeServer and deploy it through the repo's MSBuild CopyToRimworld/ZipMod targets.

Options:
      --mods-dir <dir>        Parent Mods folder. Deploys to <dir>/RimBridgeServer and <dir>/RimBridgeServer.zip
      --target-dir <dir>      Exact active mod root. Deploys directly into this directory
      --zip-path <path>       Override zip output path
  -c, --configuration <name>  Build configuration. Default: Release
      --debug                 Shortcut for --configuration Debug
      --release               Shortcut for --configuration Release
      --test                  Run dotnet test after a successful deploy build
      --no-restore            Pass --no-restore to dotnet build
  -h, --help                  Show this help

If --mods-dir/--target-dir are omitted, RIMWORLD_MOD_DIR or RIMWORLD_MOD_TARGET_DIR are used.
USAGE
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--mods-dir)
			[[ $# -ge 2 ]] || { echo "$1 requires a value" >&2; exit 2; }
			MODS_DIR="$2"
			shift 2
			;;
		--target-dir)
			[[ $# -ge 2 ]] || { echo "$1 requires a value" >&2; exit 2; }
			TARGET_DIR="$2"
			shift 2
			;;
		--zip-path)
			[[ $# -ge 2 ]] || { echo "$1 requires a value" >&2; exit 2; }
			ZIP_PATH="$2"
			shift 2
			;;
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

if [[ -z "$TARGET_DIR" && -z "$MODS_DIR" ]]; then
	echo "missing deploy target: pass --mods-dir, --target-dir, RIMWORLD_MOD_DIR, or RIMWORLD_MOD_TARGET_DIR" >&2
	exit 2
fi

BUILD_ARGS=(
	"$ROOT/RimBridgeServer.sln"
	-c "$CONFIGURATION"
)

if [[ -n "$TARGET_DIR" ]]; then
	BUILD_ARGS+=(-p:RIMWORLD_MOD_TARGET_DIR="$TARGET_DIR" -p:RIMWORLD_MOD_DIR=)
else
	BUILD_ARGS+=(-p:RIMWORLD_MOD_DIR="$MODS_DIR" -p:RIMWORLD_MOD_TARGET_DIR=)
fi

if [[ -n "$ZIP_PATH" ]]; then
	BUILD_ARGS+=(-p:RIMWORLD_MOD_ZIP_PATH="$ZIP_PATH")
fi

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

if [[ -n "$TARGET_DIR" ]]; then
	echo "deployed=$TARGET_DIR"
	echo "zip=${ZIP_PATH:-$TARGET_DIR.zip}"
else
	echo "deployed=$MODS_DIR/RimBridgeServer"
	echo "zip=${ZIP_PATH:-$MODS_DIR/RimBridgeServer.zip}"
fi
