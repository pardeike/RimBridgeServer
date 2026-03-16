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

## 2026-03-16 - Step A9 - Save/Load and Screenshot Live Scenarios

Status:

- completed

Completed:

- extended [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) with two additional real-instance scenarios: `save-load-roundtrip` and `screenshot-capture`
- added the first lifecycle-focused live case, `save-load-roundtrip`, which uses the shared playable-game precondition, writes a stable test save, verifies that it appears in `rimworld/list_saves`, reloads it through `rimworld/load_game`, waits for a playable state again, and confirms the colony still exposes colonists on the current map
- added the first screenshot-focused live case, `screenshot-capture`, which positions the camera on a real colonist, captures a screenshot with a run-specific file name, validates the reported path and file size, and records the resulting operation/log window
- reused the shared observation-window and scenario-context primitives so both new cases capture bounded deltas rather than broad journal snapshots
- expanded [`SmokeScenarioCatalogTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/SmokeScenarioCatalogTests.cs) so the live-smoke unit layer now guards the full scenario matrix and default-scenario contract
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) with the new scenario descriptions and the growing live-smoke matrix

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --list-scenarios`
- `scripts/live-smoke.sh --scenario save-load-roundtrip --game-id rimworld --stop-after`
- `scripts/live-smoke.sh --scenario screenshot-capture --game-id rimworld --stop-after`

Notes:

- the save/load path is intentionally tested as a full roundtrip instead of only asserting the save file exists, because the reload wait and colony verification are where async lifecycle failures tend to surface
- the screenshot case currently validates the file artifact and captures the surrounding journal window; later UX work can extend this toward clipped captures and visual assertions without changing the harness contract
- I attempted to use the RimWorld decompiler for these seams first, but the current MCP decompiler instance failed on assembly loading with `Cannot access a disposed object` for `AssemblyContextManager`, so this increment relied on the existing verified runtime surface instead

Next:

- Step A10: start adding foreground-independent input-oriented live scenarios and the first internal abstractions for in-process mouse/keyboard injection so future UX tests are not tied to OS focus state

## 2026-03-16 - Step A10 - Background-Safe UI Input Foundations

Status:

- completed

Completed:

- added [`RimWorldInput.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldInput.cs) as the first dedicated in-process input abstraction, centered on semantic UI actions and window-stack inspection instead of foreground-dependent desktop automation
- added [`InputCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/InputCapabilityModule.cs) and registered it in [`RimBridgeCapabilities.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeCapabilities.cs), exposing the first background-safe input tools through the normal capability registry
- extended [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) with `rimworld/get_ui_state`, `rimworld/press_accept`, `rimworld/press_cancel`, and `rimworld/close_window`
- implemented `rimworld/get_ui_state` as a structured snapshot of the current `WindowStack`, including focused/top window information, input-related flags, and the live list of open windows
- implemented `rimworld/press_accept` / `rimworld/press_cancel` through `WindowStack.Notify_PressedAccept()` / `Notify_PressedCancel()` and added `rimworld/close_window` as a typed window-stack fallback for cases where accept/cancel are not the correct semantic control surface
- extended [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) with a new `context-menu-cancel-roundtrip` live scenario that normalizes away stray dialog windows, opens a real float menu, closes it through semantic cancel input, and records the exact resulting log/event window
- updated [`SmokeScenarioCatalogTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/SmokeScenarioCatalogTests.cs) and [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) to cover the expanded scenario matrix and document the new input-oriented tools

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --list-scenarios`
- `scripts/live-smoke.sh --scenario context-menu-cancel-roundtrip --game-id rimworld --stop-after`

Notes:

- this is intentionally the semantic half of the input stack first: it proves that useful keyboard-like flows can already be automated safely while RimWorld is backgrounded, without overcommitting to a generic physical input design too early
- the structured `get_ui_state` snapshot gives later UX and debug-action work a stable discovery surface for active windows and input blockers, which should reduce trial-and-error waits in future scenarios
- `close_window` is intentionally included beside `press_accept` and `press_cancel` because development environments frequently keep debug windows open, and automated UI flows need a deterministic way to normalize that state before testing higher-level interactions

Next:

- Step A11: build the first physical-targeting seam on top of this semantic foundation, likely starting with screenshot-linked target metadata and a narrowly scoped background-safe click path for known RimWorld UI surfaces

## 2026-03-16 - Step A11 - Screen Target Metadata Foundations

Status:

- completed

Completed:

- added [`RimWorldTargeting.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldTargeting.cs) as the first screen-target aggregation seam, combining window-stack state, camera state, selection, and active context-menu geometry into one structured payload
- added [`FloatMenuTargetLayoutCalculator.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/FloatMenuTargetLayoutCalculator.cs) and [`FloatMenuTargetLayoutCalculatorTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/FloatMenuTargetLayoutCalculatorTests.cs) so float-menu option rects are derived in a pure, unit-tested core component instead of being mixed into host-side UI code
- extended [`ViewCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ViewCapabilityModule.cs) with `GetScreenTargets()` and updated `TakeScreenshot(...)` so screenshots can attach the same target snapshot that a caller can fetch independently
- extended [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) with `rimworld/get_screen_targets` and the `includeTargets` parameter on `rimworld/take_screenshot`
- refined [`RimWorldInput.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldInput.cs) with a typed UI-rect snapshot so screen-target and input code share the same rect model
- extended the live smoke harness in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) so `context-menu-cancel-roundtrip` validates real float-menu option rects and `screenshot-capture` validates that screenshot responses include target metadata
- hardened [`SmokeScenarioContext.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioContext.cs) to retry `start_debug_game` when RimWorld reports the transient "busy with another long event" race immediately after an idle wait
- updated [`JsonNodeHelpers.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/JsonNodeHelpers.cs), [`JsonNodeHelpersTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/JsonNodeHelpersTests.cs), and [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) to document and verify the new payload shape

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --scenario context-menu-cancel-roundtrip --game-id rimworld --stop-after`
- `scripts/live-smoke.sh --scenario screenshot-capture --game-id rimworld --stop-after`

Notes:

- the new targeting surface is intentionally descriptive before it is interactive: it gives later click and clipping work a deterministic geometry API without committing to physical input semantics too early
- `rimworld/take_screenshot` now captures the target snapshot before the screenshot write is awaited, so the metadata reflects the UI state that produced the image instead of a later state
- the live harness now validates both a real float-menu geometry path and a screenshot-attached target snapshot, which reduces the risk of silently drifting payload contracts as the UI tooling expands

Next:

- Step A12: build the first narrowly scoped background-safe click path on top of `rimworld/get_screen_targets`, starting with known float-menu option and window targets before expanding toward broader physical input coverage

## 2026-03-16 - Step A12 - Background-Safe Screen Target Clicks

Status:

- completed

Completed:

- added [`ScreenTargetIds.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/ScreenTargetIds.cs) and [`ScreenTargetIdsTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/ScreenTargetIdsTests.cs) as the shared target-id contract for screen-target payloads and click dispatch
- extended [`RimWorldTargeting.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldTargeting.cs) so screen-target payloads now expose actionable ids: `dismissTargetId` for dismissible windows and context menus, plus `targetId` for context-menu options
- added [`RimWorldContextMenuActions.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldContextMenuActions.cs) to centralize active-menu validation and option execution, then reused it from [`ContextMenuCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ContextMenuCapabilityModule.cs) and the new click path instead of duplicating menu-resolution logic
- extended [`RimWorldInput.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldInput.cs), [`InputCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/InputCapabilityModule.cs), and [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) with `rimworld/click_screen_target`, which semantically dispatches known target ids without depending on OS focus
- fixed the context-menu execution seam so clicking a context-menu option now removes the clicked float menu after invoking the option action, matching real click behavior more closely for automation flows
- added a new real-instance live scenario, `screen-target-click-roundtrip`, in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) and updated [`SmokeScenarioCatalogTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/SmokeScenarioCatalogTests.cs) so the harness now validates both dismiss-target clicks and option-target clicks against a real RimWorld colony
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) and [`docs/architecture.md`](/Users/ap/Projects/RimBridgeServer/docs/architecture.md) to document the new tool surface and advance the roadmap

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --list-scenarios`
- `scripts/live-smoke.sh --scenario screen-target-click-roundtrip --game-id rimworld --stop-after`

Notes:

- this is intentionally still semantic targeting, not unrestricted physical mouse injection; it gives AI agents a background-safe click primitive while keeping the execution surface narrow and testable
- the target-id contract now sits in `Core`, which means future screenshot clipping, scripting, and extension packages can refer to the same identifiers without reaching into host-only code
- the live proof caught a real behavioral gap: invoking `FloatMenuOption.Chosen(...)` alone did not fully emulate click behavior for automation because the float menu could remain open, so the shared execution helper now closes the clicked menu explicitly

Next:

- Step A13: add target-relative screenshot clipping on top of `rimworld/get_screen_targets` and `rimworld/click_screen_target` so live tests can make focused visual assertions without relying on full-frame screenshots

## 2026-03-16 - Step A15 - Architect and God-Mode Designator Service

Status:

- completed

Completed:

- added the shared Architect id contract in [`ArchitectDesignatorIds.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/ArchitectDesignatorIds.cs) with focused coverage in [`ArchitectDesignatorIdsTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/ArchitectDesignatorIdsTests.cs) so categories and designators now have stable ids independent of UI text formatting
- added [`ArchitectCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ArchitectCapabilityModule.cs), registered it in [`RimBridgeCapabilities.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeCapabilities.cs), and exposed the new aliases in [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs)
- added [`RimWorldArchitect.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldArchitect.cs) as the new runtime seam over `DesignationCategoryDef`, `DesignatorManager`, `DebugSettings.godMode`, `Blueprint_Build`, and `Frame`
- implemented `rimworld/get_designator_state`, `rimworld/set_god_mode`, `rimworld/list_architect_categories`, `rimworld/list_architect_designators`, `rimworld/select_architect_designator`, `rimworld/apply_architect_designator`, and `rimworld/get_cell_info`
- kept the Architect surface map-context aware so entry-scene probes return clean state without touching `Find.DesignatorManager`, while map-only operations fail with direct messages instead of leaking RimWorld exceptions
- extended [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) and [`SmokeScenarioCatalogTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/SmokeScenarioCatalogTests.cs) with the new real-instance `architect-wall-placement` scenario
- made that scenario discover the `Structure` category and `Wall` designator by stable ids, prove blueprint placement with god mode off, prove direct structure placement with god mode on, verify both cells through `rimworld/get_cell_info`, and restore the original god-mode state afterward
- extended the human-verification flow so the architect scenario now exports Desktop screenshots and same-name `.txt` notes for both the blueprint and direct-build wall states
- fixed a real harness bug in the architect cell-search probe by treating dry-run rejections as expected search results when the underlying operation completed cleanly

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --scenario architect-wall-placement --game-id rimworld --stop-after`
- `scripts/live-smoke.sh --scenario architect-wall-placement --game-id rimworld --human-verify --stop-after`

Notes:

- the first live probe surfaced two legitimate edge cases that were fixed in this increment: `get_designator_state` originally enumerated Architect categories from the main menu, and selection-state reads originally touched `Find.DesignatorManager` outside map UI context
- the initial live scenario failure was in the harness, not the bridge surface: dry-run placement rejections are normal search results and should not be treated like capability failures as long as the operation envelope itself reports success
- the current architect payload already gives the AI enough structure to find the right category and build command without label heuristics, because build designators expose `buildableDefName`, `stuffDefName`, and stable ids
- the live verification proved the core user-facing distinction we care about right now: `rimworld/apply_architect_designator` creates a `Blueprint_Build` for `Wall` when god mode is off and a solid `Wall` building when god mode is on

Next:

- Step A16: widen the Architect/designator surface into dropdown-heavy categories, zone and area designators, and richer drag semantics before returning to the batch/script layer

## 2026-03-16 - Step A16 - Dropdown, Zone, Area, and Drag Architect Coverage

Status:

- completed

Completed:

- extended [`RimWorldArchitect.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldArchitect.cs) so designator descriptors now report drag and targeting metadata such as `applicationKind`, `supportsRectangleApplication`, `dragDrawMeasurements`, `drawStyleCategoryDefName`, `zoneTypeName`, and current selected zone/allowed-area context where RimWorld exposes it
- added inspection tools for zone and area results in [`ArchitectCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ArchitectCapabilityModule.cs) and [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs): `rimworld/list_zones`, `rimworld/list_areas`, and richer `rimworld/get_cell_info` payloads with `zone` plus `areas`
- kept the execution model unchanged by continuing to drive all Architect mutations through the existing `rimworld/apply_architect_designator` path rather than introducing a second drag-specific code path
- extended the live smoke catalog in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) and [`SmokeScenarioCatalogTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/SmokeScenarioCatalogTests.cs) with `architect-floor-dropdown` and `architect-zone-area-drag`
- made `architect-floor-dropdown` prove a real dropdown child designator can be selected semantically, keeps the parent-child selection relationship intact, and directly paints a 2x2 floor patch through god-mode placement
- made `architect-zone-area-drag` prove rectangle drag semantics for a stockpile zone and a home area, with verification through both the new list tools and cell-level inspection
- extended [`JsonNodeHelpers.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/JsonNodeHelpers.cs) and [`scripts/human-verify.sh`](/Users/ap/Projects/RimBridgeServer/scripts/human-verify.sh) so the curated manual-review flow now covers the new Architect cases too

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --scenario architect-floor-dropdown --game-id rimworld --human-verify --stop-after`
- `scripts/live-smoke.sh --scenario architect-zone-area-drag --game-id rimworld --human-verify --stop-after`

Notes:

- the widened Architect payload now distinguishes builds, zones, areas, and generic designations through `applicationKind`, which gives the AI a better routing signal before it executes a tool
- the zone and area verification surface is intentionally read-only; it exists so automation can assert results without adding stateful zone-edit APIs before they are needed
- live validation showed the current generalized rectangle path is already sufficient for stockpile zones and home areas, so no separate drag executor was necessary in this increment

Next:

- Step A17: make stateful Architect context deterministic for the remaining high-value cases, especially allowed-area selection/creation, explicit existing-zone targeting for expand/shrink flows, and richer cleanup or removal semantics before returning to the batch/script layer

## 2026-03-16 - Step A14 - Debug Menu Graph, Output Effects, and Settings Toggles

Status:

- completed

Completed:

- added a dedicated debug-actions module in [`DebugActionsCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/DebugActionsCapabilityModule.cs) and the shared runtime helper in [`RimWorldDebugActions.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldDebugActions.cs) so RimBridgeServer can enumerate and execute the same internal node graph that powers RimWorld's debug dialog
- extended [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) with `rimworld/list_debug_action_roots`, `rimworld/list_debug_action_children`, `rimworld/get_debug_action`, `rimworld/execute_debug_action`, and `rimworld/set_debug_setting`
- kept the internal seam generic by path, but surfaced UI-aligned tab metadata for `Actions/tools`, `Settings`, and `Output` so clients can stay close to the in-game dialog while still using stable paths like `Outputs\\Tick Rates`
- added shared execution policy coverage in [`DebugActionExecutionPolicy.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/DebugActionExecutionPolicy.cs) and [`DebugActionExecutionPolicyTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/DebugActionExecutionPolicyTests.cs)
- made debug-action execution report side effects instead of only success/failure by capturing log deltas and opened/closed RimWorld windows around the action call
- added deterministic settings semantics on top of the same graph: settings nodes now expose current state through `get_debug_action`, and `rimworld/set_debug_setting` drives them to an explicit target value instead of relying on blind toggle calls
- added a new live-smoke scenario, `debug-action-discovery`, in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) that discovers roots, executes a low-side-effect output action, and flips then restores a safe debug setting
- improved the live harness in [`SmokeHarness.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeHarness.cs) with bounded `games.connect` retries after game start so fresh launches do not fail immediately on the first GABP timing race
- documented the new debug-menu surface in [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md)

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- manual real-instance validation through the ambient GABS MCP session after restarting RimWorld with the rebuilt mod:
  - `rimworld/list_debug_action_roots`
  - `rimbridge/wait_for_game_loaded`
  - `rimworld/execute_debug_action` with `Outputs\\Tick Rates`
  - `rimworld/set_debug_setting` with `Settings\\Show Architect Menu Order` to `true`, then back to `false`

