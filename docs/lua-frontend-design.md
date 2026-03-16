# Lua Front-End Design for `rimbridge/run_lua`

## Decision

If RimBridgeServer adds a human-friendly scripting language, the best fit is:

- Lua syntax
- MoonSharp as the embedded interpreter or parser layer
- the existing script runner as the execution backend

The important constraint is architectural: Lua should not become a second direct automation runtime that talks to RimWorld on its own. It should lower into, or delegate through, the same registry-backed execution/reporting path already used by [`rimbridge/run_script`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs).

## Status

As of 2026-03-17, slices 3 and 4 below are implemented. [`rimbridge/run_lua`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) and [`rimbridge/compile_lua`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeTools.cs) now compile a narrow Lua v1 subset into the shared script runner instead of introducing a second automation runtime. The remaining work is around proving that front-end against richer live scenarios and improving authoring/discoverability, not around the core Lua execution path itself.

## Why Lua

Lua is small, readable, and good at the kind of glue logic these scripts need:

- local variables
- tables
- arithmetic
- loops
- conditionals
- small helper abstractions

For this repo, MoonSharp is the right Lua implementation shape because it is pure C# and does not require native runtime packaging. That matters for a RimWorld mod targeting `net472` and running inside Unity/Mono.

Rejected alternatives:

- NLua/KeraLua:
  pulls in native runtime complexity and cross-platform packaging risk that this mod does not need
- Jint:
  technically viable and actively maintained, but JavaScript is less compact for this style of game automation and is not the smallest language that solves the problem

## Goals

- Move from JSON-only ordered scripts to a small readable language suitable for real scenario logic.
- Preserve the current capability model so every registered capability remains scriptable automatically.
- Preserve the current reporting model: per-step operation ids, timings, success/failure, warnings, and optional results.
- Support the minimum missing control flow needed for dynamic scenarios such as:
  - starting from a connected RimWorld session
  - resetting to main menu
  - starting a fresh debug colony
  - choosing cells dynamically
  - iterating over colonists or wall segments
  - waiting until a generic condition becomes true

## Non-Goals

- Host-level process control inside Lua. `games.start` and `games.connect` remain outside the in-game capability registry.
- Arbitrary CLR access from Lua.
- File I/O, OS access, networking, module loading, or debug-library access from Lua.
- Replacing the existing JSON script format.
- Replacing the current `CapabilityScriptRunner` with a separate scripting engine.

## Current Backend That Must Stay

The current backend is already the right execution core:

