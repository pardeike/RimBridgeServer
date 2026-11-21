# Engine facilities to use deliberately

Implementation snapshot: 1.7.4981 rev136, 2026-10-17. See
[sources.md](sources.md) for provenance and validation boundaries.

## Built-in frame profiler

The game includes `Verse.ProfilerUI.Window_FrameProfiler` and Waylith's
`FrameProfiler`. The debug action **General → Frame profiler** is registered for
both Entry and Playing states. This is separate from RimBridgeServer's Dubs
Performance Analyzer tools; do not call a DPA tool and label its output as a
Waylith capture.

The inspected implementation supports frame history, call-tree/flamechart views,
spikes, thread timelines, allocation tracking when supported by the runtime,
GC/runtime pauses and method instrumentation. `ProfilerPrefsData` includes
`startOnBoot`, `keepRunningWhenClosed`, `historyCapacity`, `allocTracking`,
`mainThreadOnly`, `ignoreThrottle`, `instrumentationTargets`, `instrumentation`,
`autoMuteTrivial`, `budgetMs` and `foldRecursion`. Preferences live in
`Config/Profiler.xml` under the Verse save-data profile.

Default method targets are `Assembly-CSharp!Verse.*::*` and
`Assembly-CSharp!RimWorld.*::*`. The preferences loader excludes `0Harmony`,
`MonoMod.*` and `Mono.Cecil*` from instrumentation. Inspect the pattern parser and
runtime support before supplying other targets; do not assume excluded internals
are measured or add broad instrumentation to performance baselines silently.

Within the profiler window, the inspected key handler uses Space for capture
pause, PageUp/PageDown for stepping, Home for newest, arrows for tree selection,
F3/Shift-F3 for search matches, Ctrl-C for a selected row and Ctrl-Shift-C for
the view. These are window-local input handling facts, not global shortcuts or
evidence that macOS Command-C is accepted in place of Control-C.
`CopyView` includes summary, median/p99/budget text and the active section in
the profiler clipboard; this is a concrete text-capture path, not an assertion
that a general trace-file exporter exists.

For a useful capture:

1. Record build, runtime, mod IDs/versions, map/save, speed and test duration.
2. Start with the narrow scenario and only the collection needed. Allocation
   tracking and method instrumentation change the workload; record them.
3. Use semantic bridge actions to reproduce the event. Discover a profiler tool
   if present; otherwise use the debug action and bridge layout/input tools.
4. Capture/export or copy the relevant data, then stop collection and restore
   previous profiler settings. Closing the window can leave collection active
   when `keepRunningWhenClosed` is enabled.
5. Separate profiler evidence from actual frame rate, tick throughput and a
   reproduced player-visible problem. Headless timing is not a rendering benchmark.

The assembly picker can select a whole assembly or only its Harmony patch
methods. For the developer's capture workflow, Release Edit and Continue, runtime IL/re-JIT
hooks, native jobs/render features and shader porting details, read
[Waylith developer notes](waylith-notes.md).

## Runtime and compatibility

The apphost's `RimWorld.runtimeconfig.json` identifies `net10.0` with bundled
`Microsoft.NETCore.App` **10.0.13-waylithclr.dev**. It enables engine-specific
compatibility behavior, including relaxed assembly/member binding and access,
dynamic-method compatibility, patchable Assembly-CSharp JIT handling, Mono-style
string hashing/float formatting and Unity assembly redirects. The configuration
redirects `UnityEngine*`/`Unity.*` to `UnityStubs` and preserves other legacy mod
compatibility behavior.

These are shipped runtime choices, not instructions to change host settings.
Use the game's apphost and current 1.7 reference/dependency profile when testing.
Do not assume CoreCLR tiering, private runtime details, patching, stack traces,
assembly identity or Unity APIs behave exactly like stock .NET or old Unity/Mono.
Many Unity-facing APIs are compatibility shims, and a stub that exists is not
proof its behavior is implemented. Retain stub diagnostics when investigating.

Use `$decompiler-mcp` on the actual 1.7 assemblies. Do not accidentally inspect
the stale 1.6 Unity managed folder merely because an old alias or app remains.

## Rendering, capture and shader iteration

The Waylith host uses Vulkan, with separate main/render-thread work and an ImGui
backend for the game's GUI. `Bootstrap.ConfigureFrameRenderer` registers
`ScreenCaptureFeature` and installs `ScreenCaptureBackend.Current` only on the
graphics path. The headless branch does not configure this renderer/backend.
This reinforces choosing `-NoWindow` for visual automation.