Notes:

- the real-instance validation confirmed the user-facing debug dialog mapping: the bridge now reports `tabId` / `tabTitle` for `actions`, `settings`, and `output`, and output execution correctly surfaced side effects by reporting a newly opened `LudeonTK.Window_DebugTable`
- `rimworld/set_debug_setting` was validated against `Settings\\Show Architect Menu Order`, which changed deterministically and restored cleanly
- the repo-owned `scripts/live-smoke.sh --scenario debug-action-discovery` path is still blocked under the latest standalone `gabs server stdio` launch flow in this environment because the isolated harness tried to `games.connect` on port `49152` while `Player.log` reported `GABP server connected to GABS on port 49153`; that points to an external GABS launch/connect mismatch rather than a RimBridgeServer feature failure

Next:

- Step A15: add explicit god-mode control plus Architect/designator discovery and application on top of `DesignationCategoryDef`, `DesignatorManager`, and `DebugSettings.godMode`

## 2026-03-16 - Step A13 - Target-Relative Screenshot Clipping

Status:

- completed

Completed:

- added shared clipping math in [`ScreenshotClipMath.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/ScreenshotClipMath.cs) with focused coverage in [`ScreenshotClipMathTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/ScreenshotClipMathTests.cs) so target rects can be scaled into screenshot pixel space consistently across normal and high-DPI captures
- extended [`RimWorldTargeting.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldTargeting.cs) so existing `window`, `window-dismiss`, and `context-menu-option` target ids can be resolved back into clip-capable UI rects without introducing a second targeting model
- extended [`ViewCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ViewCapabilityModule.cs) and [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) so `rimworld/take_screenshot` accepts `clipTargetId` and `clipPadding`, preserves the full-frame `sourcePath`, and writes a clipped artifact with `clipRect`, `clipTargetId`, `clipTargetKind`, and `clipTargetLabel`
- added a real-instance `screen-target-clip` live scenario in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) and updated [`SmokeScenarioCatalogTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/SmokeScenarioCatalogTests.cs) so the harness now validates clipped screenshot dimensions against a live target rect from RimWorld
- documented the clipping surface in [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md)

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --scenario screen-target-clip --game-id rimworld --human-verify --stop-after`

Notes:

- clipping uses the target rect captured at screenshot request time rather than re-reading later UI state, which keeps the cropped artifact aligned with the frame that was actually written
- the first supported clipping targets are the same ones that already have stable screen-space geometry today: windows, window dismiss targets, and context-menu options

Next:

- Step A14: add generic debug-action discovery and path execution, then prioritize god-mode designator selection/application before returning to the bulk script layer

## 2026-03-16 - Step A12.2 - Automation-Ready Load Waits

Status:

- completed

Completed:

- added a shared [`AutomationReadiness`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/AutomationReadiness.cs) evaluator in `Core` plus focused coverage in [`AutomationReadinessTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/AutomationReadinessTests.cs) so the bridge can distinguish between merely playable state and truly automation-ready state
- extended [`RimWorldState.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldState.cs) so bridge state snapshots now include `paused`, `screenFading`, `fadeOverlayAlpha`, `screenFadeClear`, `playable`, and `automationReady`
- updated [`RimWorldWaits.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldWaits.cs), [`DiagnosticsCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/DiagnosticsCapabilityModule.cs), and [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) so `rimbridge/wait_for_game_loaded` can wait for the post-load screen fade to complete and optionally pause the game before returning success
- updated [`ViewCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ViewCapabilityModule.cs) and [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) so automated `rimworld/take_screenshot` calls suppress RimWorld's screenshot toast by default only during the tool-driven capture
- updated the live harness in [`SmokeScenarioContext.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioContext.cs) and [`SmokeHarness.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeHarness.cs) so all playable-game preconditions now wait for automation-ready state and pause the game before continuing
- documented the tighter ready-state semantics in [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md)

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --scenario save-load-roundtrip --game-id rimworld --human-verify --stop-after`
- `scripts/live-smoke.sh --scenario screenshot-capture --game-id rimworld --human-verify --stop-after`

Notes:

- the prior wait condition could return while RimWorld was still visually fading in, which made screenshots technically correct but dimmer than a human would consider "ready"
- pausing is deliberately opt-in at the tool level but enabled by default in the repo's live harness so automated scenarios stabilize quickly without forcing mutation on every external caller

Next:

- Step A13: add target-relative screenshot clipping on top of `rimworld/get_screen_targets` and `rimworld/click_screen_target` so live tests can make focused visual assertions without relying on full-frame screenshots

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

## 2026-03-16 - Step A12.1 - Human Verification Artifacts and Probe Safety

Status:

- completed

Completed:

- extended [`CliOptions.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/CliOptions.cs) with `--human-verify` and `--human-verify-dir` so live runs can export curated evidence for manual review without changing scenario logic
- extended [`SmokeScenarioContext.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioContext.cs), [`SmokeReports.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeReports.cs), and [`SmokeHarness.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeHarness.cs) so scenarios can copy screenshots to the Desktop, write same-name `.txt` expectation notes, and include those artifact paths in the JSON report and console summary
- updated [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) so the most visually useful live scenarios now export explicit checkpoints for save/load, context-menu cancel, screen-target clicks, and the screenshot pipeline itself
- added [`scripts/human-verify.sh`](/Users/ap/Projects/RimBridgeServer/scripts/human-verify.sh) as the convenience wrapper for the current human-review scenario set
- fixed a real probe bug in [`ContextMenuCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ContextMenuCapabilityModule.cs) by avoiding `new FloatMenu(...)` when vanilla probing yields zero options; this removed RimWorld's red `Created FloatMenu with no options. Closing.` error from the live context-menu scenarios
- added focused flag coverage in [`CliOptionsTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/CliOptionsTests.cs)
- documented the new human-verification workflow in [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md)

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --scenario context-menu-cancel-roundtrip --game-id rimworld --human-verify --stop-after`
- `scripts/live-smoke.sh --scenario screen-target-click-roundtrip --game-id rimworld --human-verify --stop-after`

Notes:

- the red error the user saw was legitimate signal from our own probing path, not a false positive in the report layer, so fixing it at the menu-construction seam was the right place
- the clean reruns no longer emit the empty-float-menu error; the remaining visible UI noise in the cancel scenario is a warning-only `Event.Use() should not be called for events of type repaint` log from the RimWorld UI layer
- exporting screenshots plus expectation text to the Desktop gives a fast sanity-check path for live automation without making the standard JSON reports heavier or harder for models to consume

Next:

- Step A13: add target-relative screenshot clipping on top of `rimworld/get_screen_targets` and `rimworld/click_screen_target` so live tests can make focused visual assertions without relying on full-frame screenshots

## 2026-03-16 - Step A17 - Deterministic Stateful Architect Targeting

Status:

- completed

Completed:

- extended [`RimWorldArchitect.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldArchitect.cs), [`ArchitectCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ArchitectCapabilityModule.cs), and [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) with deterministic state helpers for mutable Architect flows: `rimworld/create_allowed_area`, `rimworld/select_allowed_area`, `rimworld/set_zone_target`, `rimworld/clear_area`, `rimworld/delete_area`, and `rimworld/delete_zone`
- extended `rimworld/get_designator_state` so it now surfaces the globally selected allowed area in addition to the selected designator/container state
- added low-friction id resolution for zones and areas so cleanup and selection helpers accept the canonical ids returned by `rimworld/list_zones` and `rimworld/list_areas` while still allowing unambiguous label fallback
- fixed a real execution bug in [`RimWorldArchitect.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldArchitect.cs): live testing showed that vanilla stockpile placement still created a fresh zone on empty cells even when `SelectedZone` was set, so explicit zone targets now apply directly to the chosen existing zone instance to make expansion deterministic
- extended the live smoke catalog in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) and [`SmokeScenarioCatalogTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/SmokeScenarioCatalogTests.cs) with the new real-instance `architect-stateful-targeting` scenario
- made `architect-stateful-targeting` prove the full stateful flow end-to-end: create a custom allowed area, select it, apply `Expand allowed area`, create a stockpile zone, pin the stockpile designator to that zone, expand the same zone into a second rectangle without increasing the zone count, then tear everything back down with the new cleanup helpers
- updated [`scripts/human-verify.sh`](/Users/ap/Projects/RimBridgeServer/scripts/human-verify.sh) so the curated manual-review flow now includes the new stateful Architect scenario
- documented the new helper tools and live scenario in [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md)

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --scenario architect-stateful-targeting --game-id rimworld --human-verify --stop-after`

Notes:

- the first live attempt failed in exactly the way we wanted the harness to catch: `rimworld/set_zone_target` reflected the requested zone id back, but RimWorld still created a second stockpile zone during placement; the final implementation step fixed the execution seam instead of relaxing the test
- the live proof artifacts for this step are [`rimbridge_verify_20260316_201610_architect-stateful-targeting_architect_allowed_area.png`](/Users/ap/Desktop/rimbridge_verify_20260316_201610_architect-stateful-targeting_architect_allowed_area.png) and [`rimbridge_verify_20260316_201610_architect-stateful-targeting_architect_existing_zone_expand.png`](/Users/ap/Desktop/rimbridge_verify_20260316_201610_architect-stateful-targeting_architect_existing_zone_expand.png), each with a same-name `.txt` expectation note on the Desktop
- using the decompiler against the publicized RimWorld reference was useful here even though `Designator_ZoneAdd` itself is extern-heavy in the reference assembly; the type graph still confirmed the relevant public seams such as `SelectedZone` and `Verse.Zone.AddCell`

Next:

- Step A18: return to the bulk/script layer now that debug actions, designator discovery, and deterministic Architect state control exist for meaningful scenario construction

## 2026-03-16 - Step A18 - First Registry-Backed Script Runner Slice

Status:

- completed

Completed:

- added shared script contracts in [`CapabilityScriptContracts.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Contracts/CapabilityScriptContracts.cs) for script definitions, ordered steps, per-step reports, and full script reports so the batch layer has a stable transport shape outside the host assembly
- added the pure execution engine in [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs), which executes ordinary capability calls in order through the registry, records child operation metadata, supports `continueOnError`, and rejects nested `rimbridge/run_script` recursion
- added first-step unit coverage in [`CapabilityScriptRunnerTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptRunnerTests.cs) and extended [`ContractTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Contracts.Tests/ContractTests.cs) so the script layer is covered for success, halt-on-failure, continue-on-error, recursion rejection, and contract serialization
- added [`ScriptingCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs), registered it in [`RimBridgeCapabilities.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeCapabilities.cs), exposed the alias in [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs), and marked `rimbridge/run_script` as `Immediate` in [`BuiltInCapabilityModuleProvider.cs`](/Users/ap/Projects/RimBridgeServer/Source/BuiltInCapabilityModuleProvider.cs) so the outer script orchestration stays off the RimWorld main thread while each child step still uses its own execution policy
- kept the v1 script format intentionally small: JSON only, no custom DSL, no variable system yet, and every step is just another normal capability call with `id`, `call`, and `arguments`
- extended the live harness with the new real-instance `script-wall-sequence` scenario in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) and [`SmokeScenarioCatalogTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/SmokeScenarioCatalogTests.cs)
- made `script-wall-sequence` prove a meaningful batch setup path end-to-end: discover the wall designator through the normal Architect metadata flow, precompute two accepted cells, run one script that enables god mode, places both walls, and captures a screenshot, then verify both direct-built walls plus the script-produced screenshot artifact
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) and [`scripts/human-verify.sh`](/Users/ap/Projects/RimBridgeServer/scripts/human-verify.sh) so the new scripting surface and scenario are visible in both normal docs and the curated manual-review path

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --scenario script-wall-sequence --game-id rimworld --human-verify --stop-after`

Notes:

- the first live attempt found a real report-shape bug: the scripting module initially returned the internal typed report with `PascalCase` property names, while the rest of the bridge exposes lower-case transport fields; the final implementation fixes that projection inside [`ScriptingCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs) instead of teaching the harness a special case
- `rimbridge/run_script` is intentionally registry-backed rather than script-runtime-specific, which means future first-party and third-party extension capabilities automatically become scriptable as long as they are registered normally
- the current limitation is deliberate: later steps cannot yet reference values produced by earlier steps, so callers still need to precompute dynamic ids outside the script when a workflow depends on newly created objects

