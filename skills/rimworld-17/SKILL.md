---
name: rimworld-17
description: "Use when launching, automating, profiling, troubleshooting, or developing against RimWorld 1.7 and its Waylith engine. Covers hidden-window versus headless execution, isolated mod lists and save data, launch arguments, built-in profiling, runtime compatibility, graphics diagnostics, and asset baking. Pair with rimbridge-server for live game tools and rimworld-mod-development for mod changes."
---

# RimWorld 1.7 and Waylith

Use the installed game's version and assemblies as the authority for what that
build implements. The initial reference is **1.7.4981 rev136**, inspected on
2026-10-17. Pre-release builds change quickly. Read
[sources and coverage](references/sources.md) before claiming a feature is
documented, implemented, or tested.

## Choose the execution mode

| Goal | Mode |
| --- | --- |
| Bridge screenshots, synthetic keys/mouse, visual regression checks without occupying the desktop | `-NoWindow`, with graphics enabled |
| Visible manual reproduction or verifying OS-window behavior | Ordinary windowed launch |
| Simulation or startup work that does not need rendered pixels | Consider `-Headless`, after confirming the required tools work there |

`-NoWindow` hides the native window but keeps the graphics path. `-Headless`
skips graphics-device initialization and uses an update-only loop. Do not use
the names interchangeably, combine them expecting rendered headless output,
or replace them with Unity's `-batchmode`/`-nographics` by habit. On Linux
Wayland, the inspected hidden-window render loop has an additional suppression
condition; macOS behavior is not proof for that platform.

Live build4981 proofs confirm hidden-window screenshots and shader rendering.
Synthetic input is state-dependent: Space after save/load did not unpause.
Headless semantic ticks work but screenshots fail, even when visualReady is true.
A new setup still needs its own observable-effect smoke test. Use bridge-owned screenshots
and synthetic input, not desktop capture or OS mouse events targeting a hidden
window. Discover the current tool contracts with `$rimbridge-server`.

## Isolate launch state

1. Discover the actual 1.7 executable, build, installed mod roots and package
   IDs. An old `RimWorldMac.app` can coexist with the new `RimWorld.app`; the
   directory's presence is not proof of which executable Steam launches.
2. Reuse an appropriate isolated GABS profile. Keep saves, configuration,
   caches and logs in the existing project/test locations. Do not alter the
   player's ordinary active mods, preferences, saves, or Steam launch options
   merely to run a test.
3. For a minimal bridge setup, use one literal argv element such as
   `-mod=2009463077;brrainz.rimbridgeserver;your.package.id`, plus an
   absolute `-savedatafolder=...` pointing to the test profile. Core and installed
   compatible official DLCs are added automatically. Include required third-party
   dependencies explicitly, and verify the resolved list and actual roots.
   The example selects Workshop Harmony by its tested numeric item ID. Do not
   use `brrainz.harmony_steam`: build 4981 skipped it after a lookup warning.
   The engine sorts the list; this is not an exact-order or Core-only profile.
   Use an isolated ModsConfig profile when the test needs exact load order.
4. **Do not promise that `-mod` leaves ModsConfig.xml unread/unwritten.** The
   supplied documentation image says this, but build 4981 reads the file before
   applying the override and its `ModsConfig.Save` has no override guard.
   Use isolated save data and compare config bytes when preservation matters.
5. Use `-WindowSize=1920,1080` when stable window dimensions help. Inspect bridge
   screenshot dimensions and UI scale rather than assuming window coordinates
   equal framebuffer pixels on HiDPI displays.

See [launch arguments](references/launch-arguments.md) for parsing details,
defaults, platform differences and diagnostics. Quote the complete `-key=value`
argument at a shell boundary when it contains spaces or semicolons. In GABS JSON
`args`, each string already is an argv element: do not embed shell quote marks.

## GABS and bridge validation

- Inspect `games_show` before choosing a profile. Names such as "headless" or
  "1.6" can be historical; verify the resolved executable and delivered argv.
- Use `SteamManaged`, `DirectPath` or `CustomCommand` when GABS must deliver
  arguments/environment. Legacy `SteamAppId` URL launch does not deliver that
  context. Read GABS's configured launch-input schema; `games_start` does not
  accept arbitrary undeclared launch inputs.
- Put data roots/lifecycle identity in named profiles. Use declared launch inputs
  for lifecycle-neutral options such as quicktest or diagnostics. Do not leave
  duplicate `-mod`, size or data-root arguments in base and profile args; the
  inspected parsers generally take the first matching value.
- Check session ownership and active processes before starting anything. Preserve
  another session's game. Follow `$rimbridge-server` for connection, readiness,
  semantic tools, screenshots and input; do not introduce a second launcher or
  a fixed port/token.
- A hidden-window smoke test must verify bridge connection, resolved mods,
  nonempty screenshots at the expected size, and observable effects of both a
  synthetic key and a mouse action. Restore changed game state and stop only
  the test instance owned by this task. A source inspection is not this test.

## Use the engine's diagnostic facilities

Read [engine features](references/engine-features.md) for the built-in frame
profiler, memory reports, crash diagnostics, shader watching, texture/cache
controls and asset-pack baking. Prefer a bounded capture with explicit settings
over turning on every diagnostic. Record the game build, mods, scenario, warm
or cold caches, rendered/headless mode and profiler overhead with the result.

Use the bundled game apphost/runtime for runtime claims. This build uses a
custom .NET 10 WaylithCLR and Unity compatibility assemblies; neither an ordinary
Mono/net472 test nor a stock .NET 10 test establishes equivalent in-game behavior.
Use `$decompiler-mcp` for binary inspection and the shared mod-development skill
for dependency profiles. Do not rewrite the shipped runtime configuration to
make a test pass.

Read [Waylith developer notes](references/waylith-notes.md) for dated developer
statements and the Modder Primer: Release Edit and Continue, runtime IL/re-JIT
hooks, patch-only profiling, native jobs/render features, shader porting traps,
and GPU-free asset baking. These are capabilities to verify in the current
build, not permission to change the player's setup.

## Mod development conventions

- Put active 1.7 content in `1.7/`: assemblies, native shader defs in
  `1.7/Defs/ShaderDefs` and GLSL in `1.7/Shaders`. Keep older version folders
  frozen and byte-identical. If a shared resource changes for 1.7, move or
  duplicate it into `1.7/` instead of editing it in place.
- Reference game assemblies with `Krafs.Rimworld.Ref` `1.7.4990-unstable` and
  Harmony with `Lib.Harmony.Ref` `3.0.0-rc.5` from NuGet.org. Restrict
  `NuGet.Config` to nuget.org.
- On macOS, the 1.7 Mods folder is the outer installation `Mods` directory beside
  `RimWorld.app`. Do not select the older `RimWorldMac.app/Mods` alias.

## Autonomous feature and bug-fix iteration

Read [proven autonomous-development techniques](references/autonomous-development.md)
for the repeatable live proof command, optional existing-tool integration, added
test methods, native checkpoint semantics, profiler and asset/shader iteration.
Use only the proved subset; finish with a full build/deploy/restart validation.

For map-cell screenshots, whole-dialog crops and screen/window-relative rectangle coordinates, read [screenshots](references/screenshots.md). Use hidden rendering for unattended captures; true headless mode has no framebuffer.
