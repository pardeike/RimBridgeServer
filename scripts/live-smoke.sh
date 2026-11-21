#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
if [[ "${1:-}" != --waylith-poc ]]; then
    exec dotnet run --project Tests/RimBridgeServer.LiveSmoke/RimBridgeServer.LiveSmoke.csproj -- "$@"
fi
mkdir -p artifacts/logs
LOG_PATH="$(mktemp "$ROOT/artifacts/logs/waylith-poc.log.XXXXXX")"
CHILD=""
cancel() {
    if [[ -n "$CHILD" ]]; then
        kill -TERM "$CHILD" 2>/dev/null || true
        wait "$CHILD" || true
    fi
    echo "Waylith proof cancelled; log: $LOG_PATH" >&2
    exit 130
}
trap cancel INT TERM
run() {
    # Run in a new session so cancellation reaches dotnet's descendants, as in build-mod.sh.
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
' "$@" >>"$LOG_PATH" 2>&1 & CHILD=$!
    local status=0
    wait "$CHILD" || status=$?
    CHILD=""
    return "$status"
}
if ! run dotnet build Tests/RimBridgeServer.LiveSmoke/RimBridgeServer.LiveSmoke.csproj -c Release --nologo; then
    echo "Waylith proof build failed; log: $LOG_PATH" >&2
    tail -n 8 "$LOG_PATH" >&2
    exit 1
fi
if ! run dotnet Tests/RimBridgeServer.LiveSmoke/bin/Release/net10.0/RimBridgeServer.LiveSmoke.dll "$@"; then
    echo "Waylith proof failed; log: $LOG_PATH" >&2
    tail -n 5 "$LOG_PATH" >&2
    exit 1
fi
echo ok