Next:

- Step A19: add generic pawn-target debug-action execution so high-value built-in developer tools such as job logging can be driven through the existing debug-action graph

## 2026-03-16 - Step A19 - Pawn-Target Debug Actions and Job-Logging Bootstrap

Status:

- completed

Completed:

- extended [`DebugActionExecutionPolicy.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/DebugActionExecutionPolicy.cs) so pawn-target debug actions are no longer treated as permanently unsupported; discovery now reports them as executable leaves with `requiredTargetKind = "pawn"` while still keeping map and world targets disabled
- extended [`RimWorldDebugActions.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimWorldDebugActions.cs), [`DebugActionsCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/DebugActionsCapabilityModule.cs), and [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) so `rimworld/execute_debug_action` accepts an optional `pawnName` and can invoke `DebugActionNode.pawnAction` directly against a resolved current-map pawn
- kept the result shape side-effect aware for targeted actions too: the execution response now includes the resolved `targetPawn`, before/after state snapshots, and the same captured log/window effects as direct debug actions
- added coverage in [`DebugActionExecutionPolicyTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/DebugActionExecutionPolicyTests.cs) for the new pawn-target assessment semantics
- added a new real-instance smoke scenario, `debug-action-pawn-target`, in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) and [`SmokeScenarioCatalogTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke.Tests/SmokeScenarioCatalogTests.cs)
- made `debug-action-pawn-target` prove the exact user-relevant seam end-to-end: discover `Actions\\T: Toggle Job Logging` and `Actions\\T: Log Job Details`, confirm discovery reports `requiredTargetKind: pawn`, execute both actions for a real colonist by `pawnName`, and verify the details action emits a captured job log row
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) so the debug-action documentation now explains targeted execution and calls out the job-logging actions explicitly

Verification:

- `dotnet build RimBridgeServer.sln`
- `dotnet test RimBridgeServer.sln --no-build`
- `scripts/live-smoke.sh --scenario debug-action-pawn-target --game-id rimworld --stop-after`

Notes:

- the live run confirmed the exact stable paths and behavior we wanted to bootstrap from: `Actions\\T: Toggle Job Logging` and `Actions\\T: Log Job Details` both resolve through the normal debug-action tree and execute successfully when `pawnName` is provided
- `T: Log Job Details` produced a captured info log for colonist `Legend` with the current `GotoWander` job and driver toil, which proves the targeted execution path and log capture work together for real dev diagnostics
- `T: Toggle Job Logging` itself currently does not expose a target-aware `on` state through the generic debug node metadata, so the bridge now treats it as an executable targeted action rather than as a deterministic boolean setting; that is acceptable for now because the goal of this step is reachability, not a full structured pawn-event API

Next:

- Step A20: add controlled step-output references to `rimbridge/run_script` so later steps can consume values produced by earlier steps without introducing conditions or full flow control yet

## 2026-03-16 - Step A20 - Controlled Step-Output References for Scripts

Status:

- completed

Completed:

- extended [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs) so script step arguments can now contain explicit reference objects of the form `{"$ref":"step_id","path":"result.someField"}`, resolved only against already executed steps
- kept the new scripting dataflow intentionally within the “simple” level: ordered steps remain the only execution model, while conditions, branching, loops, and a separate DSL are still out of scope
- made references work even when `includeStepResults = false` in the returned script report by storing internal raw step results separately from the projected response payload
- added duplicate step-id rejection in the runner because step references require unambiguous step identities inside one script
- expanded [`CapabilityScriptRunnerTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptRunnerTests.cs) to cover successful result references, reference resolution with suppressed projected results, and invalid reference-path failures
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) so the scripting section now documents the `$ref`/`path` shape and includes a concrete value-passing example

