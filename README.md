# RimBridgeServer

RimBridgeServer lets you control RimWorld from outside the game. This is useful for testing mods automatically or building tools that work with RimWorld.

Project planning and the implementation roadmap live in [`docs/architecture.md`](docs/architecture.md) and [`docs/progress-log.md`](docs/progress-log.md).

## What does it do?

This mod creates a connection point (called a "server") inside RimWorld. Other programs can connect to this server to:

- Check if the game is running
- Pause or unpause the game
- Get information about the current game
- Inspect colonists, selection, camera, saves, screenshots, screen-space UI targets, Architect designators, zones, and areas
- Open and execute context-menu actions for debugging mods

This is especially helpful for:
- **Mod developers** who want to test their mods automatically
- **AI tools** that need to interact with RimWorld
- **External programs** that want to read game data

## Features

- **Easy to use**: Works automatically when you start RimWorld
- **Safe**: Only accepts connections from your own computer
- **Simple**: Uses a standard protocol that many tools understand
- **Compatible**: Works with RimWorld 1.6
- **Debuggable**: Exposes debug-action, Architect, context-menu, and screenshot tools that are useful for mod repro cases

## How to get started

### Installation

1. Download the mod and put it in your RimWorld Mods folder
2. Enable the mod in RimWorld
3. Start the game

That's it! The server starts automatically when RimWorld loads.

### How to connect

When RimBridgeServer starts in standalone mode, it will show messages like:

```
[RimBridge] GABP server running standalone on port 5174
[RimBridge] Bridge token: abc123...
```

Your external program needs:
- **Port number**: Usually 5174 (but check the log to be sure)
- **Token**: The random text shown in the log (for security)
- **Address**: Always 127.0.0.1 (your own computer only)

### Working with GABS