- [`ScriptingCapabilityModule.cs`](/Users/ap/Projects/RimBridgeServer/Source/ScriptingCapabilityModule.cs)
- [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs)
- [`CapabilityScriptContracts.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Contracts/CapabilityScriptContracts.cs)

It already provides the hard operational guarantees we care about:

- execution through the shared capability registry
- uniform child operation metadata
- per-step success/failure reporting
- value passing with `$ref`
- bounded polling with `continueUntil`
- predictable halt-on-failure behavior

Lua should be introduced as a front-end over that backend, not as a new automation stack.

## Recommended Architecture

### Shape

Recommended layering:

```text
Lua source
  -> Lua front-end
  -> extended script AST / lowered script model
  -> CapabilityScriptRunner
  -> CapabilityRegistry
  -> existing capability implementations
```

This keeps one execution model and one report model.

### Public API

Add a sibling tool rather than overloading `rimbridge/run_script`:

- `rimbridge/run_lua`
- `rimbridge/compile_lua`

Suggested first signature:

```text
rimbridge/run_lua(
  luaSource: string,
  includeStepResults: bool = true
)
```

Optional compile-only/debug tool:

```text
rimbridge/compile_lua(
  luaSource: string
)
```

`compile_lua` is useful for debugging lowering errors and for verifying that Lua remains a front-end over the shared script model.

## Internal Changes Needed Before Lua Is Useful

The current JSON runner is intentionally step-oriented. To support meaningful Lua control flow cleanly, the internal script model needs a small control-flow expansion.

### Statement Kinds

Recommended statement kinds:

- `call`
- `block`
- `let`
- `if`
- `foreach`
- `while`

### Expression Kinds

Recommended expression kinds:

- literal values
- variable lookup
- prior-step reference
- object/table construction
- array construction
- property access
- index access
- unary operators such as `not` and unary minus
- binary operators:
  - arithmetic
  - comparison
  - boolean `and` / `or`

### Reporting Rule

Only capability calls should produce ordinary step reports.

Control statements should not flood the existing report format. If extra visibility is needed later, add a lightweight trace channel, but keep the main report focused on concrete capability executions.

### Loop Safety

Every loop path must remain bounded. Recommended guards:

- maximum lowered statement count
- maximum loop iteration count
- maximum script wall-clock duration
- maximum nested control depth

Lua should make scripts easier to write, not make it possible to hang RimWorld with an unbounded loop.

## Lua v1 Subset

The first Lua slice should be intentionally narrow.

### Supported

- `local` variables
- table literals
- field and index access
- arithmetic and comparisons
- boolean operators
- `if` / `elseif` / `else`
- numeric `for`
- array iteration via `ipairs`
- `while`
- calls to a narrow host API under a single namespace such as `rb`

### Not Supported In v1

- `require`
- metatables
- coroutines
- user-provided global mutation outside the script scope
- direct CLR interop
- `io`, `os`, `package`, and `debug` libraries
- arbitrary library import

This is still a real language, just one constrained to automation needs.

## Host API Shape

The host API exposed to Lua should stay narrow and explicit. A single namespace is preferable:

```lua
rb.call("rimworld/go_to_main_menu")
rb.call("rimworld/start_debug_game")
status = rb.call("rimbridge/get_bridge_status")
rb.poll("rimbridge/get_bridge_status", {}, {
  timeoutMs = 30000,
  pollIntervalMs = 100,
  condition = {
    all = {
      { path = "result.state.inEntryScene", equals = true },
      { path = "result.state.programState", equals = "Entry" }
    }
  }
})
```

Initial host helpers should stay close to the current runner semantics:

- `rb.call(alias, args?)`
- `rb.poll(alias, args?, policy)`
- `rb.ref(stepId, path?)` only if needed after AST support is extended

Important constraint:

- Lua must never receive direct RimWorld objects
- Lua only sees plain projected values and plain dictionaries/lists

## Example Target Script

This is the kind of script the system should support after the first Lua slice:

```lua
rb.call("rimworld/go_to_main_menu")

rb.poll("rimbridge/get_bridge_status", {}, {
  timeoutMs = 30000,
  pollIntervalMs = 100,
  condition = {
    all = {
      { path = "result.state.inEntryScene", equals = true },
      { path = "result.state.programState", equals = "Entry" },
      { path = "result.state.hasCurrentGame", equals = false },
      { path = "result.state.longEventPending", equals = false }
    }
  }
})

rb.call("rimworld/start_debug_game")
rb.call("rimbridge/wait_for_game_loaded", {
  timeoutMs = 60000,
  pollIntervalMs = 100,
  waitForScreenFade = true,
  pauseIfNeeded = true
})

local colonists = rb.call("rimworld/list_colonists", { currentMapOnly = true })

for i, colonist in ipairs(colonists.result.colonists) do
  rb.call("rimworld/select_pawn", {
    pawnName = colonist.name,
    append = i > 1
  })
end
```

This example is intentionally close to the current JSON semantics. Lua adds readability and normal control flow; it should not invent a separate automation model.

## Implementation Strategy

### Slice 1: Extract a Reusable Execution Core

Refactor the current runner so the per-call execution/reporting path can be reused by richer script forms.

Deliverables:

- extract the call-execution/reporting logic out of the current monolithic loop in [`CapabilityScriptRunner.cs`](/Users/ap/Projects/RimBridgeServer/Source/RimBridgeServer.Core/CapabilityScriptRunner.cs)
- preserve current JSON behavior exactly
- add tests proving no regression in `run_script`

### Slice 2: Extend the Internal Script Model

Add the smallest control-flow model needed for dynamic scripts.

Deliverables:

- new statement and expression contracts in `RimBridgeServer.Contracts`
- runner support for `let`, `if`, `foreach`, and bounded `while`
- tests for variable scope, loop bounds, and report shape

This slice should land before Lua. It is useful on its own and makes the execution model explicit.

### Slice 3: Add a Lua Front-End Without Executing Game Logic Directly

Introduce MoonSharp and build a narrow front-end that lowers supported Lua into the extended script model.

Deliverables:

- `LuaScriptCompiler`
- syntax/lowering diagnostics
- compile-only tests
- no new direct capability execution path

Status:

- completed on 2026-03-17

### Slice 4: Add `rimbridge/run_lua`

Once lowering is stable, expose the public tool.

Deliverables:

- `rimbridge/run_lua`
- `rimbridge/compile_lua`
- README examples
- safety limits on script size and complexity

Status:

- completed on 2026-03-17

### Slice 5: Prove It With the Prison Scenario

Use the user-provided prison flow as the first serious smoke case.

Success criteria:

- one Lua script starts from a connected RimWorld session
- it resets to main menu
- starts a fresh debug colony
- drafts and groups colonists
- builds the enclosing wall
- undrafts them
- captures a screenshot
- returns the same high-quality report shape as `run_script`

## Risks

### 1. Runtime Drift

If Lua grows its own execution semantics instead of lowering into the shared backend, the project will split into two automation systems. That should be treated as a failure mode.

### 2. Sandbox Holes

Any accidental CLR exposure or broad standard-library enablement turns Lua from “small helper language” into “arbitrary code execution inside the game process”.

### 3. Loop Abuse

The language must be pleasant, but it cannot be allowed to hang the game. Limits are part of the design, not a later hardening pass.

### 4. Report Bloat

If every control node becomes a report row, script output will become noisy and harder to consume. Preserve step reports for capability calls.

## Recommendation Summary

Recommended path:

1. keep the current JSON runner as the execution core
2. extend the internal script model with minimal control flow
3. add MoonSharp-based Lua as a front-end over that model
4. expose `rimbridge/run_lua`
5. prove it with the prison scenario

This is the smallest path that gives real scripting power without discarding the work already done on `rimbridge/run_script`.
