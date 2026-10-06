# RimBridgeServer Agent Guide

RimBridgeServer is a C# RimWorld 1.6 mod that turns a running RimWorld session into a live automation bridge for external tools, test harnesses, and AI agents. It is a Harmony-heavy runtime mod plus a small tool/documentation/test ecosystem, so keep source changes narrow and verify both the mod assembly and the generated tool surface when you touch public tools.

Start here before making non-trivial changes:

- [README.md](README.md) for the user-facing setup model, GABS vs direct mode, and generated grouped tool surface.
- [docs/architecture.md](docs/architecture.md) for the runtime bridge architecture and ownership boundaries.
- [docs/tool-reference.md](docs/tool-reference.md) for the generated annotation-driven tool contract.
- [docs/lua-frontend-design.md](docs/lua-frontend-design.md) for lowered Lua scripting behavior.
- [docs/semantic-state-design.md](docs/semantic-state-design.md) for semantic inspection and notification surfaces.

## Working Rules

- Use the quiet `scripts/build-mod.sh [--test]` and `scripts/deploy-local-mod.sh --mods-dir <active Mods folder> [--test]` commands for routine work. Full logs are in `artifacts/logs`; successful workflows print exactly `ok`, and failures report the step/log with a nonzero exit code.
- Build after C#, project or dependency changes; add `--test` when test projects are affected. Deployment verifies installed and ZIP DLLs against build bytes and keeps test companions outside the player mod ZIP.
- The main build includes the optional `Companions/Multiplayer` project. Normal Mods-folder deployment places its sole DLL in sibling `BridgeTools/Multiplayer`; it is excluded from player mod ZIPs. Use its README for native local host/join/status/leave controls.
- The debug build writes tracked mod assemblies under `1.6/Assemblies/`. If `RIMWORLD_MOD_DIR` is set, the build copies the mod into `$RIMWORLD_MOD_DIR/RimBridgeServer` and creates a zip there. If the enabled mod is an exact active root such as a Steam Workshop item folder, use `RIMWORLD_MOD_TARGET_DIR` instead so the build deploys into that folder directly; verify the active root with `rimworld/get_mod_configuration_status` before live smoke testing.
- For companion-tool guidance and downstream mod work, normal paired local deploys must derive the companion root from the active RimWorld `Mods` folder: mod DLLs deploy under `$(RIMWORLD_MOD_DIR)/SomeMod`, and companion DLLs deploy under `$(RIMWORLD_MOD_DIR)/../BridgeTools/SomeMod`. Do not replace this with a separate BridgeTools override or a mod-local `1.6/BridgeTools` layout unless the task explicitly targets the rare packaged-mod edge case.
- `Directory.Build.props` is the source of truth for `ModVersion`, `ModFileName`, repository metadata, and RimBridge package version pins.
- Public tool docs are generated from `[Tool]`, `[ReadmeTool]`, and `[ToolParameter]` annotations in `Source/RimBridgeTools.cs`. After changing public tools, run `scripts/generate-tool-reference.sh` and then `scripts/install-skills.sh` so the generated live-bridge skill and repo-owned companion-tool skill are both refreshed locally.
- Do not use the live `rimbridge-server` Codex skill just because you are editing this repository. Use it only when the task requires a running RimWorld session or live bridge interaction.
- Keep old public tool names out of the annotated surface unless compatibility is explicitly required. Agents discover tools dynamically through the bridge.
- For UI or visible in-game validation on macOS, use the home-directory Computer Use/BrrainzTools instructions and prefer bridge-owned semantic tools for gameplay.

## Current Dependency Note

RimBridgeServer targets RimWorld 1.6 and builds the main mod assembly for `net472`, with shared contracts and live-smoke tooling also targeting `net10.0` for local and CI validation. There is intentionally no repo-local `global.json`; use the installed SDK selected by the normal .NET resolver.
