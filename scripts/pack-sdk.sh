#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
OUT_DIR="${NUGET_OUT_DIR:-$ROOT/../.nuget-local}"
VERSION="${PACKAGE_VERSION:-}"
NO_RESTORE=false
EXTRA_ARGS=()

usage() {
	cat >&2 <<'USAGE'
usage: scripts/pack-sdk.sh [options] [-- <dotnet pack args>]

Create the RimBridgeServer.Sdk NuGet package for companion-tool projects.

Options:
  -o, --out <dir>            Package output directory. Default: ../.nuget-local
  -c, --configuration <name> Build configuration. Default: Release
      --version <version>    Override PackageVersion/Version for this pack
      --debug                Shortcut for --configuration Debug
      --release              Shortcut for --configuration Release
      --no-restore           Pass --no-restore to dotnet pack
  -h, --help                 Show this help
USAGE
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		-o|--out)
			[[ $# -ge 2 ]] || { echo "$1 requires a value" >&2; exit 2; }
			OUT_DIR="$2"
			shift 2
			;;
		-c|--configuration)
			[[ $# -ge 2 ]] || { echo "$1 requires a value" >&2; exit 2; }
			CONFIGURATION="$2"
			shift 2
			;;
		--version)
			[[ $# -ge 2 ]] || { echo "$1 requires a value" >&2; exit 2; }
			VERSION="$2"
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

mkdir -p "$OUT_DIR"

PACK_ARGS=(
	"$ROOT/Source/RimBridgeServer.Sdk/RimBridgeServer.Sdk.csproj"
	-c "$CONFIGURATION"
	-o "$OUT_DIR"
)

if [[ -n "$VERSION" ]]; then
	PACK_ARGS+=(
		-p:Version="$VERSION"
		-p:PackageVersion="$VERSION"
		-p:AssemblyVersion="$VERSION"
		-p:FileVersion="$VERSION"
		-p:InformationalVersion="$VERSION"
	)
fi

if [[ "$NO_RESTORE" == true ]]; then
	PACK_ARGS+=(--no-restore)
fi

PACK_COMMAND=(dotnet pack "${PACK_ARGS[@]}")
if [[ ${#EXTRA_ARGS[@]} -gt 0 ]]; then
	PACK_COMMAND+=("${EXTRA_ARGS[@]}")
fi
"${PACK_COMMAND[@]}"

echo "nugetSource=$OUT_DIR"
