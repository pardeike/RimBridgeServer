# RimBridgeServer

RimBridgeServer is a RimWorld mod that turns the running game into a bridge an external tool can use.

In practice, that means a test harness, developer tool, or AI agent can start RimWorld, inspect live game state, drive built-in UI, manage mods and mod settings, execute debug actions, take targeted screenshots, and verify behavior against the real game instead of a simulation.

This project is aimed at automated mod development and testing:

- integration testing against a real RimWorld session
- UI and UX verification through semantic dialogs, targets, and screenshots
- faster repro loops for debug actions, saves, settings, and load-order changes
- AI-assisted mod development through GABS or direct bridge connections

Architecture and design notes live in [docs/architecture.md](docs/architecture.md), [docs/lua-frontend-design.md](docs/lua-frontend-design.md), and [docs/semantic-state-design.md](docs/semantic-state-design.md).

## What It Does

RimBridgeServer runs inside RimWorld and exposes a tool surface for:

- reading game, map, camera, selection, notification, and UI state
- driving high-level game actions such as debug actions, main tabs, Architect designators, and selection changes
- changing mod settings and mod load order
- capturing screenshots, including clipped screenshots for known UI or screen targets
- running small automation scripts through JSON or Lua front-ends

It is designed to stay as close as possible to RimWorld's own logical seams instead of reimplementing gameplay logic outside the game.

## Installation

### Recommended: Use GABS

GABS is the best way to use RimBridgeServer from an AI or automation harness.

Why this is the recommended mode:

- GABS can start and stop RimWorld for you
- GABS can discover the live bridge tool surface after the game connects
- you do not need to manage ports or tokens manually
- this is the cleanest setup for autonomous mod testing

Basic setup:

