#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/skill-generator.sh [options]

Generate a Codex skill from a repo README and tool reference, then install or
update it under the local Codex skills directory.

Options:
  --readme PATH            Source README to summarize (default: README.md)
  --tool-reference PATH    Tool reference markdown (default: docs/tool-reference.md)
  --skill-name NAME        Installed skill directory/name
  --install-root DIR       Codex skills root (default: ${CODEX_HOME:-$HOME/.codex}/skills)
  --output-dir DIR         Persist the generated skill under DIR/NAME before install
  --no-install             Generate files only; do not sync into the Codex skills root
  -h, --help               Show this help
EOF
}

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd -- "$SCRIPT_DIR/.." && pwd)"

README_PATH="$REPO_DIR/README.md"
TOOL_REFERENCE_PATH="$REPO_DIR/docs/tool-reference.md"
INSTALL_ROOT="${CODEX_HOME:-$HOME/.codex}/skills"
OUTPUT_ROOT=""
SKILL_NAME=""
NO_INSTALL=0

while (($# > 0)); do
  case "$1" in
    --readme)
      README_PATH="$2"
      shift 2
      ;;
    --tool-reference)
      TOOL_REFERENCE_PATH="$2"
      shift 2
      ;;
    --skill-name)
      SKILL_NAME="$2"
      shift 2
      ;;
    --install-root)
      INSTALL_ROOT="$2"
      shift 2
      ;;
    --output-dir)
      OUTPUT_ROOT="$2"
      shift 2
      ;;
    --no-install)
      NO_INSTALL=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ ! -f "$README_PATH" ]]; then
  echo "README not found: $README_PATH" >&2
  exit 1
fi

if [[ ! -f "$TOOL_REFERENCE_PATH" ]]; then
  echo "Tool reference not found: $TOOL_REFERENCE_PATH" >&2
  exit 1
fi

if [[ -z "$SKILL_NAME" ]]; then
  SKILL_NAME="$(
    python3 - "$README_PATH" <<'PY'
from pathlib import Path
import re
import sys

text = Path(sys.argv[1]).read_text(encoding="utf-8")
match = re.search(r"^#\s+(.+)$", text, re.MULTILINE)
title = match.group(1).strip() if match else Path(sys.argv[1]).resolve().parent.name
title = re.sub(r"([a-z0-9])([A-Z])", r"\1-\2", title)
title = re.sub(r"[^A-Za-z0-9]+", "-", title).strip("-").lower()
title = re.sub(r"-{2,}", "-", title)
print(title or "generated-skill")
PY
  )"
fi

if ((NO_INSTALL)) && [[ -z "$OUTPUT_ROOT" ]]; then
  OUTPUT_ROOT="$REPO_DIR/artifacts/generated-skills"
fi

TEMP_ROOT=""
if [[ -n "$OUTPUT_ROOT" ]]; then
  mkdir -p "$OUTPUT_ROOT"
  SKILL_DIR="$OUTPUT_ROOT/$SKILL_NAME"
  rm -rf "$SKILL_DIR"
else
  TEMP_ROOT="$(mktemp -d)"
  SKILL_DIR="$TEMP_ROOT/$SKILL_NAME"
fi

cleanup() {
  if [[ -n "$TEMP_ROOT" && -d "$TEMP_ROOT" ]]; then
    rm -rf "$TEMP_ROOT"
  fi
}
trap cleanup EXIT

mkdir -p "$SKILL_DIR/references" "$SKILL_DIR/agents"
cp "$README_PATH" "$SKILL_DIR/references/readme.md"
cp "$TOOL_REFERENCE_PATH" "$SKILL_DIR/references/tool-reference.md"

python3 - "$README_PATH" "$TOOL_REFERENCE_PATH" "$SKILL_DIR" "$SKILL_NAME" <<'PY'
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

readme_path = Path(sys.argv[1])
tool_reference_path = Path(sys.argv[2])
skill_dir = Path(sys.argv[3])
skill_name = sys.argv[4]

readme = readme_path.read_text(encoding="utf-8")
tool_reference = tool_reference_path.read_text(encoding="utf-8")


def first_heading(text: str) -> str:
    match = re.search(r"^#\s+(.+)$", text, re.MULTILINE)
    return match.group(1).strip() if match else "Generated Skill"


