# RimBridgeServer Architecture and Implementation Strategy

## Purpose

RimBridgeServer should become a reliable automation layer that lets an AI develop, validate, and debug RimWorld mods against the real game, not a partial simulation. The bridge needs to support unit, UX, and integration testing while keeping the implementation surface intentionally selective: only build features that are broadly useful, stable, and worth maintaining.

The immediate design goal is to turn the current proof-of-useful-tools into a platform with:

- low-latency execution for common operations
- explicit async handling for long-running or frame-bound work
- clean separation between bridge contracts, game adapters, and optional extensions
- strong discoverability for capabilities and events
- a TDD workflow that can scale with the project

## Current State

The repository is still small and that is an advantage. It already has a working GABP host, a main-thread queue, and a set of useful tools in [`Source/RimBridgeTools.cs`](../Source/RimBridgeTools.cs). That gives us a working vertical slice but also shows the main architectural risks:

- the tool surface is monolithic and mixes transport, orchestration, game access, UI state, and serialization
- async behavior is mostly implicit, with ad hoc waiting in specific tools like screenshots
- there is no shared result envelope, operation journal, or event pipeline
- there is no test project yet
- capability discovery is limited to GABP tool metadata, not an internal registry
- optional mod integrations currently rely on reflection, even though RimWorld itself is already available through a publicized reference assembly

## Non-Negotiable Constraints

### 1. Publicized RimWorld first

RimBridgeServer already references `Krafs.Rimworld.Ref` and swaps the normal game assembly for `Assembly-CSharp_publicised.dll` during build. This must become a hard rule:

- direct RimWorld access should use the publicized API first
- reflection should be treated as an exception path, not the default
- reflection is only acceptable for third-party mod adapters where there is no stable compile-time dependency

The current project already follows the build pattern used in Achtung2. That is the correct baseline and should stay.

### 2. Main-thread ownership

All reads and writes that touch RimWorld state, map state, selection, windows, designators, debug menus, screenshots, save/load, or input must flow through a single execution abstraction that understands:

- main-thread affinity
- frame-bounded work
- long events
- synchronous fast paths
- async wait conditions

### 3. Compatibility first

We should preserve existing tool names while the internal architecture changes. External automation should not break just because internals are cleaned up.

### 4. Testability by extraction

Full coverage is only realistic if the bulk of new logic lives outside the game-specific adapter layer. The architecture therefore needs a thin RimWorld-facing shell and thick pure logic modules around:

- capability discovery
- request normalization
- script planning
- result envelopes
- operation journaling
- event correlation
- retry and wait policies

## Verified RimWorld Seams

Using the decompiler against the publicized RimWorld reference, the following seams are already available and should anchor the architecture:

- `Verse.GameDataSaveLoader.SaveGame` and `LoadGame`
- `Verse.LongEventHandler.QueueLongEvent`, `ExecuteWhenFinished`, and `AnyEventNowOrWaiting`
- `Verse.ScreenshotTaker.TakeNonSteamShot` and `QueueSilentScreenshot`
- `Verse.Messages.Message`
- `Verse.LetterStack.ReceiveLetter`
- `Verse.PlayLog.Add`
- `Verse.Log.Notify_MessageReceivedThreadedInternal`
- `Verse.DebugWindowsOpener.ToggleDebugActionsMenu`
- `LudeonTK.Dialog_Debug.TrySetupNodeGraph` and `GetNode`
- `Verse.Command.ProcessInput`
- `Verse.Designator.ProcessInput`
- `Verse.DesignatorManager.Select` and `Deselect`
- `RimWorld.MapInterface.HandleMapClicks` and `HandleLowPriorityInput`
- `Verse.LoadedModManager.ReadModSettings` and `WriteModSettings`

These seams are enough to implement most of the requested core feature set without inventing large amounts of game logic ourselves.

## Architectural Principles

### Capability-first, not tool-first

The bridge should not be designed as a growing bag of GABP methods. Internally it should expose a capability registry where each capability declares:

- a stable id
- category
- description
- argument schema
- result schema
- execution kind
- whether sync execution is supported
- whether it emits events