1. Install RimBridgeServer into your RimWorld `Mods` folder.
2. Enable `RimBridgeServer` in RimWorld's mod list.
3. Install and configure [GABS](https://github.com/pardeike/GABS).
4. Start RimWorld through GABS and connect to the running game.

Once RimWorld is up, GABS exposes the game-management tools (`games.start`, `games.connect`, `games.call_tool`) and then the live RimBridgeServer tool surface behind them.

## RimWorld Mod Debugging Stack

If you want the same stack used for real mod repro-and-fix sessions, use:

1. `Harmony` plus the mod you are testing
2. `RimBridgeServer` inside RimWorld for live game inspection and control
3. [GABS](https://github.com/pardeike/GABS) to launch RimWorld and surface the live tools to your AI client
4. [DecompilerServer](https://github.com/pardeike/DecompilerServer) to inspect `Assembly-CSharp.dll` and related managed code while debugging

There is a short setup order here: [docs/rimworld-mod-debugging-stack.md](docs/rimworld-mod-debugging-stack.md).

### Direct Mode

Direct mode still works if you do not want to use GABS.

Basic setup:

1. Install RimBridgeServer into your RimWorld `Mods` folder.
2. Enable `RimBridgeServer` in RimWorld's mod list.
3. Start RimWorld normally.
4. Read the RimWorld log for the bridge startup lines.

In direct mode, RimBridgeServer logs lines like:

```text
[RimBridge] GABP server running standalone on port 5174
[RimBridge] Bridge token: abc123...
```

Your client then connects to:

- address: `127.0.0.1`
- port: the logged bridge port
- token: the logged bridge token

## Beginner Start

If you only need the shortest possible mental model, use this:

1. Install the mod and enable it in RimWorld.
2. Prefer GABS if you want AI or harness control.
3. Start RimWorld.
4. Wait until the bridge is connected.
5. Use tools like `rimbridge/get_bridge_status`, `rimworld/start_debug_game`, `rimworld/get_ui_layout`, `rimworld/take_screenshot`, `rimworld/list_mods`, and `rimworld/update_mod_settings` to drive and validate the game.

## Third-Party Extension Tools

Third-party mods can expose bridge tools by referencing the `RimBridgeServer.Annotations` NuGet package and annotating ordinary public methods. RimBridgeServer delays its own GAB startup until RimWorld has finished initializing all loaded mods, then scans every loaded mod assembly exactly once, registers all discovered annotated tools exactly once, and exposes them through the same capability registry and top-level GAB tool surface as built-in tools.

Practical rules:

- use `RimBridgeServer.Annotations` as the only shared dependency
- annotate public static methods, public instance methods on your `Verse.Mod` class, or public instance methods on a type with a public parameterless constructor
- use `[ToolParameter]` for argument docs and `[ToolResponse]` for response field docs when useful
- expect per-mod fault isolation: one broken mod should not block discovery for other mods

Minimal example:

```csharp
using RimBridgeServer.Annotations;

public sealed class MyModBridgeTools
{
    [Tool("mymod/ping", Description = "Example tool exposed through RimBridgeServer")]
    public object Ping(
        [ToolParameter(Description = "Optional label")] string label = null)
    {
        return new
        {
            success = true,
            label = label ?? "pong"
        };
    }
}
```

## Tool Surface

The current public tool surface is grouped below by function.
For the generated parameter-level reference pulled straight from the annotated source and kept fresh by CI, see [docs/tool-reference.md](docs/tool-reference.md).

Lua authoring note: `rimbridge/run_lua` is intentionally a lowered Lua subset, not general-purpose Lua. Start with `rimbridge/get_lua_reference` and `rimbridge/compile_lua`; prefer `local` bindings, `rb.call`/`rb.poll`, static field access, and static one-based indexes such as `names[1]`. Dynamic indexing such as `names[i]`, arbitrary global assignment, and most broader Lua features are rejected in v1.

<!-- BEGIN GENERATED:tool-surface -->

### Bridge Diagnostics

- `rimbridge/ping` - Connectivity test. Returns 'pong'.
- `rimworld/get_game_info` - Get basic information about the current RimWorld game
- `rimbridge/get_operation` - Get the latest retained journal snapshot for a specific operation id, including any bounded retained result payload
- `rimbridge/get_bridge_status` - Get the current bridge and RimWorld state snapshot without mutating game state
- `rimbridge/list_capabilities` - List registered bridge capabilities so an agent can discover the live bridge surface instead of relying on hardcoded tool knowledge
- `rimbridge/get_capability` - Get one registered bridge capability descriptor by capability id or alias
- `rimbridge/list_operations` - List recent bridge operations from the in-memory operation journal, optionally expanding retained result payloads
- `rimbridge/list_operation_events` - List recent bridge operation lifecycle events from the in-memory event journal
- `rimbridge/list_logs` - List recent captured RimWorld and bridge log entries from the in-memory log journal, including operation correlation when available
- `rimbridge/wait_for_operation` - Wait for an operation in the journal to reach a terminal status
- `rimbridge/wait_for_game_loaded` - Wait until RimWorld has finished loading a playable game and, optionally, until screen fade is complete
- `rimbridge/wait_for_long_event_idle` - Wait until RimWorld reports no long events in progress

### Scripting

- `rimbridge/get_script_reference` - Get a machine-readable authoring reference for `rimbridge/run_script`, including statement types, expressions, conditions, limits, and examples
- `rimbridge/get_lua_reference` - Get a machine-readable authoring reference for the lowered `rimbridge/run_lua` subset, including quick-start rules, common pitfalls, `params` binding, compile errors, limits, and examples
- `rimbridge/run_script` - Execute a JSON script containing ordered capability calls and generic control statements; call `rimbridge/get_script_reference` for the machine-readable language reference
- `rimbridge/run_lua` - Compile a small lowered Lua subset, not general-purpose Lua, into the shared script runner and execute it through the normal capability registry; start with `rimbridge/get_lua_reference` or `rimbridge/compile_lua`
- `rimbridge/run_lua_file` - Load a `.lua` file, treat it as the same lowered Lua subset used by `rimbridge/run_lua`, and execute it through the shared script runner
- `rimbridge/compile_lua` - Compile the supported lowered Lua subset, not general-purpose Lua, into the JSON script model without executing capability calls; use this first for new script shapes
- `rimbridge/compile_lua_file` - Load a `.lua` file and compile it as the same lowered Lua subset used by `rimbridge/run_lua` without executing capability calls

### Debug Actions And Mods

- `rimworld/pause_game` - Pause or unpause the game
- `rimworld/set_time_speed` - Set RimWorld's current time speed directly
- `rimworld/list_debug_action_roots` - List top-level RimWorld debug action roots using stable internal debug-action paths
- `rimworld/list_debug_action_children` - List direct children of a RimWorld debug action path
- `rimworld/search_debug_actions` - Search the full RimWorld debug-action tree globally by path, label, category, and source metadata so callers do not need to walk one subtree at a time
- `rimworld/get_debug_action` - Get metadata for one RimWorld debug action path and, optionally, its immediate children
- `rimworld/execute_debug_action` - Execute a supported RimWorld debug action leaf by stable path, including pawn-target actions when pawnName or pawnId is provided
- `rimworld/set_debug_setting` - Set a RimWorld debug setting toggle by stable path to a deterministic on/off state
- `rimworld/set_colonist_job_logging` - Deterministically enable or disable job-tracker logging for one current-map colonist and return a log cursor plus recommended `rimbridge/list_logs` arguments for consuming future job lines
- `rimworld/list_mods` - List installed RimWorld mods, whether each one is enabled in the current configuration, and whether it matches the currently loaded session
- `rimworld/get_mod_configuration_status` - Read semantic mod-configuration status for the current active load order, including warnings, ordering issues, and whether a restart is required to match the loaded session
- `rimworld/set_mod_enabled` - Enable or disable one installed mod by stable mod id, package id, name, or folder name, optionally persisting the updated `ModsConfig.xml` immediately
- `rimworld/reorder_mod` - Move one currently enabled mod to a new zero-based active load-order index, optionally persisting the updated `ModsConfig.xml` immediately
- `rimworld/list_mod_settings_surfaces` - List loaded mod handles that expose a settings dialog, a persistent `ModSettings` state object, or both
- `rimworld/get_mod_settings` - Read one loaded mod's semantic `ModSettings` object by stable mod id, package id, settings category, or handle type name
- `rimworld/update_mod_settings` - Apply one or more field-path updates to a loaded mod's `ModSettings` object, with optional immediate persistence through `Mod.WriteSettings()`
- `rimworld/reload_mod_settings` - Reload a loaded mod's `ModSettings` object from disk, discarding unsaved in-memory changes
- `rimworld/open_mod_settings` - Open RimWorld's native `Dialog_ModSettings` window for one loaded mod without foreground-dependent input

### Architect And Map State

- `rimworld/get_designator_state` - Get the current Architect/designator selection state, including god mode and the selected designator
- `rimworld/set_god_mode` - Enable or disable RimWorld god mode deterministically
- `rimworld/list_architect_categories` - List RimWorld Architect categories using stable category ids
- `rimworld/list_architect_designators` - List Architect designators for one category, flattening dropdown widgets into actionable child designators
- `rimworld/select_architect_designator` - Select an Architect designator by stable id without relying on foreground UI interaction
- `rimworld/apply_architect_designator` - Apply an Architect designator to one cell or a rectangle, with optional dry-run validation
- `rimworld/list_zones` - List current-map zones such as stockpiles and growing zones
- `rimworld/list_areas` - List current-map areas such as home, roof, snow-clear, and allowed areas
- `rimworld/create_allowed_area` - Create a new allowed area and optionally make it the selected allowed-area target
- `rimworld/select_allowed_area` - Select an allowed area by id for area-designator flows, or clear the selection when areaId is omitted
- `rimworld/set_zone_target` - Set or clear the explicit existing-zone target on a zone-add designator
- `rimworld/clear_area` - Clear all cells from a mutable area such as a custom allowed area
- `rimworld/delete_area` - Delete a mutable area such as a custom allowed area
- `rimworld/delete_zone` - Delete an existing zone by id
- `rimworld/get_cell_info` - Inspect one map cell, including things, blueprints, frames, designations, zones, and areas
- `rimworld/get_cells_info` - Inspect every map cell in a rectangle up to 1024 cells, including things, blueprints, frames, designations, zones, and areas
- `rimworld/find_random_cell_near` - Use RimWorld's expanding-radius random cell search to find a nearby cell or footprint that satisfies generic map criteria
- `rimworld/flood_fill_cells` - Analyze a contiguous area from one root cell using RimWorld's generic cell flood-fill algorithm and the same reusable footprint criteria as find_random_cell_near

### UI And Input

- `rimworld/get_ui_state` - Get the current RimWorld window stack and input state for background-safe UI automation
- `rimworld/list_main_tabs` - List RimWorld main tabs such as Work, Assign, Research, and mod-provided tabs with stable `main-tab` target ids
- `rimworld/open_main_tab` - Open one RimWorld main tab by stable target id, `defName`, label, or tab window type
- `rimworld/close_main_tab` - Close the currently open RimWorld main tab, optionally asserting which tab is open first
- `rimworld/get_ui_layout` - Capture a generic structured layout snapshot of the current dialogs, windows, or main tabs, including actionable control target ids
- `rimworld/click_ui_target` - Activate an actionable UI control target returned by `rimworld/get_ui_layout` on the next real draw frame
- `rimworld/set_hover_target` - Set a persistent virtual hover target for UI review and screenshots, using either an actionable `ui-element` target id or a current-map cell, pawn, or thing
- `rimworld/clear_hover_target` - Clear the current virtual hover target so screenshots and mouseover-driven UI return to the real cursor state
- `rimworld/press_accept` - Send semantic accept input to the active RimWorld window stack without requiring OS focus
- `rimworld/list_languages` - List installed RimWorld languages, including a recommended ASCII-safe switch query for each language and the currently active language
- `rimworld/press_cancel` - Send semantic cancel input to the active RimWorld window stack without requiring OS focus
- `rimworld/close_window` - Close an open RimWorld window by type name or, if omitted, the topmost window
- `rimworld/open_window_by_type` - Open a RimWorld window by short or full .NET type name when the window exposes a public parameterless constructor
- `rimworld/click_screen_target` - Semantically click a known actionable target id returned by `rimworld/get_screen_targets` without requiring OS focus
- `rimworld/switch_language` - Switch RimWorld to an installed language by the recommendedQuery from `rimworld/list_languages` or another exact language name match, mirroring the main-menu language picker and saving prefs
- `rimworld/start_debug_game` - Start RimWorld's built-in quick test colony from the main menu
- `rimworld/go_to_main_menu` - Return to the RimWorld main menu entry scene, or no-op if already there

### Selection And Colony State

- `rimworld/list_colonists` - List player-controlled colonists available for selection and drafting, including stable pawn ids
- `rimworld/clear_selection` - Clear the current map selection
- `rimworld/select_pawn` - Select a single colonist by name or stable pawn id
- `rimworld/deselect_pawn` - Deselect a single selected pawn by name or stable pawn id
- `rimworld/set_draft` - Draft or undraft a colonist by name or stable pawn id
- `rimworld/get_selected_pawn_inventory_state` - Read the selected pawn's carried thing and inventory contents, including Pick Up And Haul tracked items when available

### Selection Semantics And Notifications

- `rimworld/get_selection_semantics` - Get structured details for the current selection, including inspect strings, inspect-tab types, and the current selection fingerprint
- `rimworld/list_selected_gizmos` - List the current selection's actionable grouped gizmos using deterministic selection-scoped gizmo ids
- `rimworld/execute_gizmo` - Execute one grouped gizmo for the current selection by gizmo id returned from `rimworld/list_selected_gizmos`
- `rimworld/list_messages` - List live RimWorld messages with native message ids and structured look-target metadata
- `rimworld/list_letters` - List current letter-stack entries with native letter ids, semantic letter content, and structured look-target metadata
- `rimworld/open_letter` - Open a specific letter by native letter id, mirroring a normal left-click on the letter stack entry
- `rimworld/dismiss_letter` - Dismiss a specific dismissible letter by native letter id, mirroring a normal right-click on the letter stack entry
- `rimworld/list_alerts` - List active RimWorld alerts with structured culprit targets and alert-snapshot-scoped alert ids
- `rimworld/activate_alert` - Activate one alert by alert id returned from `rimworld/list_alerts`, mirroring a normal left-click on the alert readout

### Camera And Screenshots

- `rimworld/get_camera_state` - Get the current map camera position, zoom, and visible rect
- `rimworld/get_screen_targets` - Get current screen-space targets such as open windows and active context-menu geometry
- `rimworld/get_map_target_info` - Resolve a current-map pawn or thing to its map position and occupied cell rectangle
- `rimworld/jump_camera_to_pawn` - Jump the camera to a pawn by name or stable pawn id
- `rimworld/jump_camera_to_cell` - Jump the camera to a map cell
- `rimworld/move_camera` - Move the camera by a cell offset
- `rimworld/zoom_camera` - Adjust the current camera zoom/root size
- `rimworld/set_camera_zoom` - Set the current camera root size directly
- `rimworld/frame_pawns` - Frame a comma-separated list of pawns by name and/or stable pawn id so they fit in view
- `rimworld/screenshot_cell_rect` - Frame a map-cell rectangle, capture the tightest visible screenshot, and restore the prior camera by default, or leave the framed camera in place when requested
- `rimworld/take_screenshot` - Take an in-game screenshot and return the saved file path plus optional target metadata

### Save/Load And Spawning

- `rimworld/list_saves` - List saved RimWorld games
- `rimworld/spawn_thing` - Spawn a thing on the current map at a target cell
- `rimworld/save_game` - Save the current game to a named save
- `rimworld/load_game` - Load a named RimWorld save

### Context Menus And Map Interaction

- `rimworld/open_context_menu` - Dispatch a live map click at a target pawn or cell and capture the resulting context menu when one remains open
- `rimworld/right_click_cell` - Dispatch a live map click interaction for the current pawn selection so vanilla and modded handlers see the same input path as a real click
- `rimworld/get_context_menu_options` - Get the currently opened debug context menu options
- `rimworld/execute_context_menu_option` - Execute a context menu option by index or label
- `rimworld/close_context_menu` - Close the currently opened debug context menu

<!-- END GENERATED:tool-surface -->

## More Reading

- [docs/architecture.md](docs/architecture.md) - implementation strategy and architectural direction
- [docs/lua-frontend-design.md](docs/lua-frontend-design.md) - Lua front-end design and current execution model
- [docs/semantic-state-design.md](docs/semantic-state-design.md) - rationale for the semantic inspection and notification surfaces
- [docs/tool-reference.md](docs/tool-reference.md) - generated per-tool reference derived from the `[Tool]` and `[ToolParameter]` annotations in source