Verification:

- `dotnet test Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj --filter CapabilityScriptRunnerTests`
- `dotnet test Tests/RimBridgeServer.Contracts.Tests/RimBridgeServer.Contracts.Tests.csproj`
- `dotnet build RimBridgeServer.sln`

Notes:

- this step matches the architecture note in [`docs/architecture.md`](/Users/ap/Projects/RimBridgeServer/docs/architecture.md) that the next increment after the first script slice should add controlled step-output references rather than jumping straight to a full scripting language
- the reference root intentionally exposes both step result data and report metadata such as `operationId`, `success`, `status`, `error`, and `warnings`, but only through explicit `$ref` objects so ordinary strings remain literal
- this is still level 1 scripting: multiple steps after each other with bounded dataflow. Continue conditions and general flow control remain separate future increments.

Next:

- Step A21: add bounded level-2 continue conditions to `rimbridge/run_script` so ordered scripts can poll until a generic result condition is satisfied without adding full flow control

## 2026-03-16 - Step A21 - Script Continue Conditions

Status:

- completed

Completed:

- extended [`CapabilityScriptContracts.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Contracts/CapabilityScriptContracts.cs) with an optional `continueUntil` policy per step plus `Attempts` in step reports so scripts can express bounded polling while keeping the overall model JSON-only and step-oriented
- extended [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs) so a step can now be re-invoked until a generic condition matches or a timeout expires
- kept the condition model intentionally constrained and data-oriented: `all`, `any`, `path`, `exists`, `equals`, `notEquals`, `in`, `notIn`, numeric comparisons, `countEquals`, `allItems`, and `anyItem`
- reused the same step-reference resolver inside condition evaluation so continue checks can consume literal values or earlier-step references without introducing a second expression runtime
- added focused coverage in [`CapabilityScriptRunnerTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptRunnerTests.cs) for numeric polling, collection-shaped polling similar to grouped-colonist waits, and timeout failure behavior
- updated [`ScriptingCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs) and [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) so transport projection and user-facing docs both describe the new `continueUntil` surface

Verification:

- `dotnet test Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj --filter CapabilityScriptRunnerTests`
- `dotnet build RimBridgeServer.sln`

Notes:

- this is the level-2 scripting slice: ordered steps plus bounded continue conditions. It intentionally does not add branching, loops, or a custom DSL
- `continueUntil` is safest when attached to read or poll steps such as `list_colonists` after an earlier mutating step has already happened; the runner simply re-invokes the same registered capability until the condition is satisfied or times out
- the collection operators are intentionally enough to express practical waits like “all listed colonists are inside this rectangle and standing in combat posture” without forcing callers to write a separate helper capability for each scenario

Next:

- Step A22: add a bounded structured pawn-event journal for `job_changed`, `draft_changed`, and `mental_state_changed`, with push when supported and cursor-based pull as the correctness path

## 2026-03-16 - Step A22 - Idempotent Main-Menu Reset for In-Game Scripts

Status:

- completed

Completed:

- added [`rimworld/go_to_main_menu`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) through [`LifecycleCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/LifecycleCapabilityModule.cs) as an idempotent lifecycle capability that succeeds as a no-op when RimWorld is already at the entry scene and otherwise queues a return to the main menu
- registered the new lifecycle seam in [`BuiltInCapabilityModuleProvider.cs`](/Users/ap/Projects/RimBridgeServer/Source/BuiltInCapabilityModuleProvider.cs) as a long-event-bound capability so script steps can safely include it before `start_debug_game`
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) to document the new command and show a single `rimbridge/run_script` example that starts from a connected RimWorld session, resets to the main menu, starts a debug colony, waits for the load to finish, and captures a screenshot

