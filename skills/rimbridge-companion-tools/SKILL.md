---
name: rimbridge-companion-tools
description: Use when adding, migrating, reviewing, or testing RimBridgeServer 2.x companion DLL tools for a RimWorld mod. Covers RimBridgeServer.Sdk package references, BridgeTools folder layout, companion csproj wiring, async IRimBridgeContext tool orchestration, local NuGet/package use, build/deploy automation, and GABS validation of mod-owned [Tool] methods.
---

# RimBridge Companion Tools

Use this skill for source work in a RimWorld mod that wants to expose tools through RimBridgeServer 2.x. Do not use it for ordinary live bridge calls; use the `rimbridge-server` skill for running-game interaction.

## Workflow

1. Check the target mod's current build/deploy layout before editing. Preserve its existing publicized RimWorld reference pattern and local deploy scripts.
2. Move bridge-only `[Tool]` methods into a companion assembly. For local paired mod deployments, build to a repo artifact folder and copy the companion to the global sibling `BridgeTools` folder derived from the selected `Mods` folder, normally `$(RIMWORLD_MOD_DIR)\..\BridgeTools\SomeMod`.
3. Reference `RimBridgeServer.Sdk` for compile-time annotations and runtime interfaces, but do not deploy `RimBridgeServer.Sdk.dll` with the companion. RimBridgeServer resolves it to the host copy.
4. Reference the owning mod DLL from the companion assembly so the companion can call mod helpers after RimWorld loads the normal mod assembly.
5. Wire the main mod build so it builds the companion after the main mod DLL exists and before local deploy/copy/zip steps run. The user should build the main mod project only; it should automatically build the matching companion.
6. Prefer async SDK orchestration for real in-game test harnesses: `IRimBridgeContext`, `ctx.Tools.List/Get/CallAsync/CallAsync<T>/QueueAsync`, result helpers such as `Succeeded()` and `ReadResult<T>(...)`, `RimBridgeEvidenceManifest`/`RimBridgeEvidence` for stable evidence results, and `ctx.Game.StepTicksAsync/RunForTicksAsync/RunUntilAsync`.
7. Validate in three layers: source build, deployed files, then live GABS discovery/call of the companion tool.

## Rules

- Do not keep new public `[Tool]` methods in the main mod assembly when migrating to RimBridgeServer 2.x; enabled mods are discovered through explicit `BridgeTools` folders.
- Use globally unique tool ids such as `mymod/render_pose_sweep`. Collisions are rejected at registration time.
- Keep tool classes public. Use public static methods, or public instance methods on public parameterless classes.
- Keep constructors and static initializers boring. They must not query `RimBridge.Current` or the tool registry because companion registration is still in progress.
- Treat `IRimBridgeContext` and `CancellationToken` as injectable method parameters. They are hidden from the public schema.
- Prefer `Task<object>` or strongly typed DTO results for non-trivial harnesses. Return clear `success`, `message`, and artifact-path fields.
- For repeatable screenshot or behavior suites, return `RimBridgeEvidenceManifest` with captures, assertions, errors, and environment fields instead of inventing a fresh manifest shape.
- For prepared-save or dev-colony setup after the bridge tool surface is available, call `rimworld/load_game_ready` or `rimworld/start_debug_game_ready` directly; do not add a separate pre-wait unless the game should already be loaded.
- If companion tools are missing or fail after SDK changes, call `rimbridge/get_bridge_status` and inspect `version`, `sdk`, and `companions.diagnostics` before guessing at GABS or save-load problems.
- Default paired dev deployments to the global sibling `BridgeTools` root. If the deploy root is `$(RIMWORLD_MOD_DIR)`, use a project-local property such as `$(RIMWORLD_MOD_DIR)\..\BridgeTools`; do not require a separate non-mod-specific BridgeTools environment variable.
- For global companion tools with private helper DLLs, use a first-level bundle folder under global `BridgeTools`, for example `BridgeTools\SomeMod\SomeMod.BridgeTools.dll`. Loose global DLLs are fine only when there are no private dependency-name collision risks.
- For explicitly packaged mod-specific companions, RimBridgeServer can also load a companion beside the load folder's `Assemblies` directory, for example `SomeMod/1.6/BridgeTools/SomeMod.BridgeTools.dll`; use that only when the mod intentionally ships tools inside its own folder instead of the paired global deployment.

## Reference

Read `references/companion-dll-guide.md` before editing project files or writing companion tools. It contains the current csproj patterns, SDK API examples, build target wiring, and validation checklist.
