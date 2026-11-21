# Launch arguments in 1.7.4981 rev136

These are inspected implementation facts, not a claim that every flag was run.
Symbols and build identities are in [sources.md](sources.md). Reinspect after
updating the game build. This inventory covers the inspected game entry
point, Waylith engine switch parser, game bootstrap, asset-pack boot and mod-list
parser; it is not a promise that every mod or native dependency has no other flags.

## Routine automation

| Argument | Behavior in the inspected build |
| --- | --- |
| `-NoWindow` | Sets `EngineApplicationOptions.HideWindow`; graphics/window resources are created, but the window is not shown. Hidden execution bypasses the ordinary unfocused-window pause condition. Linux Wayland has a separate render suppression condition. |
| `-Headless` | Uses the graphics-free update loop. No graphics device or ordinary render loop. Choose only for nonvisual work, and verify the bridge capabilities needed. |
| `-WindowSize=1920,1080` | Parses two comma-separated integers, sets initial dimensions and locks engine size changes. Default engine dimensions are 1920×1080 before preferences/overrides. |
| `-WindowPos=0,0` | Parses two comma-separated integers, sets initial position and locks engine position changes. |
| `-savedatafolder=/absolute/profile` | Overrides Verse save/config data; also supplies the bootstrap's cache-root override. Use an absolute path to avoid working-directory ambiguity. It does not relocate the inspected macOS engine file log. |
| `-mod=id.one;id.two` | One runtime mod-list override; Core and compatible installed official DLCs precede the supplied mods, followed by sorting. See details below. |
| `-quicktest` | Existing quick-start mechanism; changes the Entry scene to Play once. Use bridge readiness/state to establish what game/map actually loaded. |
| `-legacy-xml-deserializer` | Makes `LoadedModManager.ParseAndProcessXML` use `DirectXmlLoader.DefFromNode` instead of the default `DirectXmlToObjectNew.DefFromNodeNew`. Useful for a controlled Def-loading compatibility comparison. |

## Boot diagnostics and logging

| Argument | Behavior |
| --- | --- |
| `-ScreenshotAfterBoot=/absolute/result.png` | Requests a screenshot at counter 120 after boot, on updates with no active or queued long event. A request is not proof of a saved nonempty image; verify the file. Requires a rendering-capable mode. |
| `-MemoryReportAfterBoot` | Logs the engine process-memory report at the same counter 120. |
| `-QuitAfterBoot` | Calls `Root.Shutdown()` at counter 180. Useful for bounded startup checks; omit for an interactive bridge session. |
| `-Verbose` | Enables trace-level/Verbose-category logging. |
| `-SuppressUnityStubs` | Suppresses compatibility-stub warnings. It can conceal evidence; use only deliberately. |
| `-DisableLogStackTraces` | Disables stack traces in the Waylith console formatter. Separate from file-log call stacks. |
| `-NoFileLog` | Disables the engine file log. Avoid for an investigation that needs persistent evidence. |
| `-NoFileLogCallstacks` | Disables call-stack capture for the engine file log. |
| `-NoCrashDialog` | Suppresses the engine crash dialog; headless already disables that dialog. |
| `-CrashHandler` | Windows-only external `Waylith.CrashHandler.exe` launcher; returns immediately on macOS/Linux. Logs attachment success/failure and writes dumps beneath the log root's `Crashes/Dumps`. |
| `-CrashDumpMini` / `-CrashDumpHeap` | Selects mini or heap mode when `-CrashHandler` is enabled; otherwise that handler defaults to full. Mini takes precedence if both flags are supplied. |
| `-CrashUI` | Requests crash-handler UI when `-CrashHandler` is enabled. |
| `-CrashCaptureBreakpoints` | Requests breakpoint capture when `-CrashHandler` is enabled. |
| `-console` | Windows-only console attachment/allocation. `-Headless` also requests this Windows console behavior. |

The after-boot counters are update counters, not wall-clock seconds, game ticks,
or a guarantee of an idle playable colony. Long events suspend the counter's
advancement. Keep bridge lifecycle readiness separate.

## Assets, shaders and graphics troubleshooting

