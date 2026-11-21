#!/usr/bin/env bash
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
RUN_TESTS=false
NO_RESTORE=false
EXTRA_ARGS=()

usage() {
	cat >&2 <<'USAGE'
usage: scripts/build-mod.sh [options] [-- <dotnet build args>]

Build and verify the public ZIP without deploying to a RimWorld Mods folder.

Options:
  -c, --configuration <name>  Build configuration. Default: Release
      --debug                 Shortcut for --configuration Debug
      --release               Shortcut for --configuration Release
      --test                  Run unit and isolated installer tests after a successful build
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

LOG_PATH="$ROOT/artifacts/logs/build-mod.log"
mkdir -p "$(dirname "$LOG_PATH")"
exec 3>&1
exec >"$LOG_PATH" 2>&1
STEP=build
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
    python3 -c '
import os, signal, subprocess, sys
process = subprocess.Popen(sys.argv[1:], start_new_session=True)
def cancel(signum, frame):
    try:
        os.killpg(process.pid, signum)
    except ProcessLookupError:
        pass
signal.signal(signal.SIGINT, cancel)
signal.signal(signal.SIGTERM, cancel)
code = process.wait()
sys.exit(code if code >= 0 else 128 - code)
' "$@" <&0 & WORKFLOW_CHILD=$!
    local command_status=0
    wait "$WORKFLOW_CHILD" || command_status=$?
    WORKFLOW_CHILD=""
    return "$command_status"
}

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

BUILD_COMMAND=(dotnet build --disable-build-servers "${BUILD_ARGS[@]}")
if [[ ${#EXTRA_ARGS[@]} -gt 0 ]]; then
	BUILD_COMMAND+=("${EXTRA_ARGS[@]}")
fi
run_workflow "${BUILD_COMMAND[@]}"

if [[ "$RUN_TESTS" == true ]]; then
	STEP=tests
	run_workflow dotnet test "$ROOT/RimBridgeServer.sln" -c "$CONFIGURATION" --no-build --no-restore
	STEP="isolated installer tests"
	run_workflow python3 -m unittest discover -s "$ROOT/Tests/Deployment" -p "test_*.py"
fi
