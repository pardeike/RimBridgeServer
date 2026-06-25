#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALL_ROOT="${CODEX_HOME:-$HOME/.codex}/skills"
INSTALL_GENERATED=1
INSTALL_REPO_SKILLS=1

usage() {
	cat >&2 <<'USAGE'
usage: scripts/install-skills.sh [options]

Install RimBridgeServer Codex skills into the local Codex skills directory.

Options:
  --install-root <dir>       Install root. Default: ${CODEX_HOME:-$HOME/.codex}/skills
  --repo-only                Install only repo-owned skills from ./skills
  --generated-only           Install only the generated rimbridge-server live bridge skill
  -h, --help                 Show this help
USAGE
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--install-root)
			[[ $# -ge 2 ]] || { echo "$1 requires a value" >&2; exit 2; }
			INSTALL_ROOT="$2"
			shift 2
			;;
		--repo-only)
			INSTALL_GENERATED=0
			shift
			;;
		--generated-only)
			INSTALL_REPO_SKILLS=0
			shift
			;;
		-h|--help)
			usage
			exit 0
			;;
		*)
			echo "unknown argument: $1" >&2
			usage
			exit 2
			;;
	esac
done

mkdir -p "$INSTALL_ROOT"

if [[ "$INSTALL_GENERATED" == 1 ]]; then
	"$ROOT/scripts/skill-generator.sh" --skill-name rimbridge-server --install-root "$INSTALL_ROOT"
fi

if [[ "$INSTALL_REPO_SKILLS" == 1 && -d "$ROOT/skills" ]]; then
	for skill_dir in "$ROOT"/skills/*; do
		[[ -d "$skill_dir" ]] || continue
		[[ -f "$skill_dir/SKILL.md" ]] || continue
		name="$(basename "$skill_dir")"
		target="$INSTALL_ROOT/$name"
		mkdir -p "$target"
		if command -v rsync >/dev/null 2>&1; then
			rsync -a --delete "$skill_dir/" "$target/"
		else
			find "$target" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
			cp -R "$skill_dir/." "$target/"
		fi
		echo "Installed skill '$name' to: $target"
	done
fi
