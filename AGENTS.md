# RimBridgeServer Agent Guide

RimBridgeServer is a C# RimWorld 1.6 mod that turns a running RimWorld session into a live automation bridge for external tools, test harnesses, and AI agents. It is a Harmony-heavy runtime mod plus a small tool/documentation/test ecosystem, so keep source changes narrow and verify both the mod assembly and the generated tool surface when you touch public tools.

Start here before making non-trivial changes:

- [README.md](README.md) for the user-facing setup model, GABS vs direct mode, and generated grouped tool surface.
- [docs/architecture.md](docs/architecture.md) for the runtime bridge architecture and ownership boundaries.
- [docs/tool-reference.md](docs/tool-reference.md) for the generated annotation-driven tool contract.
- [docs/lua-frontend-design.md](docs/lua-frontend-design.md) for lowered Lua scripting behavior.
- [docs/semantic-state-design.md](docs/semantic-state-design.md) for semantic inspection and notification surfaces.

## Working Rules

- Build from the repository root with `dotnet build RimBridgeServer.sln` after C#, project, or dependency changes.
- Run `dotnet test RimBridgeServer.sln --no-build` after a successful build when test projects are affected.
- The debug build writes tracked mod assemblies under `1.6/Assemblies/`. If `RIMWORLD_MOD_DIR` is set, the build also copies the mod into that RimWorld Mods directory and creates a zip there.
- `Directory.Build.props` is the source of truth for `ModVersion`, `ModFileName`, repository metadata, and RimBridge package version pins.
- Public tool docs are generated from `[Tool]`, `[ReadmeTool]`, and `[ToolParameter]` annotations in `Source/RimBridgeTools.cs`. After changing public tools, run `scripts/generate-tool-reference.sh` and then `scripts/skill-generator.sh --skill-name rimbridge-server`.
- Do not use the live `rimbridge-server` Codex skill just because you are editing this repository. Use it only when the task requires a running RimWorld session or live bridge interaction.
- Keep old public tool names out of the annotated surface unless compatibility is explicitly required. Agents discover tools dynamically through the bridge.
- For UI or visible in-game validation on macOS, prefer the local `regionshot` workflow when screenshots or app/window inspection are needed.

## Current Dependency Note

RimBridgeServer targets RimWorld 1.6 and builds the main mod assembly for `net472`, with shared contracts and live-smoke tooling also targeting `net10.0` for local and CI validation. There is intentionally no repo-local `global.json`; use the installed SDK selected by the normal .NET resolver.