def first_paragraph(text: str) -> str:
    lines = text.splitlines()
    seen_title = False
    paragraph: list[str] = []
    for line in lines:
        if not seen_title:
            if line.startswith("# "):
                seen_title = True
            continue
        stripped = line.strip()
        if not stripped:
            if paragraph:
                break
            continue
        if stripped.startswith("#"):
            break
        paragraph.append(stripped)
    return " ".join(paragraph).strip()


def parse_tool_counts(text: str) -> tuple[str | None, list[tuple[str, str]]]:
    total_match = re.search(r"-\s+`(\d+)`\s+tools total", text)
    total = total_match.group(1) if total_match else None
    namespaces = re.findall(r"-\s+`(\d+)`\s+`([^`]+)`\s+tools", text)
    return total, [(ns, count) for count, ns in namespaces]


def parse_surface_sections(text: str) -> list[dict[str, object]]:
    block_match = re.search(
        r"<!-- BEGIN GENERATED:tool-surface -->(.*?)<!-- END GENERATED:tool-surface -->",
        text,
        re.DOTALL,
    )
    if not block_match:
        return []

    sections: list[dict[str, object]] = []
    current: dict[str, object] | None = None
    for raw_line in block_match.group(1).splitlines():
        line = raw_line.strip()
        if not line:
            continue
        if line.startswith("### "):
            current = {"title": line[4:].strip(), "tools": []}
            sections.append(current)
            continue
        if line.startswith("- `") and current is not None:
            match = re.match(r"- `([^`]+)` - (.+)$", line)
            if match:
                tools = current["tools"]
                assert isinstance(tools, list)
                tools.append((match.group(1), match.group(2)))
    return sections


def tool_score(tool_name: str, summary: str, index: int) -> tuple[int, int]:
    key = tool_name.lower().replace("/", "_").replace(".", "_").replace("-", "_")
    summary_key = summary.lower()
    score = 0
    weights = {
        "status": 120,
        "state": 100,
        "capabil": 110,
        "wait_for_game_loaded": 105,
        "load_game_ready": 115,
        "wait": 90,
        "reference": 95,
        "compile": 90,
        "layout": 95,
        "ui_state": 90,
        "screen_targets": 85,
        "screenshot": 100,
        "search": 80,
        "list_": 70,
        "get_": 55,
        "select_": 45,
        "open_": 40,
        "save_": 70,
        "load_": 70,
        "set_": 35,
        "draft": 45,
        "designator": 40,
        "settings": 35,
        "activate_": 35,
        "execute_": 20,
        "run_": 15,
    }
    for needle, points in weights.items():
        if needle in key:
            score += points
    if "semantic" in summary_key:
        score += 30
    if "deterministic" in summary_key:
        score += 20
    if "without requiring os focus" in summary_key:
        score += 25
    if "machine-readable" in summary_key:
        score += 20
    if "current" in summary_key:
        score += 10
    if key.endswith("_ping") or key == "ping":
        score -= 30
    if key.startswith("deselect_") or key.startswith("clear_") or key.startswith("close_"):
        score -= 20
    score += max(0, 12 - (index * 2))
    return score, -index


def choose_examples(tools: list[tuple[str, str]], limit: int = 3) -> list[str]:
    ranked = sorted(
        ((tool_score(name, summary, idx), name) for idx, (name, summary) in enumerate(tools)),
        reverse=True,
    )
    examples: list[str] = []
    for _, name in ranked:
        if name not in examples:
            examples.append(name)
        if len(examples) == limit:
            break
    return examples


def join_backticked(values: list[str]) -> str:
    if not values:
        return ""
    wrapped = [f"`{value}`" for value in values]
    if len(wrapped) == 1:
        return wrapped[0]
    if len(wrapped) == 2:
        return f"{wrapped[0]} and {wrapped[1]}"
    return ", ".join(wrapped[:-1]) + f", and {wrapped[-1]}"


title = first_heading(readme)
display_name = title
intro = first_paragraph(readme)
total_tools, namespace_counts = parse_tool_counts(tool_reference)
surface_sections = parse_surface_sections(readme)
has_gabs = "GABS" in readme
has_direct = bool(re.search(r"^###\s+Direct Mode\b", readme, re.MULTILINE))
mentions_rimworld = "RimWorld" in readme

if mentions_rimworld:
    description = (
        f"Use when a task needs to discover or call live {title} tools against a running "
        "RimWorld session through GABS or a direct bridge connection. Do not use it just "
        f"because the current repository is {title}; for source-only edits or reviews with "
        "no live bridge interaction, work directly in the codebase."
    )
    short_description = "Use live RimWorld bridge tools"