Verification:

- `dotnet build RimBridgeServer.sln`
- manual in-game verification through GABS: one `rimbridge/run_script` can now start with `rimworld/go_to_main_menu`, wait for the entry scene, create a fresh debug colony, and continue with later scripted actions

Notes:

- this does not expand `rimbridge/run_script` into host-level process control; `games.start` and `games.connect` still live outside the in-game capability registry
- the new lifecycle seam moves the practical script boundary to “connected to RimWorld”, which is the right starting contract for the next scripting increments

Next:

- Step A23: add a bounded structured pawn-event journal for `job_changed`, `draft_changed`, and `mental_state_changed`, with push when supported and cursor-based pull as the correctness path

## 2026-03-16 - Design Note - Lua Front-End Proposal

Status:

- completed

Completed:

- added [`lua-frontend-design.md`](/Users/ap/Projects/RimBridgeServer/docs/lua-frontend-design.md) to capture the first concrete proposal for a human-friendly scripting layer on top of the current JSON runner
- kept the recommendation aligned with the architecture rules already in the repo: MoonSharp/Lua is the preferred language front-end, but the existing script runner remains the execution backend
- documented the minimal internal work needed before `run_lua` is practical: control-flow nodes, expression support, bounded loops, and a narrow sandboxed host API rather than direct RimWorld object exposure
- updated [`architecture.md`](/Users/ap/Projects/RimBridgeServer/docs/architecture.md) so the general “DSL later” note now points at the concrete Lua design proposal

