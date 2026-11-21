# Sources, version and research coverage

## Initial installed-code snapshot

Inspected locally on 2026-10-17 using DecompilerServer MCP. Game root:
`~/Library/Application Support/Steam/steamapps/common/RimWorld`.
`Version.txt` reports **1.7.4981 rev136**. Inspected assemblies are in
`RimWorld.app/Contents/MacOS/`, not the coexisting old `RimWorldMac.app`.

| Assembly | MVID | SHA-256 |
| --- | --- | --- |
| Assembly-CSharp.dll | `f1e6382600a84191bbc6d8a184fd6b57` | `eb0826b85af430d13962e5c097ef53d869cf6a031656fb6a3673168a761c9f23` |
| RimWorld.dll | `074eea848ae24e97819f22055f974055` | `bd5c274966ba78ba584b6bc7473d27f6a9729dbffa22cf55766616bc4d544c9b` |
| Waylith.dll | `fbe66ae50ee044898f92424459f70a7e` | `4b14bc007509ca1ed31924939ec6106ef05b32e9726905cd6e63fba91952df36` |

Also inspected the shipped `RimWorld.runtimeconfig.json` and `Readme.txt`;
registered UnityStubs.dll and Waylith.UnityCompat.dll with DecompilerServer for
follow-up. The old Readme contains historical instructions and is not a complete
1.7 launch manual.

## Implementation evidence map

| Topic | Inspected code |
| --- | --- |
| Entry-point flags, logging, headless, crash options | `RimWorldWaylith.Program.Main`, `InitLogger`, `WindowsConsole.OpenIfRequested`, `CrashHandlerLauncher.TryLaunch/ResolveDumpDir` in RimWorld.dll |
| Engine switches and argv syntax | `Waylith.CLI.EngineSwitches.Apply`, `CommandLineExtensions.HasSwitch/GetValue/GetFollowingValue/GetInt2` |
| Hidden window versus graphics-free execution | `Waylith.EngineApplication` constructor, `RunLoop`, `RunHeadlessLoop`; `RimWorldWaylith.Bootstrap.Initialize`, `ConfigureFrameRenderer`, `OnHeadlessPrePollEvents` |
| Boot screenshot, memory report and exit | `Bootstrap.RunAfterBootSwitches` |
| Texture format/quality, shader watching | `Bootstrap.ConfigureTextureCompression`, `ConfigureShaderHotReload`; `Waylith.Rendering.RuntimeShaderCompiler` |
| Base/mod asset packs | `Waylith.Assets.AssetPackBoot.Open`; `RimWorldWaylith.ModAssetPackBake.Requested/Run/BakeMod/LoadFolders` |
| Mod list and persistence discrepancy | `Verse.ModsConfig` static constructor, `TryGetCommandLineModList`, `ApplyCommandLineModList`, `Save`; `Verse.GenCommandLine` |
| Data roots, caches, file logs | `Verse.GenFilePaths.SaveDataFolderPath/ModsConfigFilePath`; `Bootstrap.ConfigureRuntime/ResolveCacheRootOverride/ResolvePersistentDataRoot/ResolveLogDataRoot`; `Waylith.Logging.FileLog` |
| Profiler UI, settings and controls | `Verse.ProfilerUI.DebugActionsFrameProfiler`, `ProfilerPrefs`, `ProfilerPrefsData`, `Window_FrameProfiler.HandleKeys`; `Waylith.Profiling.FrameProfiler` member surface |
| GPU override and budget | `Waylith.Graphics.GraphicsDevice.SelectQueueFamilies`, `ReadVramLimitOverride` |
| Other diagnostic and lifecycle arguments | `Verse.LoadedModManager.ParseAndProcessXML`, `Verse.UnityData.CopyUnityData`, `Waylith.Graphics.GraphicsDevice.CreateInstance/GetEnabledInstanceLayers`, `Waylith.RestartManager` |

Decompiler contexts used: `rw17-launch-4981`, `rw17-entry-4981`, `waylith-4981`,
`unitystubs-4981`, `unitycompat-4981`. Resolve symbols again after updates rather
than trusting an alias still points to the current installed file.

## User-provided evidence

- The user reports that windowless RimWorld 1.7 works with RimBridgeServer's
  synthetic keys/mouse and screen grabbing. No fresh runtime test was performed
  as part of the initial code/documentation inspection.
- The supplied launch-argument image describes `-mod=<packageIdOne>[;<packageIdTwo>...]`
  as adding Core/all installed DLC and not reading or modifying ModsConfig.xml.
  The installed build confirms the runtime-list feature but differs on config
  reads and potential writes. The references preserve that discrepancy.

## Developer sources

Sources are Waylith developer statements on the Waylith Discord, the
[Modder Primer for 1.7](https://docs.google.com/document/d/1PtuKCNbh1z_hlkcW9T4xbdHVRjinx8TdIVmEGg6yook/edit)
and inspected game builds. Snapshot date: 2026-10-17. The Primer was last
modified 2026-10-13 23:36:22 UTC. It documents launch options, jobs, profiling
and shader contracts. Its `-mod` persistence promise matches the screenshot
above but conflicts with the build-4981 code inspection. Keep that qualification.
Primer claims of compatibility or performance are goals or reported observations,
not measurements.

[Developer notes](waylith-notes.md) hold the selected operational findings,
dated Discord permalinks and superseded advice.

## Validation boundary

The initial skill update is an implementation/documentation investigation.
No player's ModsConfig.xml, save, game files, GABS profile or launch options were
changed, no mod asset pack was baked, and no game was started. GABS status
reported all configured games stopped at inspection time. Current bridge
connection/input/capture behavior remains a separate runtime acceptance check.
