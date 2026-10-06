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

LOG_PATH="$ROOT/artifacts/logs/deploy-local-mod.log"
mkdir -p "$(dirname "$LOG_PATH")"
exec 3>&1
exec >"$LOG_PATH" 2>&1
STEP="build and deploy"
trap 'workflow_status=$?; if [[ $workflow_status -eq 0 ]]; then echo ok >&3; else echo "$STEP failed" >&3; tail -n 12 "$LOG_PATH" >&3; echo "Full log: $LOG_PATH" >&3; fi' EXIT
WORKFLOW_CHILD=""
cancel_workflow() {
	if [[ -n "$WORKFLOW_CHILD" ]]; then
		kill -TERM "$WORKFLOW_CHILD" 2>/dev/null || true
		wait "$WORKFLOW_CHILD" || true
	fi
	echo "Workflow cancelled"
	exit "$1"
}
trap 'cancel_workflow 130' INT
trap 'cancel_workflow 143' TERM
run_workflow() {
	"$@" <&0 & WORKFLOW_CHILD=$!
	local command_status=0
	wait "$WORKFLOW_CHILD" || command_status=$?
	WORKFLOW_CHILD=""
	return "$command_status"
}

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
run_workflow "${BUILD_COMMAND[@]}"

if [[ "$RUN_TESTS" == true ]]; then
	STEP=tests
	run_workflow dotnet test "$ROOT/RimBridgeServer.sln" -c "$CONFIGURATION" --no-build
fi

STEP="deployed artifact verification"
DEPLOYMENT_DIR="${TARGET_DIR:-$MODS_DIR/RimBridgeServer}"
COMPANION_MODS_DIR="$MODS_DIR"
if [[ -n "$TARGET_DIR" ]]; then COMPANION_MODS_DIR=""; fi
run_workflow python3 - "$ROOT" "$DEPLOYMENT_DIR" "${ZIP_PATH:-$DEPLOYMENT_DIR.zip}" "$COMPANION_MODS_DIR" <<'PY'
from pathlib import Path
import sys
import zipfile

root, deployed, archive = map(Path, sys.argv[1:4])
with zipfile.ZipFile(archive) as package:
    if package.testzip() is not None:
        raise RuntimeError('Deployed mod ZIP failed its CRC check')
    for source in (root/'1.6/Assemblies').glob('*.dll'):
        relative = '1.6/Assemblies/'+source.name
        expected = source.read_bytes()
        if (deployed/relative).read_bytes() != expected or package.read(relative) != expected:
            raise RuntimeError('Deployed DLL or ZIP differs from the build: '+source.name)
    if any('BridgeTools' in name for name in package.namelist()):
        raise RuntimeError('Test companions must not enter the player mod ZIP')
if not sys.argv[2] or not (deployed/'1.6/Assemblies/RimBridgeServer.dll').is_file():
    raise RuntimeError('Deployed main mod DLL is missing')
if sys.argv[4]:
    bundle = Path(sys.argv[4]).parent/'BridgeTools/Multiplayer'
    source = root/'artifacts/BridgeTools/Multiplayer/Multiplayer.BridgeTools.dll'
    if (bundle/source.name).read_bytes() != source.read_bytes():
        raise RuntimeError('Deployed Multiplayer companion differs from the build')
    if any(p.suffix == '.dll' and p.name != source.name for p in bundle.iterdir()):
        raise RuntimeError('Multiplayer companion bundle contains unexpected DLLs')
PY