| Argument | Behavior |
| --- | --- |
| `-WatchShaders` | Sets the bootstrap shader-watch mode. Verify the shader source directory and installed watcher path before relying on hot reload. |
| `-ColdBoot` | Sets `ColdBoot.Enabled`; for example, shader compilation bypasses its user-cache reads. Do not describe it as deleting every cache or simulating a fresh OS install. |
| `-TrackRehydration` | Enables GPU texture rehydration tracking. Diagnostic use. |
| `-texCacheCompression=lz4` | Selects LZ4 cache encoding; `none`, `zstd`, and `lz4hc` are also explicitly parsed. Unknown values fall back to LZ4. |
| `-texFormat=bc7` | Selects runtime texture block-compression target. Default is BC7. Parser also recognizes `bc1`/`dxt1`, `bc4`, `bc5`; unrecognized values, including `none`, select no target. Bootstrap separately sets `AllowBc1=false`, so parsing BC1 does not establish that all textures will use it. |
| `-texQuality=1` | Parses an invariant-culture float for texture-compression quality; default is 1. Check the downstream encoder before assuming a supported range. |
| `-AssetPack /absolute/Assets.zip` | Overrides the base asset pack. **Two argv elements, not `-AssetPack=...`.** Default pack is `Assets.zip` beside the apphost. |
| `-NoAssetPack` | Skips the base pack. Loose assets must actually exist; this is not a general faster-start switch for a packed installation. |
| `-BakeModAssetPacks=/absolute/modA;/absolute/modB` | Runs mod asset-pack baking instead of ordinary game startup, then exits with success/failure status. Takes filesystem roots, not package IDs. Writes mod assets; use a staging copy and verify outputs before release. |
| `-DisplayCaptureCompatibilityMode` | Disallows exclusive fullscreen in engine graphics options. It is not the hidden-window switch. |
| `-KosmicKrisp` | Requests the alternate Vulkan implementation on macOS. Treat as a platform/driver experiment, not a universal optimization. |
| `-BundledVulkanLoader` / `-SystemVulkanLoader` | Chooses the Vulkan loader; bundled takes precedence if both supplied. With neither, loader choice is Newest. Availability varies by platform. |
| `-NoVulkanLayers` | Disables implicit Vulkan layers. Use for a layer/overlay diagnosis with the change recorded. |
| `-NoVulkanFallback` | Disables the engine's Vulkan fallback. Diagnostic use. |
| `-ResetVulkanFallback` | Requests reset of fallback state. Inspect state paths before changing an existing user's setup. |
| `-EnableVulkanValidation` | Enables available Vulkan validation layers plus best-practices/synchronization validation features. This reader uses a case-sensitive substring of `Environment.CommandLine`; preserve this exact spelling. It logs when the validation layer is unavailable. Diagnostic overhead is not a gameplay-performance baseline. |
| `-disable-compute-shaders` | Exact case-sensitive argv match in `Verse.UnityData.CopyUnityData`; forces Verse's compute-support flag false. This is not proof all engine GPU compute is disabled. |

`-WaitForExitingPid=PID` is also implemented by `Waylith.RestartManager` for
replacement-process startup. It waits at most 15 seconds and then proceeds.
The restart path preserves other argv, removes prior wait-PID switches, and
appends its own. This is an internal lifecycle aid, not a replacement for GABS
ownership/readiness checks or a command that terminates the preceding process.

## Mod-list behavior and config preservation

Live build4981 tests selected Workshop Harmony successfully with numeric item ID
`2009463077`. The suffixed ID `brrainz.harmony_steam` failed lookup and Harmony
was omitted after a warning. Use the numeric ID for that Workshop item and verify
the actual loaded root. The list is sorted, not an exact-order fixture; use an
isolated ModsConfig profile for precise order or exclusion of installed DLCs.

`TryGetCommandLineModList` accepts either `-mod=...` or `-mod` followed by the
value, case-insensitively. It takes the **first** occurrence, splits on semicolons
or commas, trims whitespace and discards empty entries. Use one complete list.

`ApplyCommandLineModList` clears the runtime list, adds Core, adds official
non-Core version-compatible installed mods, resolves the supplied IDs, removes
duplicates and invokes `TrySortMods`. It first tries a lowercased package ID
with Steam-postfix tolerance, then an exact folder-name fallback. Prefer package
IDs. Missing mods produce a warning and are skipped; a successful launch alone
does not prove the requested mod was loaded. Declared load-order constraints can
change the supplied order. Required third-party dependencies are not explicitly
added by this method, so include them yourself and inspect the actual result.

The user's supplied documentation says no ModsConfig.xml reads or writes occur.
That differs from the inspected build: `ModsConfig`'s static constructor first
loads the existing XML, may call `Reset`/`Save` for missing/outdated data or
maintenance, and only then applies the override. `ModsConfig.Save` writes without
checking `-mod`. The override method itself does not call `Save`. These facts
establish an isolation requirement, not that every launch overwrites the file.

Use `-savedatafolder` with an isolated profile until a newer build's implementation
or an actual before/after filesystem check establishes stronger guarantees.
`-mod` does not isolate mod-specific settings, save files or a mod that writes
outside the normal data root.

## Parsing and example argv

Waylith's engine switches trim leading/trailing `-`/`+` and compare names without
case. Engine `GetValue` uses `name=value`; `GetFollowingValue` uses the next argv
element. Verse's value parser requires exactly one `=` in the whole argument,
so avoid `=` inside the values of its flags. Different parsers mean one syntax
does not imply support for another.

An example GABS `args` fragment, to merge into an appropriate existing profile:

```json
[
  "-NoWindow",
  "-WindowSize=1920,1080",
  "-savedatafolder=/absolute/project/artifacts/rimworld-17-profile",
  "-mod=2009463077;brrainz.rimbridgeserver;your.package.id"
]
```

The paths and final package ID above are placeholders, not installed configuration.
Do not add shell quote characters inside those JSON strings. For a shell launch,
pass the mod list as a quoted string so `;` is not interpreted by the shell.

Legacy Unity examples such as `-batchmode`, `-nographics`, `-logFile`,
`-screen-width`, `-screen-height`, `-screen-fullscreen` and `-popupwindow` are
not established by this inventory. Find a reader in the installed build or a
current engine-author statement before relying on them.
