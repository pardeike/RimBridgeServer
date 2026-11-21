# Autonomous development: proven subset

Validated 2026-10-17 on macOS ARM64, RimWorld 1.7.4981 rev136 and bundled
.NET 10.0.13-waylithclr.dev. Revalidate on other builds/platforms.

Use the existing RimBridgeServer/GABS lifecycle and discover tool contracts.
Never take over another agent's game. Preserve player settings and use an isolated
save-data root. The proof runner refuses an existing game and removes only its
own fixture mod/companion after verified termination.

## Repeat the proofs

From the RimBridgeServer checkout, with the game stopped:

```sh
scripts/live-smoke.sh --waylith-poc --game-root '/absolute/Steam/steamapps/common/RimWorld' --gabs-bin /absolute/path/to/gabs
```

This existing smoke command builds the optional Waylith companion, stages it
outside the player mod ZIP, launches isolated hidden/headless GABS profiles,
and cleans up. Logs, JSON results, screenshots, generated deltas and packs remain
under ignored `artifacts/waylith-proof/<run>/`. It expects the installed bridge and
Workshop Harmony item 2009463077. Inspect resolved mods, not just launch arguments.
First complete run including packed-only texture rendering:
`artifacts/waylith-proof/20261017-103730-204/results.json`.

| Technique | Live proof | Supported use / boundary |
| --- | --- | --- |
| Hidden window | Nonempty decoded bridge PNG; shader pixel red to green | Visual checks without occupying the desktop; verify size/UI scale |
| Headless | Exactly 60 semantic ticks; added and edited tests execute | Simulation/semantic tests only: screenshot timed out with no file, despite bridge visualReady=true |
| Native pause on load | Two loads paused at saved tick + 1 | Enable Prefs.PauseOnLoad before loading; +1 is intentional Game.LoadGame behavior, not exact tick restoration |
| Add test methods | Existing()=1, insert AddedTest()=41, edit to42 in same assembly MVID | Synchronous public static zero-argument annotated tests on sealed development types |
| Native profiler | Bounded capture returns scalar frame rows; prior enabled/paused flags restored | Broad frame diagnosis; no proof of arbitrary mod instrumentation or replacement for a mod profiler |
| Shader watch | Raw GLSL edit changes sampled pixel (255,0,0) to (0,255,0) in same process | Shader iteration with -WatchShaders; ShaderDef/content reload not established |
| Asset bake | CPU/headless and GPU/hidden produce DDS+manifest archives | CPU pack also loaded/rendered with loose Textures absent; GPU archive runtime loading not separately proved |
| Synthetic input | Earlier quicktest key/mouse worked; Space after save/load failed to unpause | Prove the observable effect in the actual screen/state; use semantic pause controls for checkpoint tests |

## Adding and editing test methods without a restart

The optional `Companions/Waylith` companion exposes a fixed
`waylith/development_tests` dispatcher. See its README and the maintained
`WaylithDeltaFixture.cs` for the Roslyn EmitDifference recipe. Compile a baseline,
retain its metadata/symbol baseline, and produce ordered metadata/IL/PDB deltas.
Load that baseline once, apply generation1 (new method), list/run AddedTest,
then apply generation2 (body edit) and run it again. The baseline SHA and expected
generation must match. Never replay an uncertain update; restart after a mutation
failure. A new baseline requires a restart.

ApplyUpdate alone left reflection enumeration stale. The proof explicitly calls
Waylith corelib's RuntimeTypeMetadataUpdateHandler.ClearCache(Type[]) after each
update, restricted to sealed test types: unsealed types can trigger clearing all
runtime-type caches. This is runtime-specific and rechecked before mutation.
The ordinary bridge/MCP tool registry is startup-only and is NOT refreshed:
new annotations are found only by this fixed dispatcher. Do not promise that
adding a method automatically adds a new MCP tool.

This supports iteration, not final acceptance. Full build, deployment, restart
and a clean live reproduction remain required. Arbitrary mod reload, field/layout
changes, closures, async state machines and Harmony-patched target updates were
not proved. Do not generalize the test assembly result to those cases.

## Operational details

- `waylith/status` reports actual argv mode, framebuffer size, pause preference,
  profiler flags and metadata-update support. Do not trust visualReady alone.
- Headless currently raises the known 0x0 resolution error. Read attention and
  acknowledge only that exact expected fixture error; stop on unknown attention.
- `waylith/profile_capture` refuses an already-active capture and headless mode,
  restores prior profiler flags, and does not enable instrumentation or clear history.
- Native -mod adds compatible installed DLCs and sorts the list. It is not a
  Core-only or exact-order profile. Workshop suffix package IDs such as
  brrainz.harmony_steam failed lookup and silently omitted Harmony in this build;
  numeric item2009463077 worked. Verify Harmony's actual loaded root.
- Derive BridgeTools from the lexical game Mods parent before resolving symlinks;
  resolving this installation's Mods symlink first selected the wrong sibling.

## Coordinate screenshot support

The existing screenshot tools also support screen rectangles and target-relative rectangles. Read [screenshots](screenshots.md) for coordinate origins, HiDPI scaling and repeatable crop/source pixel verification. Map-cell capture preserves the camera by default.
