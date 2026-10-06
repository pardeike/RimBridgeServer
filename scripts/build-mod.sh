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
	"$@" <&0 & WORKFLOW_CHILD=$!
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

BUILD_COMMAND=(dotnet build "${BUILD_ARGS[@]}")
if [[ ${#EXTRA_ARGS[@]} -gt 0 ]]; then
	BUILD_COMMAND+=("${EXTRA_ARGS[@]}")
fi
run_workflow "${BUILD_COMMAND[@]}"

if [[ "$RUN_TESTS" == true ]]; then
	STEP=tests
	run_workflow dotnet test "$ROOT/RimBridgeServer.sln" -c "$CONFIGURATION" --no-build
fi
