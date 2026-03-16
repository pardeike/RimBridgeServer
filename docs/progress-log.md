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