GABP tools then become one transport projection of that internal registry.

### First-party and third-party capabilities use the same model

If RimBridgeServer supports extension packages for other mods, the same principle should govern our own features. That means:

- core RimBridgeServer features register through the same provider contract as external extensions
- optional first-party feature groups should live in separate packages or projects when their scope justifies it
- the host should not special-case first-party capabilities beyond bootstrapping trusted default providers
- every capability package, whether shipped by us or another mod, should be discoverable through the same registry

This prevents the architecture from drifting into two incompatible systems: one for built-in features and one for extensions.

### Explicit async instead of hidden waiting

Every operation should clearly declare whether it is:

- immediate
- frame-bound
- long-event-bound
- background observed

The caller can then choose:

- `immediate` to fail fast if the game is not ready
- `wait` to block until completion or timeout
- `queue` to receive an operation id and poll or subscribe for completion

### Stable references over fuzzy strings

Human-friendly resolution like pawn name matching is useful, but the internal API should prefer stable handles once something is resolved:

- map id
- thing id
- pawn id
- window id
- menu id
- operation id
- screenshot id

This reduces ambiguity and repeated lookup cost.

### Thin adapters, thick core

Everything not inherently tied to RimWorld runtime objects should live in shared, testable libraries.

## Target Project Layout

The long-term structure should move toward this split:

```text
Source/
  RimBridgeServer.Host/               // net472 RimWorld entry assembly and GABP host
  RimBridgeServer.Contracts/          // schemas, ids, result envelopes, script AST
  RimBridgeServer.Core/               // registry, execution policies, journaling, script engine
  RimBridgeServer.Game/               // shared RimWorld adapter helpers
  RimBridgeServer.Capabilities.Core.Diagnostics/
  RimBridgeServer.Capabilities.Core.Lifecycle/
  RimBridgeServer.Capabilities.Core.Selection/
  RimBridgeServer.Capabilities.Core.View/
  RimBridgeServer.Capabilities.Core.DebugActions/
  RimBridgeServer.Capabilities.Optional.Pawns/
  RimBridgeServer.Capabilities.Optional.UI/
  RimBridgeServer.Extensions.Abstractions/   // provider contract used by all packages
  RimBridgeServer.Extensions.Achtung/        // example third-party adapter

Tests/
  RimBridgeServer.Contracts.Tests/
  RimBridgeServer.Core.Tests/
  RimBridgeServer.Game.Integration/
  RimBridgeServer.E2E/

docs/
  architecture.md
  progress-log.md
```

This can be introduced incrementally. There is no need for a single risky big bang move. The important part is that feature packages, including first-party ones, plug into the same registry and execution model.

## Target Runtime Architecture

### 1. Host layer

Responsible for:

- bootstrapping the mod
- starting and stopping the GABP server
- exposing legacy tool aliases
- exposing internal capability discovery
- managing subscriptions for events and operation updates

Suggested types:

- `BridgeHost`
- `ToolFacade`
- `CapabilityRegistry`
- `LegacyToolMapper`

### 2. Execution kernel

Responsible for:

- game-thread dispatch
- safe synchronous execution
- wait conditions
- long-event coordination
- timeout and cancellation handling
- operation journaling

Suggested types:

- `IGameThreadDispatcher`
- `GameThreadDispatcher`
- `IOperationRunner`
- `OperationRunner`
- `OperationJournal`
- `WaitCondition`
- `ExecutionMode`
- `OperationStatus`

### 3. Capability modules

Capabilities should be grouped by domain, with each domain owning request normalization and response shaping for its own area.

Each domain should preferably ship as a provider package that registers one or more capabilities into the shared registry. For first-party code, that means the host composes a set of provider packages rather than directly owning all behavior.

Core domains:

- diagnostics and logging
- game lifecycle and state
- pause and time control
- save and load
- screenshot and view targeting
- input and cursor control
- selection
- settings and mod configuration
- debug actions
- scripting and batch execution

Optional domains:

- pawn state and commands
- faction state
- context menus
- widget row and gizmo access
- inspect pane
- designators

Extension domains:

- capabilities registered by other mods
- adapters for known mods like Achtung

### 4. Observability layer

Responsible for turning game activity into structured events.

Core event sources should include:

- bridge operation lifecycle
- long event start and completion
- warnings and errors from `Verse.Log`
- message feed from `Verse.Messages`
- letter feed from `Verse.LetterStack`
- play log additions
- selection changes
- map changes
- active window changes
- screenshot completion

Suggested types:

- `IEventSource`
- `EventBus`
- `EventEnvelope`
- `ObservationSnapshot`
- `StateProbe`

### 5. Script engine

Responsible for executing batches of capability calls with low overhead, consistent waiting semantics, and a detailed report.

Suggested types:

- `ScriptDefinition`
- `ScriptStep`
- `ScriptExecutionContext`
- `ScriptRunner`
- `ScriptReport`
- `StepReport`

## Internal Contracts

The internal contract model should be established early and then reused everywhere.

### Capability descriptor

Each capability descriptor should contain:

- `Id`
- `Category`
- `Summary`
- `ArgumentsSchema`
- `ResultSchema`
- `ExecutionKind`
- `SupportsImmediate`
- `SupportsWait`
- `SupportsQueue`
- `EmitsEvents`
- `Source` such as `core`, `optional`, or `extension`

### Operation envelope

Every operation should return a consistent envelope:

```json
{
  "success": true,
  "operationId": "op_123",
  "status": "completed",
  "startedAtUtc": "2026-03-16T12:00:00Z",
  "completedAtUtc": "2026-03-16T12:00:00Z",
  "durationMs": 12,
  "result": {},
  "warnings": [],
  "events": []
}
```

Even immediate operations should have an `operationId` so logs, events, and reports can correlate cleanly.

### Target references

Introduce small, reusable reference types:

- `MapRef`
- `PawnRef`
- `ThingRef`
- `WindowRef`
- `MenuRef`
- `CellRef`
- `ScreenRectRef`

### Wait conditions

Avoid ad hoc sleeping loops inside capabilities. Centralize wait behavior around named conditions such as:

- `game.idle`
- `long_event.none`
- `window.open`
- `window.closed`
- `selection.matches`
- `screenshot.exists`
- `log.contains`

## Capability Taxonomy

### Core capabilities

These belong in RimBridgeServer itself because they are broadly useful across mod development.

#### Diagnostics

- process and game running status
- active program state
- current map and loaded game summary
- Player.log tail and structured in-game log stream
- warnings and errors subscription

#### Lifecycle and time

- pause and unpause
- speed control
- explicit `wait_until_idle`
- `wait_frames`
- `wait_for_long_event`

#### Save and load

- list saves
- save game
- load game
- quick snapshot save for test fixtures
- fixture restore helpers

#### View and screenshot

- camera state
- jump and frame
- screenshot capture
- clipped screenshot capture
- semantic screenshot targeting
- screenshot metadata including map, selection, and camera context

#### Input and selection

- mouse position
- mouse click
- keyboard input
- selection read and mutate
- click by screen rect or semantic target
- input must work even when RimWorld is not the foreground application, which rules out a foreground-only desktop automation design

#### Settings and mod config

- RimWorld prefs
- mod settings persistence through `LoadedModManager`
- safe discovery of loaded mod settings surfaces

#### Debug actions

- discover debug action tree
- resolve action by path
- execute action directly or through a UI-visible mode when needed
- support pinning and toggles where exposed by `DebugActionNode`

#### Script execution

- run a structured batch
- choose sync or async execution mode per step
- capture detailed report and intermediate artifacts

### Optional capabilities

These are useful, but should sit behind separate modules so they can evolve independently.

- pawn state and commands
- faction state
- context menu inspection and execution
- widget row and bottom-left gizmo access
- inspect pane extraction
- designator discovery and application

### Extension capabilities

The extension model should allow external mods to register additional capabilities with descriptors that look identical to core ones.

## Debug Actions Strategy

Debug action access should not devolve into custom tool-per-action code. The correct design is:

- use the internal debug node graph as the discovery surface
- expose nodes by stable path
- let callers query children before execution
- support direct execution where the node semantics allow it
- preserve a UI-backed fallback path for actions that require actual dialog interaction

This is one of the biggest opportunities to reduce waiting time because it avoids implementing one-off wrappers for individual debug items.

## Input and UI Strategy

The input stack should support two modes.

### Semantic mode

The preferred mode for automation:

- resolve a semantic target such as a selected pawn, menu option, designator, or gizmo
- execute through the underlying command object when possible
- return structured evidence of what was targeted

### Physical mode

Required for cases where the UI itself is under test:

- screen coordinate targeting
- rect clipping
- mouse move and click
- key press simulation
- screenshot before and after action

Physical mode should still be implemented inside RimWorld's process or window event path wherever possible. Foreground-dependent OS desktop input is not a sufficient design because automated test runs may keep RimWorld in the background.

This split matters because functional automation and UX automation are different jobs. We should not pay the cost of physical UI simulation when a direct command path is available.

## Event Model

The event system should be treated as a first-class API, not a future add-on.

Each event should include:

- `sequence`
- `timestampUtc`
- `category`
- `type`
- `source`
- `operationId` when relevant
- `payload`

Core categories:

- `bridge`
- `operation`
- `game`
- `long_event`
- `log`
- `message`
- `letter`
- `selection`
- `window`
- `capability`

For real-time debugging, the log pipeline should combine:

- tailing `Player.log`
- patched `Verse.Log.Notify_MessageReceivedThreadedInternal`
- structured warnings and errors raised by bridge code itself

## Script Language Strategy

Do not start with a custom textual parser. That is unnecessary risk. The low-risk first version should be a structured JSON script format that every client can generate easily.

Suggested first shape:

```json
{
  "name": "load-fixture-and-run-debug-action",
  "defaults": {
    "mode": "wait",
    "timeoutMs": 10000
  },
  "steps": [
    { "call": "rimworld.load_game", "args": { "saveName": "fixture_a" } },
    { "wait": { "condition": "game.idle", "timeoutMs": 60000 } },
    { "call": "rimworld.debug_actions.execute", "args": { "path": "Pawns/..." } },
    { "call": "rimworld.take_screenshot", "args": { "fileName": "after_action" } }
  ]
}
```

The important property is not syntax. The important property is that every step can target any registered capability, including extension capabilities, and produces a uniform report.

Later, a human-friendly DSL can be layered on top if it is still worth it.

## Extension Strategy

The extension system should begin with explicit registration, not automatic scanning magic.

Recommended first model:

- define `RimBridgeServer.Extensions.Abstractions`
- expose an interface such as `IRimBridgeCapabilityProvider`
- providers register descriptors and handlers during mod startup
- each extension uses a namespace like `mod.<modid>/...`

The same interface should be used by first-party packages, for example:

- `rimbridge.core/diagnostics/...`
- `rimbridge.core/lifecycle/...`
- `rimbridge.core/debug_actions/...`
- `rimbridge.optional/ui/...`

Only after the explicit model is working should we consider discovery helpers.

For third-party mods:

- prefer compile-time adapters if the dependency is stable and acceptable
- otherwise isolate reflection into one adapter assembly per mod
- never let reflection leak into core execution or contract code

## Testing Strategy

### Testing objective

Target full coverage by pushing almost all branching logic into `Contracts` and `Core`, then keeping `Game` adapters thin and validated with deterministic in-game scenarios.

### Test pyramid

#### Unit tests

Scope:

- capability registry
- request validation
- result envelopes
- wait condition evaluation
- operation journal
- script planning and report generation
- event correlation
- sync vs queue policy logic

These should aim for near-100 percent line and branch coverage.

#### Contract tests

Scope:

- legacy tool alias mapping
- capability descriptor completeness
- schema compatibility
- serialization stability

#### In-game integration tests

Scope:

- real save and load
- pause and time control
- selection behavior
- screenshot capture
- debug action discovery and execution
- log and message capture
- designator and command invocation

These should run against stable fixture saves and quick-test colonies.

#### End-to-end tests through GABS

Scope:

- launch RimWorld
- connect through GABP
- execute scripts end to end
- verify screenshots, logs, and operation reports

### Async test matrix

Every async-capable feature should be exercised across:

- immediate success
- queued completion
- long-event overlap
- timeout
- cancellation
- missing target after map or state change
- event correlation correctness

## CI and Workflow Strategy

Each incremental step should follow the same discipline:

1. add or update tests first
2. implement the smallest coherent vertical slice
3. run focused tests locally
4. run a build of the mod
5. if the step touches game integration, run the relevant GABS-driven smoke case
6. update `docs/progress-log.md`
7. commit
8. push

The rule is to keep the tree in a releasable state after every step.

## Low-Risk Incremental Roadmap

### Step A0. Architecture baseline

Deliverables:

- architecture document
- progress log
- explicit constraints around publicized RimWorld usage

Exit criteria:

- agreed target structure exists in-repo

### Step A1. Extract contracts and result envelope

Deliverables:

- `Contracts` project
- `Extensions.Abstractions` project with the provider contract used by both first-party and third-party packages
- operation envelope types
- capability descriptor types
- shared ids and references
- adapter layer that keeps current tools working

Tests:

- serialization
- validation
- compatibility

### Step A2. Extract execution kernel

Deliverables:

- `GameThreadDispatcher`
- `OperationRunner`
- timeout and wait condition support
- operation journal

Tests:

- main-thread dispatch behavior with fakes
- timeout behavior
- queued vs immediate execution

### Step A3. Refactor existing tools into capability modules

Deliverables:

- current features moved out of `RimBridgeTools`
- first-party provider packages for the existing feature groups
- legacy tool aliases preserved
- new registry-backed dispatch

Tests:

- capability discovery
- legacy alias contract tests
- integration smoke for existing feature set

### Step A4. Observability and diagnostics

Deliverables:

- event bus
- structured in-game log capture
- Player.log tail reader
- long-event observation

Tests:

- event envelopes
- log capture filtering
- long-event state transitions

### Step A5. Lifecycle, save/load, and time service

Deliverables:

- consolidated lifecycle service
- wait helpers
- faster sync control paths for common flows

Tests:

- save/load lifecycle
- pause and speed behavior
- wait conditions around long events

### Step A6. View, targeting, input, and screenshot service

Deliverables:

- screen and map target references
- clipped screenshot support
- semantic target resolution
- input service abstraction

Tests:

- unit tests for target resolution
- integration tests for screenshot and selection flows
- UX smoke tests with fixture screenshots

### Step A7. Generic debug action service

Deliverables:

- debug action discovery
- debug action path execution
- reportable debug-action results

Tests:

- path resolution
- discovery stability
- integration execution cases

### Step A8. Script runner

Deliverables:

- JSON script format
- script execution engine
- step-level report
- mixed sync and async steps

Tests:

- report correctness
- rollback and failure reporting semantics
- end-to-end script execution

### Step A9. Optional UI adapters

Deliverables:

- gizmo access
- context menu improvements
- designator access
- inspect pane and widget row extraction

Tests:

- per-adapter integration tests
- UI regression scripts

### Step A10. Extension system

Deliverables:

- extension abstraction package
- registration lifecycle
- extension discovery endpoint
- first sample extension

Tests:

- registration
- namespacing
- script access through extension capabilities

### Step A11. Harden for autonomous development

Deliverables:

- fixture management
- canned repro scripts
- coverage gates for `Contracts` and `Core`
- nightly end-to-end runs

Tests:

- complete automated smoke matrix

## Decisions To Keep Us Fast

- prefer internal direct execution over UI simulation when the goal is functionality testing
- prefer UI simulation when the goal is UX validation
- prefer JSON scripts over a custom DSL in v1
- prefer explicit extension registration over auto-discovery in v1
- prefer stable ids over repeated fuzzy name lookup
- prefer publicized RimWorld APIs over reflection
- prefer reflection only in isolated third-party adapters such as Achtung integration

## Immediate Next Step

The next implementation step should be Step A1: extract the shared contracts and the operation envelope without changing externally visible tool names. That creates the seam needed for every later feature while keeping the risk low.