Verification:

- document review against the current scripting implementation in [`ScriptingCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs), [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs), and [`CapabilityScriptContracts.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Contracts/CapabilityScriptContracts.cs)

Notes:

- this is intentionally a design slice, not an implementation slice; no runtime code changed yet for `run_lua`
- the recommended sequence is still: extend the internal script model first, then add Lua as a front-end over that backend

Next:

- Step A23: add the minimal internal script control-flow and expression model needed for a future `rimbridge/run_lua` frontend

## 2026-03-16 - Step A23 - Minimal Internal Script Control Flow for Future Lua Front-End

Status:

- completed

Completed:

- extended [`CapabilityScriptContracts.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Contracts/CapabilityScriptContracts.cs) so script steps can now represent internal control statements through `type`, `name`, `value`, `condition`, `body`, `elseBody`, `collection`, `itemName`, `indexName`, and `maxIterations`
- updated [`ScriptingCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs) so the `rimbridge/run_script` transport normalizes those new control-flow and expression fields recursively instead of only normalizing plain call arguments
- refactored [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs) around a shared execution state that now supports `call`, `let`, `set`, `if`, `foreach`, and bounded `while`
- kept the existing dataflow model instead of inventing a second runtime: control statements reuse the current `$ref` mechanism, add `$var` for scoped variable lookup, and support only a small arithmetic expression surface with `$add`, `$subtract`, `$multiply`, `$divide`, and `$mod`
- kept the script report focused on concrete capability executions: successful control statements do not emit ordinary report rows, while invalid control statements still surface as failed script steps
- expanded [`CapabilityScriptRunnerTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptRunnerTests.cs) to cover variable declaration plus branching, collection iteration with repeated call ids and latest-result references, bounded while loops with mutation and arithmetic, and max-iteration loop safety failures
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) to document the new control statements and expression forms and added a small bounded-loop example
- updated [`lua-frontend-design.md`](/Users/ap/Projects/RimBridgeServer/docs/lua-frontend-design.md) to note that the internal JSON-side control-flow layer is now implemented and that the remaining major step is the Lua front-end itself

Verification:

- `dotnet test Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj --filter CapabilityScriptRunnerTests`
- `dotnet build RimBridgeServer.sln`

Notes:

- this is still not `run_lua`; it is the internal scripting slice that makes a later Lua front-end practical without bypassing the shared registry-backed execution path
- `set` was added alongside `let` because bounded loops are not very useful without a minimal mutation primitive for counters and accumulators
- repeated executions of the same call statement now receive report ids like `step`, `step#2`, `step#3`, while `$ref` continues to resolve by the base step id to the latest execution of that statement

Next:

- Step A24: make `rimbridge/run_script` usable as a test-like tool call with explicit bailout, trace output, and final return values

## 2026-03-16 - Step A24 - Script Assertions, Bailout, and Trace Output

Status:

- completed

Completed:

- extended [`CapabilityScriptContracts.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Contracts/CapabilityScriptContracts.cs) so script steps can now carry a `message`, and script reports can now return top-level `error`, `output`, `result`, and `returned` state
- extended [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs) with four new control statements: `assert`, `fail`, `print`, and `return`
- made `assert` and `fail` act as explicit bailout points for scripts, including a top-level propagated script error and immediate stop semantics suitable for test-style scripts
- made `print` append structured output entries instead of polluting the per-step capability report, so scripts can leave a readable trace that comes back with the outer `rimbridge/run_script` tool result
- made `return` end the script successfully with a final structured result payload, allowing scripts to behave more like small test/program units rather than only imperative batches
- updated [`ScriptingCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs) so the outer tool projection now includes `error`, `output`, `result`, and `returned`, and failed scripts surface their failure message at the top level instead of requiring callers to dig through step details first
- expanded [`CapabilityScriptRunnerTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptRunnerTests.cs) to cover printed output, assertion bailout, explicit fail semantics even with `continueOnError = true`, and early return behavior
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) so the scripting section now documents the new test-like statements and the shape of the outer script result

Verification:

- `dotnet test Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj --filter CapabilityScriptRunnerTests`
- `dotnet build RimBridgeServer.sln`

Notes:

- this keeps the “script as one tool call” model intact: callers still invoke one capability, but they now get a much better failure boundary and human-readable trace when a script assumption is violated
- the outer tool result still uses the existing `success` projection model, which means current clients that already treat `success: false` as a tool failure continue to work without a transport redesign
- `print` is intentionally structured rather than plain text only, so future Lua support can map onto the same output surface without inventing a second logging model

Next:

- Step A25: add global script execution guards so runaway loops and oversized scripts fail predictably at the script boundary before `run_lua` broadens the surface

## 2026-03-16 - Step A25 - Global Script Execution Guards

Status:

- completed

Completed:

- enforced the already-modeled script-wide `maxDurationMs`, `maxExecutedStatements`, and `maxControlDepth` limits inside [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs) so they now apply to ordinary call steps, loop iterations, nested control bodies, and `continueUntil` retry attempts
- made global limit failures surface as explicit top-level script errors with `script.timeout`, `script.statement_limit_exceeded`, `script.max_depth_exceeded`, and `script.invalid_definition` instead of hanging or failing ambiguously later
- kept the existing local loop and poll bounds in place, so `while.maxIterations` and `continueUntil.timeoutMs` still protect the specific statement while the new guards cap the whole script run
- expanded [`CapabilityScriptRunnerTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptRunnerTests.cs) with focused coverage for statement-budget failure, wall-clock timeout, control-depth overflow, and invalid script-definition limits
- added a small `test/sleep` capability in the script-runner test provider so the duration guard can be exercised deterministically
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) to document the new top-level script guard fields and the corresponding failure codes