else:
    description = (
        f"Use when working with {title}, especially through GABS or a direct bridge "
        "connection, and the task requires discovering or using a live dynamic tool surface."
    )
    short_description = "Discover and use live bridge tools"

default_prompt = (
    f"Use ${skill_name} when the task requires discovering or calling live {title} tools "
    "against a running game through GABS or a direct bridge connection."
)

count_line = ""
if total_tools and namespace_counts:
    ns_summary = ", ".join(f"{count} {namespace}" for namespace, count in namespace_counts)
    count_line = f"Current docs snapshot: {total_tools} total tools ({ns_summary})."
elif total_tools:
    count_line = f"Current docs snapshot: {total_tools} total tools."

task_lines: list[str] = []
for section in surface_sections:
    section_title = str(section["title"])
    tools = section["tools"]
    assert isinstance(tools, list)
    if not tools:
        continue
    examples = choose_examples(tools)
    if examples:
        task_lines.append(
            f"- {section_title}: direct-tool starting points include {join_backticked(examples)}."
        )
    else:
        task_lines.append(f"- {section_title}: use the current live discovery surface.")

overview_line = intro or f"{title} exposes a live tool surface that can change at runtime."
if mentions_rimworld:
    overview_line = (
        "RimBridgeServer turns a running RimWorld session into a live automation bridge. "
        "Use this skill when the goal is to inspect, drive, verify, or script RimWorld "
        "through that bridge. Do not use it for source-only editing of RimBridgeServer itself."
    )

quick_start_lines = [
    "1. Use this skill only when the task actually needs live bridge or in-game interaction; for source-only repo work, do not load it.",
    "2. Prefer the GABS connector in Codex when it is available, because it gives you a stable discovery and call surface even when the mirrored game tools change at runtime.",
    "3. Start by checking session state with `mcp__gabs__games_status` and then use `mcp__gabs__games_connect` when RimWorld is running but not yet attached.",
    "4. For a cold-start fast path, prefer `mcp__gabs__games_start` -> `rimworld/load_game_ready` when you need a save -> the semantic tool you actually need. Do not insert arbitrary sleeps, redundant reconnects, or a second readiness wait after the composite load tool succeeds.",
    "5. If `mcp__gabs__games_start` reports that the game started successfully and connected via GABP, do not call `mcp__gabs__games_connect` again. Starting the process is not itself a reason to call `rimbridge/wait_for_game_loaded`: if you need to load a save, call `rimworld/load_game_ready`; if a playable game is already loading or a lifecycle result says `state.automationReady: false`, then call `rimbridge/wait_for_game_loaded`; otherwise proceed to the semantic tool you actually need.",
    "6. If RimWorld is already running but owned by another live GABS session and you intentionally want this session to take over, prefer `mcp__gabs__games_connect` with `forceTakeover: true` instead of stop/start just to gain ownership.",
    "7. Discover the live mirrored tool names with `mcp__gabs__games_tool_names` and inspect only the few candidates you might use with `mcp__gabs__games_tool_detail`.",
    "8. Call the discovered mirrored tool through `mcp__gabs__games_call_tool`. Do not guess how direct tool ids were mirrored, normalized, or prefixed.",
]

if has_direct:
    quick_start_lines.append(
        "9. If a direct RimBridgeServer MCP connector is already installed in the current Codex session, start with its own status and discovery tools such as `rimbridge/get_bridge_status`, `rimbridge/list_capabilities`, and `rimbridge/wait_for_game_loaded`."
    )
else:
    quick_start_lines.append(
        "9. If you are not using GABS, look for the server's direct discovery and status tools before attempting any domain action."
    )

skill_md = [
    "---",
    f'name: {json.dumps(skill_name)}',
    f'description: {json.dumps(description)}',
    "---",
    "",
    f"# {title}",
    "",
    overview_line,
    "",
]

if count_line:
    skill_md.extend([count_line, ""])

