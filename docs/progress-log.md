# RimBridgeServer Progress Log

## Working Rules

- Each completed incremental step updates this file before commit.
- Each step should record what changed, how it was verified, and what comes next.
- Git history is the authoritative record of commit hashes. This log focuses on implementation progress and verification.

## 2026-03-16 - Step A0 - Architecture Baseline

Status:

- completed

Completed:

- reviewed the current RimBridgeServer codebase and identified the current monolithic tool surface
- validated the current build already uses `Krafs.Rimworld.Ref` with `Assembly-CSharp_publicised.dll`
- used the decompiler against the publicized RimWorld reference to confirm core seams for save/load, long events, screenshots, logs, messages, letters, debug actions, commands, designators, and mod settings
- reviewed `pardeike/Achtung2` as a reference for the same publicized assembly workflow
- added [`docs/architecture.md`](./architecture.md) with the target architecture, capability model, test strategy, and low-risk roadmap

Verification:

- repository inspection
- decompiler inspection of the publicized RimWorld assembly
- reference inspection of Achtung2 build setup

Notes:

- the project already has the correct publicized RimWorld reference pattern, so the main work is architectural extraction rather than dependency change
- reflection should remain limited to optional third-party mod adapters
- first-party capability groups should use the same provider contract as third-party extensions so the system can be split into packages by scope without a second internal model

Next:

- Step A1: extract shared contracts and the standard operation envelope while preserving existing tool ids

## 2026-03-16 - Step A1 - Shared Contracts and Provider Abstractions

Status:

- completed

Completed:

- added a multi-target [`RimBridgeServer.Contracts`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Contracts/RimBridgeServer.Contracts.csproj) project for capability descriptors, invocation contracts, operation envelopes, warnings, and errors
- added a multi-target [`RimBridgeServer.Extensions.Abstractions`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Extensions.Abstractions/RimBridgeServer.Extensions.Abstractions.csproj) project for capability provider registrations shared by first-party and third-party packages
- added a first-party [`BuiltInToolCapabilityProvider`](/Users/ap/Projects/RimBridgeServer/Source/BuiltInToolCapabilityProvider.cs) that describes the existing built-in tools through the same provider abstraction intended for extension packages
- wired the current host project to reference the new shared projects
- added [`LegacyToolExecution`](/Users/ap/Projects/RimBridgeServer/Source/LegacyToolExecution.cs) so existing tool responses now include a non-breaking `operation` metadata object derived from the standard envelope while keeping the current tool ids and payload fields
- updated [`RimBridgeTools`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) so `rimbridge/ping` and `rimworld/take_screenshot` use the same standardized execution wrapper pattern as the rest of the tool surface
- added a focused [`RimBridgeServer.Contracts.Tests`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Contracts.Tests/RimBridgeServer.Contracts.Tests.csproj) test project covering envelope metadata, result projection, execution mode flags, descriptor-only registrations, and serialization

Verification:

- `dotnet test Tests/RimBridgeServer.Contracts.Tests/RimBridgeServer.Contracts.Tests.csproj`
- `dotnet build RimBridgeServer.sln`

Notes:

- the repo-wide `ProjectGuid` in [`Directory.Build.props`](/Users/ap/Projects/RimBridgeServer/Directory.Build.props) required explicit per-project overrides for the new SDK projects before they could be added to the solution cleanly
- the source project needed explicit compile exclusions for the new subproject folders because SDK default globs would otherwise compile the shared contract source files twice
- contract-side test targets were moved to `net10.0` to match the runtime available on this machine while leaving the mod-facing target on `net472`

Next:

- Step A2: extract the execution kernel into explicit dispatcher and operation-runner components instead of keeping execution policy inside the legacy tool facade

## 2026-03-16 - Step A2 - Execution Kernel Extraction

Status:

- completed

Completed:

- added a multi-target [`RimBridgeServer.Core`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/RimBridgeServer.Core.csproj) project for execution-kernel types shared between the mod host and test projects
- extracted `IGameThreadDispatcher`, `OperationExecutionOptions`, and `OperationRunner` into [`OperationExecution.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/OperationExecution.cs)
- added [`MainThreadDispatcher`](/Users/ap/Projects/RimBridgeServer/Source/MainThreadDispatcher.cs) in the host as the RimWorld-specific adapter over `RimBridgeMainThread`
- refactored [`LegacyToolExecution`](/Users/ap/Projects/RimBridgeServer/Source/LegacyToolExecution.cs) so the legacy tool facade now only resolves tool ids, delegates execution to the shared runner, and projects the resulting envelope back into the current response shape
- added a focused [`RimBridgeServer.Core.Tests`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj) project covering main-thread dispatch usage, background execution bypass, and exception-to-failure-envelope behavior
- updated the source project to exclude the new core subproject files from SDK compile globs and reference the extracted core assembly cleanly

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`

Notes:

- the execution kernel is still small, but it is now isolated enough to evolve toward wait conditions, operation journals, and richer async policies without burying that logic back inside the GABP-facing facade
- this step keeps the outward tool ids and top-level payloads stable while reducing the amount of host-specific execution logic living in [`RimBridgeTools`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) and [`LegacyToolExecution`](/Users/ap/Projects/RimBridgeServer/Source/LegacyToolExecution.cs)

Next:

- Step A3: move the built-in features behind registry-backed capability modules and preserve the current GABP tool names as aliases over that registry

## 2026-03-16 - Step A3 - Registry-Backed Built-In Capability Modules

Status:

- completed

Completed:

- added [`CapabilityRegistry`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityRegistry.cs) to register capability providers, resolve aliases, and invoke capabilities through the shared registration model
- replaced the metadata-only built-in provider with [`BuiltInCapabilityModuleProvider`](/Users/ap/Projects/RimBridgeServer/Source/BuiltInCapabilityModuleProvider.cs), which maps capability module methods to descriptors and handlers while preserving the current GABP tool ids as aliases
- split the current built-in behavior into focused first-party modules: [`DiagnosticsCapabilityModule`](/Users/ap/Projects/RimBridgeServer/Source/DiagnosticsCapabilityModule.cs), [`LifecycleCapabilityModule`](/Users/ap/Projects/RimBridgeServer/Source/LifecycleCapabilityModule.cs), [`SelectionCapabilityModule`](/Users/ap/Projects/RimBridgeServer/Source/SelectionCapabilityModule.cs), [`ViewCapabilityModule`](/Users/ap/Projects/RimBridgeServer/Source/ViewCapabilityModule.cs), and [`ContextMenuCapabilityModule`](/Users/ap/Projects/RimBridgeServer/Source/ContextMenuCapabilityModule.cs)
- added [`RimBridgeCapabilities`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeCapabilities.cs) as the host bootstrap for trusted first-party providers so built-in packages now register through the same provider contract intended for future extension packages
- reduced [`RimBridgeTools`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) to transport-facing alias wrappers and updated [`LegacyToolExecution`](/Users/ap/Projects/RimBridgeServer/Source/LegacyToolExecution.cs) to resolve aliases through the registry instead of executing host-owned handlers directly
- added [`CapabilityRegistryTests`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityRegistryTests.cs) covering alias resolution, alias invocation, and duplicate-alias rejection across providers

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`

Notes:

- first-party modules now use the same provider registration path that extension packages will use, which removes a major architectural split before it can harden into the codebase
- capability ids are now internal and transport-agnostic, while the existing GABP names continue to work as stable aliases for compatibility
- the next extraction can move from structure into behavior by adding shared operation journaling, event publication, and faster wait/poll paths on top of the registry

Next:

- Step A4: add structured operation journaling and event publication so async and long-running capabilities can be observed without per-tool waiting logic

## 2026-03-16 - Step A4 - Operation Journal and Lifecycle Events

Status:

- completed

Completed:

- added [`OperationJournal`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/OperationJournal.cs) and [`OperationEventRecord`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/OperationJournal.cs) in the shared core project to keep recent operation snapshots, publish lifecycle events, and retain a bounded in-memory history for diagnostics
- updated [`CapabilityRegistry`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityRegistry.cs) so the registry now owns operation ids, records started and completed states centrally, normalizes handler envelopes, and captures provider failures into the journal instead of leaving lifecycle tracking to individual tools
- updated [`OperationRunner`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/OperationExecution.cs) and [`BuiltInCapabilityModuleProvider`](/Users/ap/Projects/RimBridgeServer/Source/BuiltInCapabilityModuleProvider.cs) so built-in handlers reuse registry-issued operation ids and timestamps rather than generating disconnected envelopes
- exposed the new diagnostics surface through [`DiagnosticsCapabilityModule`](/Users/ap/Projects/RimBridgeServer/Source/DiagnosticsCapabilityModule.cs), [`RimBridgeCapabilities`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeCapabilities.cs), and [`RimBridgeTools`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) with `rimbridge/get_operation`, `rimbridge/list_operations`, and `rimbridge/list_operation_events`
- marked built-in descriptors as event-emitting and kept the new diagnostics reads on the fast immediate execution path so journal inspection does not wait on the RimWorld main thread
- added focused tests in [`OperationRunnerTests`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/OperationRunnerTests.cs), [`CapabilityRegistryTests`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityRegistryTests.cs), and [`OperationJournalTests`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/OperationJournalTests.cs) covering stable operation identity, registry-to-journal recording, event publication, and result-free journal snapshots

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln`

Notes:

- this step does not introduce long-running queued execution yet, but it establishes the operation identity and lifecycle history needed for that next layer
- the diagnostics readouts intentionally avoid using the reserved top-level legacy response key `operation`, because [`LegacyToolExecution`](/Users/ap/Projects/RimBridgeServer/Source/LegacyToolExecution.cs) already injects the current invocation envelope there for backwards compatibility
- operation journaling is bounded in memory and currently scoped to bridge process lifetime, which keeps risk low while still giving the AI immediate visibility into recent failures and timings

Next:

- Step A5: add explicit wait conditions and synchronous fast-path helpers so long-event and frame-bound capabilities can expose lower-latency polling and blocking behavior through the shared execution kernel

## 2026-03-16 - Step A5 - Explicit Wait Conditions and Live Smoke Baseline

Status:

- completed

Completed:

- added shared polling primitives in [`ConditionWaiter.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/ConditionWaiter.cs) for bounded condition waits with timeout, poll interval, elapsed time, attempt count, and last-state snapshots
- added host-side RimWorld wait helpers in [`RimWorldWaits.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldWaits.cs) that probe game state safely through [`MainThreadDispatcher.cs`](/Users/ap/Projects/RimBridgeServer/Source/MainThreadDispatcher.cs) instead of relying on blind sleeps from callers
- extended [`DiagnosticsCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/DiagnosticsCapabilityModule.cs) and [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) with `rimbridge/get_bridge_status`, `rimbridge/wait_for_operation`, `rimbridge/wait_for_game_loaded`, and `rimbridge/wait_for_long_event_idle`
- marked the new wait and status tools as immediate/background-safe in [`BuiltInCapabilityModuleProvider.cs`](/Users/ap/Projects/RimBridgeServer/Source/BuiltInCapabilityModuleProvider.cs) so the outer execution runner does not marshal the whole wait loop onto the RimWorld main thread
- added focused unit coverage in [`ConditionWaiterTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/ConditionWaiterTests.cs) for successful polling and timeout behavior
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) so the new bridge diagnostics and wait commands are visible as part of the supported automation surface

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln`
- live GABS smoke against a fresh RimWorld instance:
- `rimbridge/get_bridge_status`
- `rimbridge/wait_for_long_event_idle`
- `rimworld/start_debug_game`
- `rimbridge/wait_for_game_loaded`
- `rimbridge/wait_for_operation`

Notes:

- the live smoke replaced a manual sleep with `rimbridge/wait_for_game_loaded`, which reported a ready playable state after about 3.2 seconds in the current environment
- this is the first real RimWorld-instance verification step recorded in the project and it establishes the shape for a future reproducible smoke harness
- MCP-style push notifications are still host-dependent, so the wait and journal path remains the correctness baseline even if event push is added next

Next:

- Step A6: add structured event and log publication on top of the journal so hosts that support unsolicited notifications can receive warnings, errors, and operation progress without polling

## 2026-03-16 - Step A6 - Event and Log Publication with Cursor-Based Test Windows

Status:

- completed

Completed:

- added a bounded [`LogJournal`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/LogJournal.cs) in the shared core project for captured RimWorld and bridge logs, including level filtering, sequence cursors, and repeated-message collapsing into a single row with `RepeatCount`
- extended [`OperationJournal`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/OperationJournal.cs) with event sequence numbers so test automation can fetch deltas after a precise cursor instead of diffing by time or relying on sleeps
- added host-side Unity log capture in [`RimBridgeLogs.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeLogs.cs) so live log messages are recorded without polling `Player.log`
- added [`RimBridgeEventRelay.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeEventRelay.cs) on top of `Lib.GAB` event channels, publishing filtered `rimbridge.operation` and `rimbridge.log` events while intentionally suppressing noisy diagnostics and low-severity log chatter
- updated [`RimBridgeCapabilities.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeCapabilities.cs), [`Mod.cs`](/Users/ap/Projects/RimBridgeServer/Source/Mod.cs), [`DiagnosticsCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/DiagnosticsCapabilityModule.cs), [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs), and [`RimWorldWaits.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldWaits.cs) so the diagnostics surface now exposes `rimbridge/list_logs`, returns `latestLogSequence` and `latestOperationEventSequence`, and supports `afterSequence` windows for both logs and operation events
- kept the delta readers opinionated for test use by excluding diagnostic bridge operations from `rimbridge/list_operation_events` unless explicitly requested with `includeDiagnostics=true`
- added focused unit coverage in [`LogJournalTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/LogJournalTests.cs) and expanded [`OperationJournalTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/OperationJournalTests.cs) to cover repeated-log collapsing, level and sequence filtering, and event cursor filtering
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) with the new log/event tooling, event channels, and the cursor-based before/after test pattern

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln`
- live GABS smoke against a fresh RimWorld instance:
- `rimbridge/get_bridge_status`
- `rimbridge/list_logs`
- `rimworld/start_debug_game`
- `rimbridge/list_operation_events` with `afterSequence`

Notes:

- unsolicited push is still host-dependent at the MCP layer, so the journals and cursor-based delta reads remain the correctness path while event channels provide faster feedback when the host surfaces them
- log push is deliberately conservative: only warning/error/fatal entries are emitted, and repeated identical rows are collapsed before publication thresholds are crossed
- the cursor-based design gives tests an explicit “start watching now” point without introducing server-side capture sessions or global mutable logging modes yet

Next:

- Step A7: add explicit test-harness helpers and scripts that drive these wait/log/event windows against real RimWorld scenarios through GABS so live smoke cases become reproducible commands in the repo

## 2026-03-16 - Step A7 - Reproducible Live Smoke Harness

Status:

- completed

Completed:

- added a dedicated live smoke runner project at [`Tests/RimBridgeServer.LiveSmoke`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke) that speaks MCP over stdio to a local `gabs server stdio` process instead of relying on one-off manual tool sequences
- implemented a baseline `debug-game-load` scenario in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) and [`SmokeHarness.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeHarness.cs) that checks game status, starts RimWorld when needed, connects through GABS, waits for idle, snapshots bridge cursors, starts RimWorld's debug colony, waits for playable state, verifies colonists, and then captures the resulting log/event window
- kept the console summary intentionally terse while writing the full structured run report to `artifacts/live-smoke/<timestamp>_<scenario>.json`, which gives us useful detail without flooding the interactive context
- added the developer wrapper [`scripts/live-smoke.sh`](/Users/ap/Projects/RimBridgeServer/scripts/live-smoke.sh) so the smoke flow is now a named repo command instead of a handwritten terminal sequence
- added the new live smoke project to [`RimBridgeServer.sln`](/Users/ap/Projects/RimBridgeServer/RimBridgeServer.sln) and documented the workflow in [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md)

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --list-scenarios`
- `scripts/live-smoke.sh --scenario debug-game-load --game-id rimworld --stop-after`

Notes:

- the harness now provides a reusable correctness pattern for real-instance verification: explicit waits plus cursor snapshots before the action, then bounded log/event collection after the action
- the current summary intentionally highlights only notable warnings and errors by default, while the JSON artifact retains the full captured data for deeper debugging
- `--stop-after` only stops the RimWorld instance when the harness started it in this run, which keeps the workflow safe around developer-managed sessions

Next:

- Step A8: extract the live smoke scenario and reporting contracts into reusable testing primitives so we can add more real-instance cases without duplicating GABS, wait, and cursor-window plumbing

## 2026-03-16 - Step A8 - Reusable Live Smoke Primitives and Second Scenario

Status:

- completed

Completed:

- split the live smoke runner into reusable pieces under [`Tests/RimBridgeServer.LiveSmoke`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke): [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs), [`SmokeScenarioContext.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioContext.cs), [`SmokeObservationWindow.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeObservationWindow.cs), [`McpStdioClient.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/McpStdioClient.cs), [`JsonNodeHelpers.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/JsonNodeHelpers.cs), and [`SmokeReports.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeReports.cs)
- moved scenario selection behind a small registry so the harness can enumerate scenario names and descriptions cleanly instead of hard-coding a single switch branch in one file
- added a reusable observation-window primitive that snapshots `latestLogSequence` and `latestOperationEventSequence`, carries per-scenario filters, and then collects only the bounded log/event delta after the action block under test
- added a shared playable-game precondition helper so scenarios that need a loaded colony can create one safely without duplicating GABS, wait, and operation-plumbing code
- added a second real-instance scenario, `selection-roundtrip`, which exercises `rimworld/list_colonists`, `rimworld/select_pawn`, `rimworld/jump_camera_to_pawn`, `rimworld/get_camera_state`, and `rimworld/clear_selection` on a real colony while capturing a precise operation/log window
- added focused unit coverage in [`RimBridgeServer.LiveSmoke.Tests`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests) for the scenario catalog and JSON/result helpers so the new reusable layer has fast feedback outside live RimWorld runs
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) with the second scenario and the reusable observation-window workflow

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --list-scenarios`
- `scripts/live-smoke.sh --scenario debug-game-load --game-id rimworld --stop-after`
- `scripts/live-smoke.sh --scenario selection-roundtrip --game-id rimworld --stop-after`

Notes:

- the harness now has a stable internal testing API: start a scenario, satisfy preconditions, begin a bounded observation window, run the action block, then collect only the resulting deltas
- this is the right place to add more live UX and integration scenarios because the GABS session, wait tools, cursor plumbing, and report writing are now shared instead of copied
- input work should be designed around in-process or window-path injection rather than foreground-dependent desktop automation, because future mouse and keyboard tests need to work even when RimWorld is backgrounded

Next:

- Step A9: add the first save/load and screenshot-oriented live scenarios on top of the reusable harness so we start covering longer-running lifecycle and UX flows with the same observation model

## 2026-03-16 - Step A4.1 - Lib.GAB NuGet Adoption

Status:

- completed

Completed:

- updated [`RimBridgeServer.csproj`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.csproj) to use `PackageReference Include="Lib.GAB" Version="0.1.0"` instead of file-based references to vendored bridge assemblies
- removed the direct project references to `lib/Lib.GAB.dll` and `lib/Gabp.Runtime.dll` so the host now restores the bridge runtime from NuGet and relies on the package dependency graph for `Gabp.Runtime`
- deleted the now-stale tracked binaries [`lib/Lib.GAB.dll`](/Users/ap/Projects/RimBridgeServer/lib/Lib.GAB.dll) and [`lib/Gabp.Runtime.dll`](/Users/ap/Projects/RimBridgeServer/lib/Gabp.Runtime.dll) to avoid keeping two conflicting runtime sources in the repository
- verified that the built mod output in [`1.6/Assemblies`](/Users/ap/Projects/RimBridgeServer/1.6/Assemblies) still contains `RimBridgeServer.dll`, `Lib.GAB.dll`, and `Gabp.Runtime.dll`

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln`
- `find 1.6/Assemblies -maxdepth 1 -type f \( -name 'Lib.GAB.dll' -o -name 'Gabp.Runtime.dll' -o -name 'RimBridgeServer.dll' \) -print | sort`

Notes:

- this keeps the project aligned with the current upstream `Lib.GAB` package instead of a manually vendored copy, which reduces drift and makes future updates lower risk
- `CopyLocalLockFileAssemblies=true` in the host project remains important because it ensures the restored package assemblies are copied into the RimWorld mod output directory

Next:

- Step A5: add explicit wait conditions and synchronous fast-path helpers so long-event and frame-bound capabilities can expose lower-latency polling and blocking behavior through the shared execution kernel