Verification:

- `dotnet test Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj --filter CapabilityScriptRunnerTests`
- `dotnet build RimBridgeServer.sln`

Notes:

- this was the right hardening slice before `run_lua`: a pleasant scripting surface without whole-script budgets would make accidental infinite or near-infinite loops too easy to create
- the current guard fields already existed on the script contract, but this step is what makes them real runtime behavior instead of passive configuration data
- the scripting surface is still discoverable as a tool through GABS, but the advanced script language is not yet machine-described enough for a fresh AI to author reliably from metadata alone

Next:

- Step A26: expose a machine-readable scripting reference over GABS so new agents can discover the JSON scripting surface without relying on repo docs

## 2026-03-16 - Step A26 - Machine-Readable Script Reference Over GABS

Status:

- completed

Completed:

- added [`CapabilityScriptReferenceBuilder.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptReferenceBuilder.cs) in the core library to produce a structured scripting reference document for `rimbridge/run_script`
- exposed that document through a new [`rimbridge/get_script_reference`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) capability routed by [`ScriptingCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs) and marked it `Immediate` in [`BuiltInCapabilityModuleProvider.cs`](/Users/ap/Projects/RimBridgeServer/Source/BuiltInCapabilityModuleProvider.cs)
- documented the script root shape, statement types, expression forms, condition operators, limits, failure codes, result shape, and multiple example scripts in a machine-readable payload instead of leaving that knowledge only in the README
- updated the `rimbridge/run_script` tool description and `scriptJson` parameter description in [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) so discoverers are explicitly pointed at the new reference tool
- added focused coverage in [`CapabilityScriptReferenceBuilderTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptReferenceBuilderTests.cs) for the exposed metadata, defaults, statement coverage, condition operators, and example presence
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) so the public docs now mention the GABS-discoverable reference tool directly

Verification:

- `dotnet test Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj --filter CapabilityScriptReferenceBuilderTests`
- `dotnet test Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj --filter CapabilityScriptRunnerTests`
- `dotnet build RimBridgeServer.sln`

Notes:

- this does not replace the README or the future Lua front-end; it closes the immediate discoverability gap for machine agents that only see the live tool surface
- the reference document intentionally returns examples as structured objects so a client can inspect or serialize them directly instead of scraping prose

Next:

- Step A27: add a MoonSharp-backed `rimbridge/run_lua` front-end that lowers the supported Lua subset into the shared script runner model

## 2026-03-17 - Step A27 - MoonSharp-Backed `rimbridge/run_lua`

Status:

- completed

Completed:

- added the `MoonSharp` dependency to [`RimBridgeServer.Core.csproj`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/RimBridgeServer.Core.csproj) and introduced [`LuaScriptCompiler.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/LuaScriptCompiler.cs) as the narrow Lua front-end that parses supported Lua source and lowers it into the existing `CapabilityScriptDefinition` model
- kept the architectural boundary from the design note intact: Lua does not execute capabilities directly and does not become a second automation runtime; the compiler lowers into the same runner/reporting path already used by `rimbridge/run_script`
- implemented a first supported Lua subset covering `local` variables, scoped shadowing, table literals, static field and one-based index access, arithmetic and comparison operators, boolean `and` / `or`, unary `not` and unary minus, `if` / `elseif` / `else`, bounded `while`, numeric `for`, `for ... in ipairs(...)`, `return`, `print` / `rb.print`, and `rb.call` / `rb.poll` / `rb.assert` / `rb.fail`
- intentionally rejected broader language surface in v1, including arbitrary global mutation, dynamic table keys, dynamic indexing, `break`, module loading, coroutines, metatables, and direct CLR exposure
- extended [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs) with the extra value-expression operators needed by lowered Lua, including boolean/comparison operators and unary negation, instead of inventing Lua-only execution semantics
- wired the new front-end through [`ScriptingCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs), exposed it publicly in [`RimBridgeTools.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs), and registered both `rimbridge/run_lua` and `rimbridge/compile_lua` as immediate capabilities in [`BuiltInCapabilityModuleProvider.cs`](/Users/ap/Projects/RimBridgeServer/Source/BuiltInCapabilityModuleProvider.cs)
- made `rimbridge/compile_lua` a first-class debugging seam so callers can inspect the lowered JSON script directly and verify that Lua remains a front-end over the shared script model
- added focused coverage in [`LuaScriptCompilerTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/LuaScriptCompilerTests.cs) for call lowering, `ipairs` iteration, boolean argument lowering, control flow plus structured output, assertion behavior, preservation of local shadowing, and compile-time rejection of unsupported global assignment
- updated [`CapabilityScriptReferenceBuilder.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptReferenceBuilder.cs) and [`CapabilityScriptReferenceBuilderTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptReferenceBuilderTests.cs) so the machine-readable JSON script reference now reflects the full current expression surface shared by JSON scripts and lowered Lua
- updated [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) and [`lua-frontend-design.md`](/Users/ap/Projects/RimBridgeServer/docs/lua-frontend-design.md) to document the new Lua tools, supported subset, and compile-vs-run workflow

Verification:

- `dotnet test Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj`
- `dotnet build RimBridgeServer.sln`

Notes:

- local-variable detection in the compiler now uses MoonSharp `SourceRef` spans against the original Lua source text rather than depending on MoonSharp's registered source list; that preserves correct `local` shadowing behavior without needing a second parser
- the same script guardrails still apply after lowering, so `run_lua` inherits `maxDurationMs`, `maxExecutedStatements`, `maxControlDepth`, `while.maxIterations`, and `continueUntil.timeoutMs` through the shared runner rather than duplicating safety logic
- this completes the core Lua front-end milestone, but it does not yet prove a full live scenario end to end; the next useful acceptance step is to drive the prison scenario through `run_lua` rather than through JSON assembled by the smoke harness

Next:

- Step A28: prove `rimbridge/run_lua` against the prison scenario as a live smoke / acceptance test

## 2026-03-17 - Step A28 - Live Lua Prison Smoke

Status:

- completed

Completed:

- rewrote the existing `script-colonist-prison` live smoke in [`SmokeScenarioCatalog.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs) so the actual gameplay sequence now runs through `rimbridge/run_lua` instead of `rimbridge/run_script`
- kept the same real acceptance behavior as the earlier JSON smoke: draft the first three colonists, rally them to a tested cell, wait until they are grouped, enable god mode, build the surrounding wall ring, undraft them, unpause, and capture a screenshot
- added a preflight `rimbridge/compile_lua` call in the same live smoke so the harness now verifies both the compile-only and execute paths for the Lua front-end
- expanded the Lua script itself to exercise more than raw capability batching: it uses locals, table literals, `ipairs`, arithmetic, `rb.poll`, `rb.print`, `rb.assert`, and `return`, then the harness validates the returned result object and structured output rows
- fixed the shared script reference-root shape in [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs) to expose `attempts` alongside the other step metadata so Lua and JSON scripts can inspect poll retry counts through assigned `rb.poll(...)` / `$ref` values
- added focused regression coverage in [`CapabilityScriptRunnerTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptRunnerTests.cs) for `$ref` access to `attempts`, and updated [`CapabilityScriptReferenceBuilder.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptReferenceBuilder.cs), [`CapabilityScriptReferenceBuilderTests.cs`](/Users/ap/Projects/RimBridgeServer/Tests/RimBridgeServer.Core.Tests/CapabilityScriptReferenceBuilderTests.cs), and [`README.md`](/Users/ap/Projects/RimBridgeServer/README.md) so the documented step-reference root matches the real runtime surface