skill_md.extend(
    [
        "## Quick start",
        "",
        *quick_start_lines,
        "",
        "## Discovery rules",
        "",
        "- Do not invoke this skill just because the current repository is RimBridgeServer. Use it only when the task needs live game or bridge interaction.",
        "- Treat the live tool surface as dynamic. Re-discover after reconnects, game restarts, or mod changes instead of relying on stale names.",
        "- Through GABS, use `mcp__gabs__games_tool_names -> mcp__gabs__games_tool_detail -> mcp__gabs__games_call_tool` as the default pattern.",
        "- `mcp__gabs__games_start` already performs a short best-effort GABP attach. If it reports success with GABP connected, do not immediately follow it with `mcp__gabs__games_connect`.",
        "- `mcp__gabs__games_stop` already waits for shutdown or kill fallback; do not insert arbitrary sleeps after a successful stop unless you are debugging a concrete platform-specific issue.",
        "- If the only problem is session ownership, prefer `mcp__gabs__games_connect` with `forceTakeover: true` over stopping and relaunching the game.",
        "- Prefer composite lifecycle tools when the surface exposes them. In particular, use `rimworld/load_game_ready` instead of chaining `rimworld/load_game` and `rimbridge/wait_for_game_loaded` manually.",
        "- Do not call `rimbridge/wait_for_game_loaded` immediately after `mcp__gabs__games_start` just because the game process connected. Use it only when a playable game is already expected to be loading, such as after `rimworld/start_debug_game`, after `rimworld/load_game`, or when a lifecycle result explicitly reports `state.automationReady: false`.",
        "- Use lifecycle results as state signals. If you did call `rimworld/load_game` directly and it returns `state.automationReady: false`, call `rimbridge/wait_for_game_loaded` immediately; if it already returns ready, skip the extra wait.",
        "- After `rimbridge/wait_for_game_loaded` or `rimworld/load_game_ready` returns success, proceed directly to the requested colony action or verification step instead of rechecking readiness again.",
        "- Through a direct bridge connection, prefer bridge-native discovery and status tools before calling task-specific tools.",
        "- Prefer semantic tools over brittle UI clicking when a structured tool exists for the task.",
        "- For new lowered-Lua automation, read the reference or compile first before running the script.",
        "- After actions that can change UI or game state significantly, re-read layout, state, selection, or logs instead of assuming success.",
        "",
        "## Workflow",
        "",
        "1. Decide whether the task actually requires live bridge interaction. If the work is source-only editing, review, or refactoring inside RimBridgeServer, do not use this skill.",
        "2. Establish whether you are using GABS or a direct bridge connection.",
        "3. Verify session health, then wait only when the next action actually depends on a playable loaded game or when a load is already in progress.",
        "4. Discover candidate tools for the user's goal and inspect exact parameter contracts for only the few tools you plan to call.",
        "5. Take the highest-level semantic path available, then verify the result with state reads, layout snapshots, screenshots, logs, or operation status.",
        "",
        "## Task map",
        "",
    ]
)

if task_lines:
    skill_md.extend(task_lines)
else:
    skill_md.append("- Use the bundled README and tool reference to identify the right discovery, state, and action tools for the current session.")

skill_md.extend(
    [
        "",
        "Through GABS, ask `mcp__gabs__games_tool_names` for the mirrored name instead of transforming the direct tool ids above yourself.",
        "",
        "## References",
        "",
        "- `references/readme.md` for setup, GABS vs direct mode, the beginner mental model, and the current grouped tool surface.",
        "- `references/tool-reference.md` for parameter-level contracts, defaults, and exact per-tool summaries.",
    ]
)

(skill_dir / "SKILL.md").write_text("\n".join(skill_md) + "\n", encoding="utf-8")

openai_yaml = "\n".join(
    [
        "interface:",
        f"  display_name: {json.dumps(display_name)}",
        f"  short_description: {json.dumps(short_description)}",
        f"  default_prompt: {json.dumps(default_prompt)}",
        "",
        "policy:",
        "  allow_implicit_invocation: true",
        "",
    ]
)
(skill_dir / "agents" / "openai.yaml").write_text(openai_yaml, encoding="utf-8")
PY

if ((NO_INSTALL)); then
  echo "Generated skill at: $SKILL_DIR"
  exit 0
fi

mkdir -p "$INSTALL_ROOT"
TARGET_DIR="$INSTALL_ROOT/$SKILL_NAME"
mkdir -p "$TARGET_DIR"

if command -v rsync >/dev/null 2>&1; then
  rsync -a --delete "$SKILL_DIR/" "$TARGET_DIR/"
else
  find "$TARGET_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
  cp -R "$SKILL_DIR/." "$TARGET_DIR/"
fi

echo "Installed skill '$SKILL_NAME' to: $TARGET_DIR"