`-WatchShaders` reaches `ShaderHotReloader.WatchContentShaders`, and the bootstrap
ticks that reloader before event polling. Confirm the source-root and touched
shader's supported path before claiming a reload. This does not establish C# mod
DLL hot reload or arbitrary XML/Def hot reload.

For graphics regressions, keep one option different at a time: loader selection,
implicit layers, fallback, capture compatibility, alternate macOS Vulkan driver,
texture format/quality, or cache state. Record the selected GPU/driver from logs.
Do not turn all diagnostics or fallbacks off as a generic performance preset.

Two inspected environment overrides are useful for bounded reproduction:

| Environment variable | Verified reader behavior |
| --- | --- |
| `WAYLITH_VULKAN_DEVICE` | Case-insensitive GPU-name substring. Selects a matching usable device; logs and falls back if none is usable/matches. Not a numeric index or UUID selector. |
| `WAYLITH_VRAM_BUDGET_MB` | Positive integer megabytes; sets the engine's device-local budget override. Invalid/nonpositive values are ignored with a warning. This does not create physical VRAM. |

Other indexed diagnostic knobs include `WAYLITH_NO_ASSET_PREFETCH`,
`WAYLITH_LOAD_ENCODE`, `WAYLITH_SLANG_COMPILER`, `WAYLITH_PROFILER_NO_TSC`,
`WAYLITH_PROFILER_FENCED_TSC`, `WAYLITH_CURSOR_SIZE`,
`WAYLITH_DISABLE_WAYLAND_GAME_CONTENT`, `WAYLITH_STAGE_UPLOADS_IN_RAM` and
`WAYLITH_NATIVE_GUARD_PAGES`. Their names were found in the installed engine;
do not infer value syntax, platform support or a recommended setting from the
name alone. Inspect the reader before use. `FORCE_X11` and
`ENABLE_VULKAN_RENDERDOC_CAPTURE` are also read in platform setup.

## Assets and caches

The base asset pack defaults to `Assets.zip` beside the apphost. The engine can
open a specified pack with `-AssetPack PATH`, or use loose assets with
`-NoAssetPack` when they actually exist. Texture block compression and cache
compression are separate controls. Compare equal texture quality and warm/cold
state when measuring startup or memory.

`-BakeModAssetPacks=ROOT_A;ROOT_B` accepts mod filesystem roots, resolves their
`LoadFolders.xml`, selects load folders with textures/sounds and calls
`ModAssetPacks.BakeForUpload`. It reports entry/reuse/deduplication/failure counts
and duration, and returns a failing exit code for failure. It replaces normal
game startup; this is not a bridge launch recipe. Bake a staging copy, inspect
the output and test it before including anything in a release. Baking itself
does not authorize upload/publication and is not proof the player build works.

No cache deletion is needed to use `-ColdBoot`. Its code-level effect includes
bypassing shader user-cache reads; it is not established as bypassing every
precompiled asset-pack cache. Record that distinction in benchmarks.

## Save data, caches and logs on macOS

The inspected paths are deliberately different:

| Data | Default in this build |
| --- | --- |
| Verse player saves/config | `~/Library/Application Support/RimWorld/` |
| Engine persistent/cache root | `~/Library/Application Support/ludeon.rimworld/` |
| Engine file log | `~/Library/Application Support/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log` |
| Previous engine file log | Same directory, `Player-prev.log` |

`-savedatafolder=ABSOLUTE_PATH` changes Verse's save/config root and supplies a
cache-root override, including the shader-cache directory. The inspected
`ResolveLogDataRoot` does not consult it on macOS. Do not assume an isolated
save profile has an isolated engine log, or that an inherited Unity `-logFile`
argument moves it. Multiple test processes can contend for a shared log; use
process-specific bridge logs/stdout where supported and preserve required logs
before another startup rotates them.

`-MemoryReportAfterBoot` calls `ProcessMemoryReport.Log`. `-CrashHandler` is a
Windows-only sidecar path in this build; on macOS/Linux its launcher returns
without starting anything. Check resulting files rather than assuming a flag
produced a dump.
Log/screenshot requests need a nonempty artifact and inspection before they
count as evidence. The traditional installed Readme.txt still contains Unity-era
paths; current bootstrap code is stronger evidence for this engine build.

## Live validation, 2026-10-17

The subsequent build4981 experiments are recorded in [autonomous development](autonomous-development.md). That reference distinguishes runtime proof from earlier source/Discord evidence, including inserted test methods with scoped reflection-cache refresh, bounded profiler capture, raw shader reload and packed-only texture rendering. Headless screenshot failure and post-load synthetic Space limitations remain explicit.