Verification:

- `dotnet test Tests/RimBridgeServer.Core.Tests/RimBridgeServer.Core.Tests.csproj --filter "CapabilityScriptRunnerTests|CapabilityScriptReferenceBuilderTests|LuaScriptCompilerTests"`
- `dotnet test Tests/RimBridgeServer.LiveSmoke.Tests/RimBridgeServer.LiveSmoke.Tests.csproj`
- `dotnet build RimBridgeServer.sln`
- `scripts/live-smoke.sh --scenario script-colonist-prison --game-id rimworld --verbose`

Notes:

- the successful live run produced report [`20260316_233929_script-colonist-prison.json`](/Users/ap/Projects/RimBridgeServer/artifacts/live-smoke/20260316_233929_script-colonist-prison.json) and screenshot [`rimbridge_script_colonist_prison_20260316_233942.png`](/Users/ap/Library/Application%20Support/RimWorld/Screenshots/rimbridge_script_colonist_prison_20260316_233942.png)
- the harness now records both `compileLua` and `runLua` scenario data artifacts for later inspection instead of keeping the old `runScript` naming
- this proves the Lua front-end on a real gameplay sequence, but the rally-cell search and playable-game bootstrap still happen in the harness rather than inside Lua itself

Next:

- Step A29: decide whether the next priority is a machine-readable Lua authoring reference or moving more of the prison setup and planning logic into Lua itself