If you use [GABS](https://github.com/pardeike/GABS) (an AI gaming environment), RimBridgeServer will automatically configure itself. No extra setup is needed, and the standalone token log line is omitted because GABS injects the bridge configuration through environment variables.

## Available commands

Your external program can send these commands to RimBridgeServer:

### Basic commands
- **`rimbridge/ping`** - Test if the connection is working (responds with `"pong"`)

### Bridge diagnostics and waits
- **`rimbridge/get_bridge_status`** - Get the current bridge state snapshot, including whether RimWorld is in the entry scene or has a loaded game
- **`rimbridge/get_operation`** - Get the latest journal snapshot for a specific operation id
- **`rimbridge/list_operations`** - List recent bridge operations from the in-memory operation journal
- **`rimbridge/list_operation_events`** - List recent bridge operation lifecycle events
- **`rimbridge/list_logs`** - List recent captured RimWorld and bridge log entries
- **`rimbridge/wait_for_operation`** - Wait until a recorded operation reaches a terminal status
- **`rimbridge/wait_for_game_loaded`** - Wait until RimWorld has finished loading a playable game
- **`rimbridge/wait_for_long_event_idle`** - Wait until RimWorld reports no long event in progress
- **`rimbridge/get_script_reference`** - Get a machine-readable authoring reference for `rimbridge/run_script`
- **`rimbridge/get_lua_reference`** - Get a machine-readable authoring reference for `rimbridge/run_lua` and `rimbridge/run_lua_file`
- **`rimbridge/run_script`** - Execute a JSON script containing an ordered list of capability calls and return a step-by-step report
- **`rimbridge/run_lua`** - Compile and execute a narrow Lua scripting subset through the shared script runner
- **`rimbridge/run_lua_file`** - Load a `.lua` file from disk, inject a read-only `params` table, and execute it through the shared script runner
- **`rimbridge/compile_lua`** - Compile supported Lua into the lowered JSON script model without executing capability calls
- **`rimbridge/compile_lua_file`** - Load a `.lua` file from disk and compile it into the lowered JSON script model without executing capability calls

### Game control
- **`rimworld/get_game_info`** - Get information about the current game
- **`rimworld/pause_game`** - Pause or unpause the game
- **`rimworld/list_debug_action_roots`** - List the top-level debug menu tabs and their stable internal paths
- **`rimworld/list_debug_action_children`** - List direct children under one debug menu path
- **`rimworld/get_debug_action`** - Get one debug node with metadata such as tab, toggle state, and execution mode
- **`rimworld/execute_debug_action`** - Execute a supported debug action and return captured side effects such as logs and opened windows, including pawn-target actions when `pawnName` is provided
- **`rimworld/set_debug_setting`** - Set a debug settings toggle to a deterministic on/off state by stable path
- **`rimworld/get_designator_state`** - Get the current Architect/designator selection state, including god mode and the selected designator
- **`rimworld/set_god_mode`** - Enable or disable RimWorld god mode deterministically
- **`rimworld/list_architect_categories`** - List the visible Architect categories with stable ids
- **`rimworld/list_architect_designators`** - List Architect designators for one category, flattening dropdown widgets into actionable child ids
- **`rimworld/select_architect_designator`** - Select an Architect designator by stable id without relying on foreground input
- **`rimworld/apply_architect_designator`** - Apply an Architect designator to one cell or rectangle, with optional dry-run validation
- **`rimworld/list_zones`** - List current-map zones such as stockpiles and growing zones
- **`rimworld/list_areas`** - List current-map areas such as home, roof, snow-clear, and allowed areas
- **`rimworld/create_allowed_area`** - Create a new allowed area and optionally select it for later area-designator calls
- **`rimworld/select_allowed_area`** - Select or clear the current allowed-area target deterministically
- **`rimworld/set_zone_target`** - Set or clear the explicit existing-zone target on a zone-add designator
- **`rimworld/clear_area`** - Clear all cells from a mutable area
- **`rimworld/delete_area`** - Delete a mutable area such as a custom allowed area
- **`rimworld/delete_zone`** - Delete an existing zone by id
- **`rimworld/get_cell_info`** - Inspect one map cell, including blueprints, frames, solid things, designations, zones, and areas
- **`rimworld/find_random_cell_near`** - Use RimWorld's generic nearby-cell search to find a cell or footprint that satisfies reusable map criteria
- **`rimworld/flood_fill_cells`** - Use RimWorld's generic flood-fill algorithm to measure contiguous matching space from one root cell
- **`rimworld/get_ui_state`** - Inspect the current RimWorld window stack and UI/input state
- **`rimworld/press_accept`** - Send semantic accept input to the active RimWorld window stack
- **`rimworld/press_cancel`** - Send semantic cancel input to the active RimWorld window stack
- **`rimworld/close_window`** - Close an open RimWorld window by type name or close the topmost window
- **`rimworld/click_screen_target`** - Semantically click a known actionable target id returned by `rimworld/get_screen_targets`
- **`rimworld/go_to_main_menu`** - Return to the RimWorld main menu entry scene, or no-op if already there
- **`rimworld/start_debug_game`** - Start RimWorld's built-in quick test colony from the main menu
- **`rimworld/list_saves`** - List saved games
- **`rimworld/spawn_thing`** - Spawn a thing on the current map at a target cell
- **`rimworld/save_game`** - Save the current game to a named save
- **`rimworld/load_game`** - Load a named save

### Pawn inspection and control
- **`rimworld/list_colonists`** - List player-controlled colonists with state such as selected/drafted/downed/job/position
- **`rimworld/clear_selection`** - Clear the current selection
- **`rimworld/select_pawn`** - Select one colonist by name
- **`rimworld/deselect_pawn`** - Deselect one selected colonist by name
- **`rimworld/set_draft`** - Draft or undraft a colonist by name

### Camera, targeting, and screenshots
- **`rimworld/get_camera_state`** - Report camera position, zoom, and current view rect
- **`rimworld/get_screen_targets`** - Report current screen-space targets such as open windows, focused dialogs, and active context-menu option rects
- **`rimworld/jump_camera_to_pawn`** - Jump the camera to a pawn and select it
- **`rimworld/jump_camera_to_cell`** - Jump the camera to a specific map cell
- **`rimworld/move_camera`** - Move the camera by a cell offset
- **`rimworld/zoom_camera`** - Adjust camera zoom relative to the current root size
- **`rimworld/set_camera_zoom`** - Set camera root size directly
- **`rimworld/frame_pawns`** - Frame several pawns together in view
- **`rimworld/take_screenshot`** - Save an in-game screenshot and return the file path plus optional screen-target metadata

### Context-menu debugging
- **`rimworld/open_context_menu`** - Open a debug context menu at a pawn or cell
- **`rimworld/get_context_menu_options`** - Return the currently opened debug menu options
- **`rimworld/execute_context_menu_option`** - Execute one menu option by index or label
- **`rimworld/close_context_menu`** - Close the currently opened debug context menu

`rimworld/start_debug_game` mirrors RimWorld's own dev quick-test flow. It only works from the main menu and returns a detailed state snapshot when the request is rejected.

`rimworld/go_to_main_menu` is the matching lifecycle reset seam. It is idempotent: if RimWorld is already at the entry scene with no loaded game, the call succeeds with a no-op status. Otherwise it queues a return to the main menu so later script steps can start from a stable known state.

`rimworld/open_context_menu` is vanilla-only. `mode: vanilla` is the intended value, and the older `mode: auto` alias is still accepted for backward compatibility but resolves to the same vanilla behavior.

### Debug menu mapping

`rimworld/list_debug_action_roots` exposes the same internal graph that powers RimWorld's debug dialog, including the three important tabs: `Actions/tools`, `Settings`, and `Output`. The bridge keeps the stable internal paths such as `Outputs\\Tick Rates` and `Settings\\Show Architect Menu Order`, while also returning normalized tab metadata so clients can stay close to the in-game UI model.

`rimworld/execute_debug_action` is intentionally side-effect aware. Instead of pretending every debug action has a typed function return, the bridge captures what actually happened around the execution: new log entries, window opens/closes, and before/after game state snapshots. This makes the `Output` tab useful without bespoke wrappers for each individual output command.

Pawn-target debug actions are now supported through the same surface. Discovery metadata exposes `execution.requiredTargetKind = "pawn"` for `ToolMapForPawns` leaves, and callers can execute those nodes by passing `pawnName` to `rimworld/execute_debug_action`. That makes actions such as `Actions\\T: Toggle Job Logging` and `Actions\\T: Log Job Details` reachable without foreground input or a second pawn-specific API.

`rimworld/set_debug_setting` builds deterministic semantics on top of the same graph. Settings nodes already expose their current `on` state and a direct toggle action, so the bridge can move them to an explicit target value and report whether anything changed.

### Architect and god-mode mapping

`rimworld/list_architect_categories` and `rimworld/list_architect_designators` expose the same build/designation surface a player sees in the Architect menu, but with stable ids that are usable from automation. The bridge keeps the category structure, flattens dropdown widgets into actionable child ids, and reports build-specific metadata such as `buildableDefName` and `stuffDefName`.

The designator payload now also reports drag and targeting metadata from RimWorld itself, including `applicationKind`, `supportsRectangleApplication`, `dragDrawMeasurements`, `drawStyleCategoryDefName`, and `zoneTypeName` where applicable. That makes dropdown-heavy floor tools, zone tools, and area tools discoverable without guessing from UI text.

`rimworld/set_god_mode` and `rimworld/apply_architect_designator` make the important dev workflow deterministic. With god mode disabled, build designators create blueprints; with god mode enabled, the same designator can place the finished structure directly. `rimworld/list_zones`, `rimworld/list_areas`, and `rimworld/get_cell_info` exist specifically so tests can verify zone, area, and structure outcomes without OCR or pixel heuristics.

For generic map planning, the bridge now also exposes RimWorld's own search primitives instead of forcing callers to brute-force probe cells manually. `rimworld/find_random_cell_near` wraps `RCellFinder.TryFindRandomCellNearWith`, while `rimworld/flood_fill_cells` wraps `Verse.FloodFiller.FloodFill`. Both tools share the same footprint criteria surface, so the same request can ask for a single walkable cell, a centered `3x3` open area, or a placement-compatible footprint validated against an Architect designator.

The stateful parts of RimWorld's Zone menu now also have explicit bridge controls instead of hidden UI context. `rimworld/create_allowed_area` and `rimworld/select_allowed_area` control the allowed-area target used by `Designator_AreaAllowedExpand`, while `rimworld/set_zone_target` pins a zone-add designator to an existing zone so later placement expands that specific zone instead of creating a new one. `rimworld/clear_area`, `rimworld/delete_area`, and `rimworld/delete_zone` provide the corresponding cleanup seam for test fixture teardown.

### Batch scripting

`rimbridge/run_script` is the first low-risk batch layer on top of the capability registry. It accepts a JSON payload with ordinary capability calls as steps, executes them in order, and returns a uniform per-step report with child operation ids, timings, success/failure state, and optional step results.

Fresh clients do not need to infer the script language from this README alone. Call `rimbridge/get_script_reference` over GABS to retrieve a machine-readable reference document covering the root script shape, statement types, expressions, conditions, limits, failure codes, result shape, and copyable examples.

That means a single in-game script can now own the full lifecycle from "connected to RimWorld" onward. A script can begin by calling `rimworld/go_to_main_menu`, wait until `rimbridge/get_bridge_status` reports the entry scene, start a fresh debug colony, wait for the map to finish loading, and then continue into normal gameplay actions and a final screenshot.

Current first-shape example:

```json
{
  "name": "wall-sequence",
  "continueOnError": false,
  "steps": [
    {
      "id": "god_on",
      "call": "rimworld/set_god_mode",
      "arguments": { "enabled": true }
    },
    {
      "id": "place_wall",
      "call": "rimworld/apply_architect_designator",
      "arguments": {
        "designatorId": "architect-designator:structure:build-wall",
        "x": 99,
        "z": 121,
        "keepSelected": false
      }
    }
  ]
}
```

Lifecycle example from a connected RimWorld session:

```json
{
  "name": "debug-colony-bootstrap",
  "continueOnError": false,
  "steps": [
    {
      "id": "main_menu",
      "call": "rimworld/go_to_main_menu"
    },
    {
      "id": "wait_entry",
      "call": "rimbridge/get_bridge_status",
      "continueUntil": {
        "timeoutMs": 30000,
        "pollIntervalMs": 100,
        "condition": {
          "all": [
            { "path": "result.state.inEntryScene", "equals": true },
            { "path": "result.state.programState", "equals": "Entry" },
            { "path": "result.state.hasCurrentGame", "equals": false },
            { "path": "result.state.longEventPending", "equals": false }
          ]
        }
      }
    },
    {
      "id": "start_debug",
      "call": "rimworld/start_debug_game"
    },
    {
      "id": "wait_start_complete",
      "call": "rimbridge/wait_for_operation",
      "arguments": {
        "operationId": { "$ref": "start_debug", "path": "operationId" },
        "timeoutMs": 60000,
        "pollIntervalMs": 50
      }
    },
    {
      "id": "wait_loaded",
      "call": "rimbridge/wait_for_game_loaded",
      "arguments": {
        "timeoutMs": 60000,
        "pollIntervalMs": 100,
        "waitForScreenFade": true,
        "pauseIfNeeded": true
      }
    },
    {
      "id": "capture",
      "call": "rimworld/take_screenshot",
      "arguments": {
        "fileName": "rimbridge_debug_colony_bootstrap",
        "includeTargets": false
      }
    }
  ]
}
```

Step-to-step value passing is now available through explicit reference objects inside `arguments`. Use `{"$ref":"step_id","path":"result.someField"}` to pull a value from an earlier step. The `path` is optional and defaults to `result`, so `{"$ref":"step_id"}` reuses the earlier step's raw result object.

Level 2 polling is available through optional per-step `continueUntil` blocks. A `continueUntil` policy re-runs that same step until its condition matches or the timeout expires. This is useful for read or poll steps such as `list_colonists`, `get_ui_state`, or `get_designator_state` after an earlier mutating action has already been issued.

The runner now also supports a small generic control-flow layer inside the same JSON format:

- `{"type":"let","name":"x","value":...}` declares a scoped variable
- `{"type":"set","name":"x","value":...}` updates an existing variable in the active scope chain
- `{"type":"if","condition":{...},"body":[...],"elseBody":[...]}` branches on the same bounded condition model used by `continueUntil`
- `{"type":"foreach","itemName":"item","indexName":"i","collection":...,"body":[...]}` iterates a resolved collection
- `{"type":"while","condition":{...},"maxIterations":100,"body":[...]}` executes a bounded loop
- `{"type":"assert","condition":{...},"message":"..."}` fails the script immediately when an assumption is not satisfied
- `{"type":"fail","message":"...","value":...}` stops the script immediately with an explicit failure
- `{"type":"print","message":"...","value":...}` appends a structured trace row to the script output
- `{"type":"return","value":...}` ends the script successfully with a final structured result

Value expressions can now also read variables with `{"$var":"name"}`, do arithmetic with `{"$add":[...]}`, `{"$subtract":[left,right]}`, `{"$multiply":[...]}`, `{"$divide":[left,right]}`, and `{"$mod":[left,right]}`, and produce booleans with `{"$negate":...}`, `{"$not":...}`, `{"$and":[...]}`, `{"$or":[...]}`, `{"$equals":[left,right]}`, `{"$notEquals":[left,right]}`, `{"$greaterThan":[left,right]}`, `{"$greaterThanOrEqual":[left,right]}`, `{"$lessThan":[left,right]}`, and `{"$lessThanOrEqual":[left,right]}`.

Successful control statements do not add rows to the per-step report. The report remains focused on capability calls, while failed control statements such as `assert` and `fail` surface as failed script steps.

This also makes `rimbridge/run_script` usable as a test-like tool call. The outer tool response now includes:

- `success`: overall script pass/fail
- `message`: success summary or the assertion/failure message
- `error`: the top-level script error when the run fails
- `output`: structured rows produced by `print`
- `result`: the value returned by `return`, if any
- `script`: the full operational report with child step details

Scripts also now have three global guardrails at the definition level:

- `maxDurationMs`: whole-script wall-clock budget, default `60000`
- `maxExecutedStatements`: total execution budget across control statements, loop iterations, and retry attempts, default `1000`
- `maxControlDepth`: maximum nested control-body depth, default `32`

Those global limits complement the existing local bounds on `while.maxIterations` and `continueUntil.timeoutMs`. When a script exceeds them, `rimbridge/run_script` fails with `script.timeout`, `script.statement_limit_exceeded`, or `script.max_depth_exceeded`.

### Lua front-end

`rimbridge/run_lua` and `rimbridge/run_lua_file` now sit on top of that same runner instead of introducing a second execution path. They compile a narrow Lua subset into the shared JSON script model, then execute the lowered script through the normal capability registry. The outer result shape matches `rimbridge/run_script`: `success`, `message`, `error`, `output`, `result`, and the full `script` report.

Fresh clients do not need to infer the Lua subset from this README alone. Call `rimbridge/get_lua_reference` over GABS to retrieve a machine-readable reference covering the supported Lua subset, the `rb.*` host API, the read-only `params` binding, file-backed execution, compile-error codes, inherited runtime model, and copyable examples.

Use `rimbridge/compile_lua` when you want to inspect the lowered JSON for inline source without executing anything. Use `rimbridge/compile_lua_file` for reusable `.lua` fixtures stored on disk. Those are the debugging seams for Lua lowering problems and the easiest way to confirm that Lua remains a front-end over the existing runner rather than a separate runtime.

Both inline and file-backed Lua tools can receive an object-style `parameters` argument. The bridge injects that object into the script as a top-level read-only `params` table. Use that for runtime values such as screenshot names, search radii, or scenario-specific limits instead of string templating the Lua source.

For waiting and synchronization, prefer a polling-first model. In `run_lua` v1 the intended pattern is: issue a mutating action once, then use `rb.poll(...)` against a read-only capability until a bounded structured condition matches. Good polling targets include `rimbridge/get_bridge_status`, `rimworld/list_colonists`, and `rimworld/get_designator_state`. This keeps scripts state-driven instead of relying on UI timing or host-side event delivery.

Use the explicit wait tools when the bridge already exposes a bounded lifecycle seam such as `rimbridge/wait_for_game_loaded`, `rimbridge/wait_for_long_event_idle`, or `rimbridge/wait_for_operation`. Those can still be called from Lua through `rb.call(...)`, but for gameplay state the general rule is simpler: mutate once, then poll the state you actually care about.

Supported `run_lua` v1 features:

- `local` variables and scoped shadowing
- table literals with array-style or object-style fields
- static field access such as `snapshot.result.count`
- static one-based index access such as `names[1]`
- arithmetic, comparisons, `and` / `or`, unary `not`, and unary minus
- `if` / `elseif` / `else`
- bounded `while`
- numeric `for`
- `for ... in ipairs(...)`
- `return`
- `print(...)` or `rb.print(...)`
- `rb.call(...)`, `rb.poll(...)`, `rb.assert(...)`, and `rb.fail(...)`
- read-only `params` table injected by `run_lua`, `compile_lua`, `run_lua_file`, and `compile_lua_file`

Not supported in v1:

- `require`
- metatables
- coroutines
- arbitrary global assignment
- dynamic table keys
- arbitrary dynamic indexing
- `break`

Lua example:

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

local snapshot = rb.call("rimworld/list_colonists", { currentMapOnly = true })

for i, colonist in ipairs(snapshot.result.colonists) do
  rb.print("colonist", { index = i, name = colonist.name, append = i > 1 })
end

rb.assert(snapshot.result.count > 0, "Expected at least one colonist.")
return snapshot.result.count
```

Planning example:

```lua
local searchRadius = 4
local planningAttempts = 0
local chosen = nil

while chosen == nil and planningAttempts < 6 do
  planningAttempts = planningAttempts + 1

  local candidate = rb.call("rimworld/find_random_cell_near", {
    x = 120,
    z = 120,
    startingSearchRadius = searchRadius,
    maxSearchRadius = searchRadius + 8,
    width = 3,
    height = 3,
    footprintAnchor = "center",
    requireWalkable = true,
    requireStandable = true,
    requireNoImpassableThings = true
  })

  if candidate.result.success == true then
    chosen = candidate
  end

  searchRadius = searchRadius + 2
end

rb.assert(chosen ~= nil, "Expected to find a candidate cell.")
rb.print("planning_attempts", planningAttempts)
return chosen.result.cell
```

File-backed fixture example:

```json
{
  "scriptPath": "/absolute/path/to/script-colonist-prison.lua",
  "parameters": {
    "screenshotFileName": "rimbridge_script_colonist_prison_demo"
  },
  "includeStepResults": true
}
```

Reference example:

```json
{
  "name": "value-passing",
  "continueOnError": false,
  "steps": [
    {
      "id": "discover",
      "call": "rimworld/list_architect_categories"
    },
    {
      "id": "structure_designators",
      "call": "rimworld/list_architect_designators",
      "arguments": {
        "categoryId": {
          "$ref": "discover",
          "path": "result.categories[0].id"
        }
      }
    }
  ]
}
```

Control-flow example:

```json
{
  "name": "bounded-loop",
  "continueOnError": false,
  "steps": [
    {
      "type": "let",
      "name": "count",
      "value": 0
    },
    {
      "type": "while",
      "maxIterations": 3,
      "condition": {
        "path": "vars.count",
        "lessThan": 2
      },
      "body": [
        {
          "type": "set",
          "name": "count",
          "value": {
            "$add": [
              { "$var": "count" },
              1
            ]
          }
        },
        {
          "id": "ping",
          "call": "rimbridge/ping"
        }
      ]
    }
  ]
}
```

The reference root also exposes step metadata such as `operationId`, `success`, `status`, `attempts`, `error`, and `warnings`, so later steps can reuse either result payloads or report metadata without a separate script runtime. The model still stays intentionally constrained: every capability execution goes through the normal registry-backed path, and even the control statements remain JSON data rather than a separate direct automation runtime.

`continueUntil` example:

```json
{
  "id": "wait_until_grouped",
  "call": "rimworld/list_colonists",
  "arguments": {
    "currentMapOnly": true
  },
  "continueUntil": {
    "timeoutMs": 10000,
    "pollIntervalMs": 100,
    "condition": {
      "all": [
        {
          "path": "result.colonists",
          "countEquals": 3
        },
        {
          "path": "result.colonists",
          "allItems": {
            "path": "position.x",
            "greaterThanOrEqual": 143,
            "lessThanOrEqual": 144
          }
        },
        {
          "path": "result.colonists",
          "allItems": {
            "path": "job",
            "in": ["Wait_Combat", "Wait_MaintainPosture"]
          }
        }
      ]
    }
  }
}
```

### Example workflow for a context-menu repro

1. Start or load a prepared colony.
2. Use `rimworld/list_colonists` to find the colonists involved in the repro.
3. Use `rimworld/select_pawn` and `rimworld/frame_pawns` to put the scene on screen.
4. Use `rimworld/open_context_menu` with `mode: vanilla` on the target pawn or cell.
5. Inspect the returned options or call `rimworld/take_screenshot` for visual proof.
6. Use `rimworld/save_game` to preserve the repro state for later development cycles.

## For developers

### How it works

RimBridgeServer uses a communication protocol called GABP (Game Agent Bridge Protocol). This is a standard way for programs to talk to games.

The basic steps are:
1. Connect to the server using TCP (a network connection type)
2. Say "hello" with your security token
3. Ask for a list of available commands
4. Send commands and receive responses
5. Optionally, listen for events from the game

Most mutating tools are marshalled onto RimWorld's main thread before they touch UI or map state. This is important for selection, camera, save/load, screenshot capture, and context-menu operations.

The new UI input helpers are intentionally semantic. `rimworld/press_accept`, `rimworld/press_cancel`, and `rimworld/close_window` drive RimWorld's own `WindowStack` APIs instead of foreground-dependent desktop input, which keeps them usable even when the game is running in the background during automated tests.

`rimworld/get_screen_targets` and the default `includeTargets: true` behavior on `rimworld/take_screenshot` expose a structured screen-space snapshot instead of forcing clients to infer UI geometry from logs or raw pixels. The current payload includes live window rects, selection, camera state, active float-menu option rects, and actionable target ids such as `dismissTargetId` and context-menu option `targetId` values. Automated screenshot calls also default to `suppressMessage: true`, which hides RimWorld's upper-left screenshot toast only for the duration of that tool-driven capture.

`rimworld/take_screenshot` also supports target-relative clipping through `clipTargetId` and `clipPadding`. Pass a target id returned by `rimworld/get_screen_targets` and the bridge will write a cropped screenshot artifact plus `clipRect` metadata, while still reporting the original full-frame `sourcePath` for traceability.

`rimworld/click_screen_target` is the first background-safe click seam on top of that metadata. It does not simulate desktop mouse input; instead it resolves known target ids in-process and dispatches the correct direct RimWorld action. The first supported targets are dismissible windows and context-menu options.

For test automation, prefer the explicit wait tools over blind sleeps. `rimbridge/wait_for_game_loaded`, `rimbridge/wait_for_long_event_idle`, and `rimbridge/wait_for_operation` provide bounded waits with state snapshots so scripts can move quickly when the game is ready and fail with useful diagnostics when it is not.

`rimbridge/wait_for_game_loaded` now distinguishes between "playable" and "automation-ready". By default it waits for RimWorld's screen fade to clear, and callers can also request `pauseIfNeeded: true` so the game is reliably paused before screenshots or other deterministic assertions are taken.

`rimbridge/get_bridge_status` also returns `latestLogSequence` and `latestOperationEventSequence`. Tests can snapshot those cursors before a command, execute the command, then call `rimbridge/list_logs` or `rimbridge/list_operation_events` with `afterSequence` to fetch only the new logs and events from that window.

If your host supports unsolicited GABP events, RimBridgeServer also publishes filtered event channels:
- **`rimbridge.operation`** - Terminal non-diagnostic operation events
- **`rimbridge.log`** - Deduplicated warning/error/fatal log entries

The push path is intentionally narrow to avoid context spam. Full detail remains available through the pull tools and journals.

### Live smoke harness

The repository now includes a reproducible live smoke runner in [`Tests/RimBridgeServer.LiveSmoke`](Tests/RimBridgeServer.LiveSmoke) and a thin wrapper script at [`scripts/live-smoke.sh`](scripts/live-smoke.sh).

Example:

```bash
scripts/live-smoke.sh --scenario debug-game-load --game-id rimworld --stop-after
```

The `debug-game-load` scenario drives GABS and RimBridgeServer end-to-end:

- checks the game status and starts RimWorld if needed
- connects through GABS
- waits for long events to go idle
- snapshots `latestLogSequence` and `latestOperationEventSequence`
- starts RimWorld's debug quick-test colony
- waits for the resulting operation and automation-ready game state, including the post-load screen fade
- pauses the game if it is still running once that ready state is reached
- verifies that colonists are present on the map
- collects the log and operation-event window for that action

The `selection-roundtrip` scenario reuses the same harness primitives against a loaded game:

- ensures a playable game exists, creating a debug colony when needed
- starts a fresh observation window after the game is ready
- lists current-map colonists and selects a real pawn
- jumps the camera to that pawn, reads camera state, and clears the selection again
- captures only the operation and log window for that interaction block

The `context-menu-cancel-roundtrip` scenario exercises the first background-safe input path:

- ensures a playable game exists and normalizes away any pre-existing dialog windows
- selects a real colonist and opens a vanilla context menu through `rimworld/open_context_menu`
- captures `rimworld/get_screen_targets` while the menu is open and verifies the reported option geometry
- closes that menu again with `rimworld/press_cancel`
- verifies the float-menu and window-stack state transitions without depending on OS focus

The `screen-target-click-roundtrip` scenario exercises the first background-safe click path:

- ensures a playable game exists and normalizes away stray dialog windows
- opens a real context menu and reads `rimworld/get_screen_targets`
- clicks the float-menu dismiss target id through `rimworld/click_screen_target`
- reopens the menu, clicks a direct option target id, and verifies the reported target/action metadata

The `screen-target-clip` scenario exercises target-relative screenshot clipping:

- ensures a playable game exists and opens a real context menu with an executable option
- reads a real option `targetId` and `rect` from `rimworld/get_screen_targets`
- captures a screenshot clipped to that option target with padding
- verifies that the clipped PNG dimensions match the reported `clipRect`
- keeps the original full-frame screenshot as `sourcePath` for comparison and debugging

The `save-load-roundtrip` scenario covers the real save/load lifecycle:

- ensures a playable game exists
- writes a stable live-smoke save through `rimworld/save_game`
- verifies that the save appears in `rimworld/list_saves`
- loads the same save again and waits for playable state
- confirms the reloaded colony still exposes current-map colonists

The `screenshot-capture` scenario covers the current screenshot path:

- ensures a playable game exists
- jumps the camera to a real colonist for deterministic framing
- captures a screenshot with a run-specific file name
- verifies that the reported screenshot path and file size are valid
- verifies that the screenshot response includes the current screen-target snapshot
- captures only the screenshot action's operation and log window

The `architect-wall-placement` scenario covers the first Architect/designator seam:

- ensures a playable game exists
- discovers the `Structure` category and the `Wall` build designator by stable ids
- disables god mode and verifies that placing a wall creates a `Blueprint_Build` for `Wall`
- enables god mode and verifies that placing the same wall creates the solid `Wall` building directly
- restores the original god-mode state after the scenario finishes

The `architect-floor-dropdown` scenario covers dropdown-heavy Architect discovery and rectangle placement:

- ensures a playable game exists
- discovers the `Floors` category and resolves a real dropdown child designator by stable id
- proves the selected child designator and its dropdown container are both tracked correctly
- enables god mode and applies the dropdown child floor designator over a 2x2 rectangle
- verifies all four cells changed to the expected floor terrain through `rimworld/get_cell_info`

The `architect-zone-area-drag` scenario covers rectangle drag semantics for non-build tools:

- ensures a playable game exists
- discovers the `Zone` category and resolves stockpile-zone plus home-area designators by class-backed stable ids
- creates a new 3x2 stockpile zone and verifies it through both `rimworld/list_zones` and `rimworld/get_cell_info`
- expands the home area over a 2x2 rectangle and verifies the change through both `rimworld/list_areas` and `rimworld/get_cell_info`
- exports human-checkable screenshots for both overlays when `--human-verify` is enabled

The `architect-stateful-targeting` scenario covers the remaining mutable Architect state:

- ensures a playable game exists
- creates a new custom allowed area and verifies `rimworld/get_designator_state` surfaces it as the selected allowed-area target
- applies `Expand allowed area` over a 2x2 rectangle and verifies the created area id through both `rimworld/list_areas` and `rimworld/get_cell_info`
- creates a stockpile zone, pins the stockpile designator to that zone with `rimworld/set_zone_target`, then expands the same zone into a second rectangle without increasing the zone count
- tears the fixture back down with `rimworld/delete_zone`, `rimworld/clear_area`, and `rimworld/delete_area`

The `debug-action-pawn-target` scenario covers the first targeted debug-action slice:

- ensures a playable game exists
- discovers `Actions\\T: Toggle Job Logging` and `Actions\\T: Log Job Details` through the same stable debug-action tree as other actions
- verifies the discovery payload reports `requiredTargetKind: pawn`
- executes both actions against a real colonist by `pawnName`
- verifies `T: Log Job Details` emits a captured log row containing the pawn's current job details

The `script-wall-sequence` scenario covers the first script-runner slice:

- ensures a playable game exists
- discovers the `Wall` designator through the normal Architect metadata flow
- precomputes two accepted wall cells, then runs one JSON script that enables god mode, places both walls, and captures a screenshot
- verifies the script report shape, child step execution order, and the on-disk screenshot artifact
- confirms both target cells contain directly built `Wall` things instead of blueprints

The `script-colonist-prison` scenario is the stronger control-flow smoke for the same runner:

- ensures a playable game exists
- runs one Lua script that resolves the `Wall` designator through the normal Architect metadata flow and finds a rally cell through generic map-analysis tools
- uses `rimworld/find_random_cell_near`, architect dry runs, `rb.print`, `rb.assert`, `rb.poll`, `ipairs`, arithmetic, and the direct `rimworld/right_click_cell` shortcut instead of the older menu-based move path
- verifies that the script report includes the planning calls as real executed steps rather than harness prework
- confirms the resulting perimeter contains real `Wall` things, the starting colonists are undrafted, and they remain inside the prison interior
- verifies the script-produced screenshot artifact

The console output stays concise, while the full structured report is written to `artifacts/live-smoke/<timestamp>_<scenario>.json`. By default, `--stop-after` only stops RimWorld if the harness started that instance itself, so it does not kill a user-managed session by surprise.

The runner looks for `gabs` on `PATH`, honors `GABS_BIN`, and also auto-detects a sibling checkout at `../GABS/gabs`. Use `--config-dir` or `GABS_CONFIG_DIR` if you need to point it at a non-default GABS configuration directory.

The shared observation-window helper is intentionally generic: scenarios can start a bounded cursor window immediately before the action under test, choose their own event/log filters, and then collect the exact delta afterward without introducing global logging modes or ad hoc sleeps.

Use `scripts/live-smoke.sh --list-scenarios` to see the current scenario matrix and descriptions.

For human-checkable runs, pass `--human-verify`. The harness will copy a curated set of `.png` screenshots plus same-name `.txt` notes to the Desktop by default so you can inspect the exact state each scenario verified. Use `--human-verify-dir <path>` if you want those artifacts somewhere else.

The helper script `scripts/human-verify.sh` runs the most visually useful scenarios in sequence with `--human-verify --stop-after`:

```bash
scripts/human-verify.sh
```

The current human-verification set covers:

- `save-load-roundtrip` after the colony has reloaded
- `context-menu-cancel-roundtrip` with the menu open and after semantic cancel input
- `screen-target-click-roundtrip` before dismiss, before option click, and after the option click
- `screen-target-clip` with a cropped image focused on one real actionable target
- `screenshot-capture` by exporting the captured screenshot itself with a matching explanation file
- `architect-floor-dropdown` with one screenshot showing a directly placed dropdown-selected floor patch
- `script-colonist-prison` with the screenshot artifact produced by the script after grouping, enclosing, and undrafting the starting colonists
- `script-wall-sequence` with the screenshot artifact produced by the script itself after placing two direct-build walls
- `architect-stateful-targeting` with one screenshot showing a custom allowed area overlay and one showing an explicitly targeted stockpile expansion
- `architect-wall-placement` with one screenshot showing the blueprint wall state and one showing the direct-build wall state
- `architect-zone-area-drag` with one screenshot showing a stockpile zone overlay and one showing a home-area overlay

The context-menu scenarios intentionally avoid creating empty float menus during target probing. Earlier harness revisions could emit RimWorld's red `Created FloatMenu with no options. Closing.` error while searching for a valid menu target; that probing path now checks for zero options before constructing the menu.

For complete details about the protocol, see the [GABP specification](https://github.com/pardeike/GABP).

### Building the mod

Requirements:
- .NET SDK
- RimWorld installed

Steps:
1. Clone this repository
2. Run `dotnet build` in the main folder
3. The built mod will be in the `1.6/Assemblies/` folder

**Tip**: Set the `RIMWORLD_MOD_DIR` environment variable to automatically copy the built mod to your RimWorld Mods folder.

### Project structure

```
About/          - Mod information for RimWorld
1.6/Assemblies/ - Built mod files
Source/         - Source code
Tests/          - Unit tests and live smoke runner
scripts/        - Developer-facing workflow helpers
lib/            - External libraries
.vscode/        - Visual Studio Code settings
```

## License

This project uses the MIT License. See the `LICENSE` file for details.

## Dependencies

This mod includes [Lib.GAB](https://github.com/pardeike/Lib.GAB), which provides the GABP protocol implementation.
