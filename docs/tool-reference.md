# Tool Reference

> Generated from [Source/RimBridgeTools.cs](../Source/RimBridgeTools.cs) by [`scripts/generate-tool-reference.sh`](../scripts/generate-tool-reference.sh). Do not edit by hand.

This is the full annotation-driven tool reference. The main README stays beginner-friendly; this page is meant to be the authoritative per-tool summary for harnesses, skills, and humans who need the exact surface.

## Summary

- `105` tools total
- `18` `rimbridge/*` tools
- `87` `rimworld/*` tools

## `rimbridge/*`

### `rimbridge/ping`

Connectivity test. Returns 'pong'.

Parameters: none.

### `rimbridge/get_operation`

Get the latest retained journal snapshot for a specific operation id, including any bounded retained result payload

Parameters:
- `operationId` (`string`, `required`): Operation id returned in tool metadata

### `rimbridge/get_bridge_status`

Get the current bridge and RimWorld state snapshot without mutating game state

Parameters: none.

### `rimbridge/list_capabilities`

List registered bridge capabilities so an agent can discover the live bridge surface instead of relying on hardcoded tool knowledge

Parameters:
- `limit` (`int`, `optional`, default `200`): Maximum number of capabilities to return
- `providerId` (`string`, `optional`, default `null`): Optional exact provider id filter
- `category` (`string`, `optional`, default `null`): Optional category filter
- `source` (`string`, `optional`, default `null`): Optional source filter: core, optional, or extension
- `query` (`string`, `optional`, default `null`): Optional case-insensitive free-text query across ids, aliases, titles, summaries, and parameter names
- `includeParameters` (`bool`, `optional`, default `true`): Include parameter descriptors for each capability

### `rimbridge/get_capability`

Get one registered bridge capability descriptor by capability id or alias

Parameters:
- `capabilityIdOrAlias` (`string`, `required`): Capability id or alias such as rimworld/select_pawn

### `rimbridge/list_operations`

List recent bridge operations from the in-memory operation journal, optionally expanding retained result payloads

Parameters:
- `limit` (`int`, `optional`, default `20`): Maximum number of operations to return
- `includeResults` (`bool`, `optional`, default `false`): Include bounded retained result payloads instead of summary-only operation envelopes

### `rimbridge/list_operation_events`

List recent bridge operation lifecycle events from the in-memory event journal

Parameters:
- `limit` (`int`, `optional`, default `50`): Maximum number of events to return
- `eventType` (`string`, `optional`, default `null`): Optional event type filter, such as operation.failed
- `afterSequence` (`long`, `optional`, default `0`): Only include events with a sequence greater than this cursor
- `operationId` (`string`, `optional`, default `null`): Optional exact operation id filter
- `includeDiagnostics` (`bool`, `optional`, default `false`): Include diagnostic bridge operations such as status and journal reads

### `rimbridge/list_logs`

List recent captured RimWorld and bridge log entries from the in-memory log journal, including operation correlation when available

Parameters:
- `limit` (`int`, `optional`, default `50`): Maximum number of log entries to return
- `minimumLevel` (`string`, `optional`, default `"info"`): Minimum level to include: info, warning, error, or fatal
- `afterSequence` (`long`, `optional`, default `0`): Only include log entries with a sequence greater than this cursor
- `operationId` (`string`, `optional`, default `null`): Optional direct operation id filter
- `rootOperationId` (`string`, `optional`, default `null`): Optional root operation id filter for grouped script runs or nested flows
- `capabilityId` (`string`, `optional`, default `null`): Optional exact capability id filter

### `rimbridge/wait_for_operation`

Wait for an operation in the journal to reach a terminal status

Parameters:
- `operationId` (`string`, `required`): Operation id returned in tool metadata
- `timeoutMs` (`int`, `optional`, default `10000`): Maximum time to wait in milliseconds
- `pollIntervalMs` (`int`, `optional`, default `50`): Polling interval in milliseconds

### `rimbridge/wait_for_game_loaded`

Wait until RimWorld has finished loading a playable game and, optionally, until screen fade is complete

Parameters:
- `timeoutMs` (`int`, `optional`, default `30000`): Maximum time to wait in milliseconds
- `pollIntervalMs` (`int`, `optional`, default `100`): Polling interval in milliseconds
- `waitForScreenFade` (`bool`, `optional`, default `true`): Wait until RimWorld's screen fade has fully cleared
- `pauseIfNeeded` (`bool`, `optional`, default `false`): Pause the game before returning success if it is still running

### `rimbridge/wait_for_long_event_idle`

Wait until RimWorld reports no long events in progress

Parameters:
- `timeoutMs` (`int`, `optional`, default `30000`): Maximum time to wait in milliseconds
- `pollIntervalMs` (`int`, `optional`, default `100`): Polling interval in milliseconds

### `rimbridge/get_script_reference`

Get a machine-readable authoring reference for rimbridge/run_script, including statement types, expressions, conditions, limits, and examples

Parameters: none.

### `rimbridge/get_lua_reference`

Get a machine-readable authoring reference for the lowered rimbridge/run_lua subset, including quick-start rules, common pitfalls, params binding, compile errors, limits, and examples

Parameters: none.

### `rimbridge/run_script`

Execute a JSON script containing ordered capability calls and generic control statements; call rimbridge/get_script_reference for the machine-readable language reference

Parameters:
- `scriptJson` (`string`, `required`): Structured JSON script. Call rimbridge/get_script_reference for the full machine-readable language reference. Example: {"name":"setup","continueOnError":false,"steps":[{"id":"pause","call":"rimworld/pause_game","arguments":{"pause":true}}]}
- `includeStepResults` (`bool`, `optional`, default `true`): Include each step's result payload in the returned script report

### `rimbridge/run_lua`

Compile a small lowered Lua subset, not general-purpose Lua, into the shared script runner and execute it through the normal capability registry; start with rimbridge/get_lua_reference or rimbridge/compile_lua

Parameters:
- `luaSource` (`string`, `required`): Lua source using the supported rimbridge/run_lua subset. Start with local bindings, rb.call/rb.poll, static field access, and static one-based indexes such as names[1]. Call rimbridge/get_lua_reference for the full machine-readable language reference and rimbridge/compile_lua to inspect the lowered JSON script.
- `parameters` (`Dictionary<string, object>`, `optional`, default `null`): Optional object-style parameters exposed to the script as a read-only global params table. Example: {"screenshotFileName":"demo_capture","maxPlanningAttempts":8}
- `includeStepResults` (`bool`, `optional`, default `true`): Include each successful call step's result payload in the returned script report

### `rimbridge/run_lua_file`

Load a .lua file, treat it as the same lowered Lua subset used by rimbridge/run_lua, and execute it through the shared script runner

Parameters:
- `scriptPath` (`string`, `required`): Absolute path or current-working-directory-relative path to a .lua file
- `parameters` (`Dictionary<string, object>`, `optional`, default `null`): Optional object-style parameters exposed to the script as a read-only global params table
- `includeStepResults` (`bool`, `optional`, default `true`): Include each successful call step's result payload in the returned script report

### `rimbridge/compile_lua`

Compile the supported lowered Lua subset, not general-purpose Lua, into the JSON script model without executing capability calls; use this first for new script shapes

Parameters:
- `luaSource` (`string`, `required`): Lua source using the supported rimbridge/run_lua subset. Prefer local bindings, rb.call/rb.poll, static field access, and static one-based indexes. Call rimbridge/get_lua_reference for the full machine-readable language reference.
- `parameters` (`Dictionary<string, object>`, `optional`, default `null`): Optional object-style parameters exposed to the script as a read-only global params table

### `rimbridge/compile_lua_file`

Load a .lua file and compile it as the same lowered Lua subset used by rimbridge/run_lua without executing capability calls

Parameters:
- `scriptPath` (`string`, `required`): Absolute path or current-working-directory-relative path to a .lua file
- `parameters` (`Dictionary<string, object>`, `optional`, default `null`): Optional object-style parameters exposed to the script as a read-only global params table

## `rimworld/*`

### `rimworld/get_game_info`

Get basic information about the current RimWorld game

Parameters: none.

### `rimworld/pause_game`

Pause or unpause the game

Parameters:
- `pause` (`bool`, `optional`, default `true`): True to pause, false to unpause

### `rimworld/set_time_speed`

Set RimWorld's current time speed directly

Parameters:
- `speed` (`string`, `optional`, default `"Normal"`): Desired time speed: Paused, Normal, Fast, Superfast, or Ultrafast

### `rimworld/list_debug_action_roots`

List top-level RimWorld debug action roots using stable internal debug-action paths

Parameters:
- `includeHidden` (`bool`, `optional`, default `false`): Include roots that are currently hidden in the active game state

### `rimworld/list_debug_action_children`

List direct children of a RimWorld debug action path

Parameters:
- `path` (`string`, `required`): Stable debug action path returned by the discovery tools
- `includeHidden` (`bool`, `optional`, default `false`): Include child nodes that are currently hidden in the active game state

### `rimworld/search_debug_actions`

Search the full RimWorld debug-action tree globally by path, label, category, and source metadata so callers do not need to walk one subtree at a time

Parameters:
- `query` (`string`, `required`): Case-insensitive search text such as Toggle Job Logging or Log Job Details
- `limit` (`int`, `optional`, default `50`): Maximum number of matches to return
- `includeHidden` (`bool`, `optional`, default `false`): Include nodes that are currently hidden in the active game state
- `supportedOnly` (`bool`, `optional`, default `false`): Only return nodes whose execution metadata reports supported=true
- `requiredTargetKind` (`string`, `optional`, default `null`): Optional required target kind filter such as pawn

### `rimworld/get_debug_action`

Get metadata for one RimWorld debug action path and, optionally, its immediate children

Parameters:
- `path` (`string`, `required`): Stable debug action path returned by the discovery tools
- `includeChildren` (`bool`, `optional`, default `true`): Include immediate child nodes in the response
- `includeHiddenChildren` (`bool`, `optional`, default `false`): Include hidden child nodes when includeChildren is true

### `rimworld/execute_debug_action`

Execute a supported RimWorld debug action leaf by stable path, including pawn-target actions when pawnName or pawnId is provided

Parameters:
- `path` (`string`, `required`): Stable debug action path returned by the discovery tools
- `pawnName` (`string`, `optional`, default `null`): Optional current-map pawn name for ToolMapForPawns debug actions
- `pawnId` (`string`, `optional`, default `null`): Optional stable current-map pawn id from rimworld/list_colonists for ToolMapForPawns debug actions

### `rimworld/set_debug_setting`

Set a RimWorld debug setting toggle by stable path to a deterministic on/off state

Parameters:
- `path` (`string`, `required`): Stable debug setting path returned by the discovery tools
- `enabled` (`bool`, `required`): Desired enabled state

### `rimworld/set_colonist_job_logging`

Deterministically enable or disable job-tracker logging for one current-map colonist and return a log cursor plus recommended rimbridge/list_logs arguments for consuming future job lines

Parameters:
- `pawnName` (`string`, `optional`, default `null`): Optional current-map colonist name, short name, or full name
- `pawnId` (`string`, `optional`, default `null`): Optional stable current-map colonist pawn id from rimworld/list_colonists
- `enabled` (`bool`, `optional`, default `true`): Desired job-logging state

### `rimworld/list_mods`

List installed RimWorld mods, whether each one is enabled in the current configuration, and whether it matches the currently loaded session

Parameters:
- `includeInactive` (`bool`, `optional`, default `true`): Include inactive installed mods as well as the active load order

### `rimworld/get_mod_configuration_status`

Read semantic mod-configuration status for the current active load order, including warnings, ordering issues, and whether a restart is required to match the loaded session

Parameters: none.

### `rimworld/set_mod_enabled`

Enable or disable one installed mod by stable mod id, package id, name, or folder name, optionally persisting the updated ModsConfig.xml immediately

Parameters:
- `modId` (`string`, `required`): Stable modId from rimworld/list_mods, or an exact package id / package id (player-facing) / name / folder name match
- `enabled` (`bool`, `required`): True to enable the mod in the current configuration, false to disable it
- `save` (`bool`, `optional`, default `true`): True to persist the updated active-mod list to ModsConfig.xml immediately
- `allowDisableCore` (`bool`, `optional`, default `false`): Allow disabling the core RimWorld mod. Defaults to false as a safety guard.

### `rimworld/reorder_mod`

Move one currently enabled mod to a new zero-based active load-order index, optionally persisting the updated ModsConfig.xml immediately

Parameters:
- `modId` (`string`, `required`): Stable modId from rimworld/list_mods, or an exact package id / package id (player-facing) / name / folder name match
- `targetIndex` (`int`, `required`): Desired zero-based index within the current active mod load order
- `save` (`bool`, `optional`, default `true`): True to persist the updated active-mod order to ModsConfig.xml immediately

### `rimworld/list_mod_settings_surfaces`

List loaded mod handles that expose a settings dialog, a persistent ModSettings state object, or both

Parameters:
- `includeWithoutSettings` (`bool`, `optional`, default `false`): Include loaded mod handles even when they expose neither a settings window nor a ModSettings object

### `rimworld/get_mod_settings`

Read one loaded mod's semantic ModSettings object by stable mod id, package id, settings category, or handle type name

Parameters:
- `modId` (`string`, `required`): Stable modId from rimworld/list_mod_settings_surfaces, or an exact package id / settings category / handle type match
- `maxDepth` (`int`, `optional`, default `4`): Maximum object depth to traverse when describing nested settings
- `maxCollectionEntries` (`int`, `optional`, default `32`): Maximum number of children to return for any one list or dictionary node

### `rimworld/update_mod_settings`

Apply one or more field-path updates to a loaded mod's ModSettings object, with optional immediate persistence through Mod.WriteSettings()

Parameters:
- `modId` (`string`, `required`): Stable modId from rimworld/list_mod_settings_surfaces, or an exact package id / settings category / handle type match
- `values` (`Dictionary<string, object>`, `required`): Object mapping field paths such as SomeSetting or Nested.List[0] to desired values
- `write` (`bool`, `optional`, default `true`): True to persist through Mod.WriteSettings() after applying the updates
- `maxDepth` (`int`, `optional`, default `4`): Maximum object depth to traverse when returning the updated settings snapshot
- `maxCollectionEntries` (`int`, `optional`, default `32`): Maximum number of children to return for any one list or dictionary node in the updated snapshot

### `rimworld/reload_mod_settings`

Reload a loaded mod's ModSettings object from disk, discarding unsaved in-memory changes

Parameters:
- `modId` (`string`, `required`): Stable modId from rimworld/list_mod_settings_surfaces, or an exact package id / settings category / handle type match
- `maxDepth` (`int`, `optional`, default `4`): Maximum object depth to traverse when returning the reloaded settings snapshot
- `maxCollectionEntries` (`int`, `optional`, default `32`): Maximum number of children to return for any one list or dictionary node in the reloaded snapshot

### `rimworld/open_mod_settings`

Open RimWorld's native Dialog_ModSettings window for one loaded mod without foreground-dependent input

Parameters:
- `modId` (`string`, `required`): Stable modId from rimworld/list_mod_settings_surfaces, or an exact package id / settings category / handle type match
- `replaceExisting` (`bool`, `optional`, default `true`): Close any currently open mod-settings dialogs before opening the requested one

### `rimworld/get_designator_state`

Get the current Architect/designator selection state, including god mode and the selected designator

Parameters: none.

### `rimworld/set_god_mode`

Enable or disable RimWorld god mode deterministically

Parameters:
- `enabled` (`bool`, `optional`, default `true`): True to enable god mode, false to disable it

### `rimworld/list_architect_categories`

List RimWorld Architect categories using stable category ids

Parameters:
- `includeHidden` (`bool`, `optional`, default `false`): Include categories that are currently hidden
- `includeEmpty` (`bool`, `optional`, default `false`): Include categories even if they currently expose no designators

### `rimworld/list_architect_designators`

List Architect designators for one category, flattening dropdown widgets into actionable child designators

Parameters:
- `categoryId` (`string`, `required`): Stable category id from rimworld/list_architect_categories or the raw category defName
- `includeHidden` (`bool`, `optional`, default `false`): Include designators that are currently hidden

### `rimworld/select_architect_designator`

Select an Architect designator by stable id without relying on foreground UI interaction

Parameters:
- `designatorId` (`string`, `required`): Stable designator id returned by rimworld/list_architect_designators

### `rimworld/apply_architect_designator`

Apply an Architect designator to one cell or a rectangle, with optional dry-run validation

Parameters:
- `designatorId` (`string`, `required`): Stable designator id returned by rimworld/list_architect_designators
- `x` (`int`, `required`): Target cell x coordinate
- `z` (`int`, `required`): Target cell z coordinate
- `width` (`int`, `optional`, default `1`): Rectangle width in cells starting at x/z
- `height` (`int`, `optional`, default `1`): Rectangle height in cells starting at x/z
- `dryRun` (`bool`, `optional`, default `false`): Validate placement without mutating the map
- `keepSelected` (`bool`, `optional`, default `true`): Keep the designator selected after the call completes

### `rimworld/list_zones`

List current-map zones such as stockpiles and growing zones

Parameters:
- `includeHidden` (`bool`, `optional`, default `false`): Include hidden zones
- `includeEmpty` (`bool`, `optional`, default `false`): Include zones with zero cells

### `rimworld/list_areas`

List current-map areas such as home, roof, snow-clear, and allowed areas

Parameters:
- `includeEmpty` (`bool`, `optional`, default `false`): Include areas with zero cells
- `includeAssignableOnly` (`bool`, `optional`, default `false`): Include only areas that can be assigned as allowed areas

### `rimworld/create_allowed_area`

Create a new allowed area and optionally make it the selected allowed-area target

Parameters:
- `label` (`string`, `optional`, default `null`): Optional label for the new allowed area
- `select` (`bool`, `optional`, default `true`): Select the new area as the current allowed-area target

### `rimworld/select_allowed_area`

Select an allowed area by id for area-designator flows, or clear the selection when areaId is omitted

Parameters:
- `areaId` (`string`, `optional`, default `null`): Area id from rimworld/list_areas, or omit to clear the selected allowed area

### `rimworld/set_zone_target`

Set or clear the explicit existing-zone target on a zone-add designator

Parameters:
- `designatorId` (`string`, `required`): Stable zone-add designator id returned by rimworld/list_architect_designators
- `zoneId` (`string`, `optional`, default `null`): Zone id from rimworld/list_zones, or omit to clear the explicit zone target

### `rimworld/clear_area`

Clear all cells from a mutable area such as a custom allowed area

Parameters:
- `areaId` (`string`, `required`): Area id from rimworld/list_areas

### `rimworld/delete_area`

Delete a mutable area such as a custom allowed area

Parameters:
- `areaId` (`string`, `required`): Area id from rimworld/list_areas

### `rimworld/delete_zone`

Delete an existing zone by id

Parameters:
- `zoneId` (`string`, `required`): Zone id from rimworld/list_zones

### `rimworld/get_cell_info`

Inspect one map cell, including things, blueprints, frames, designations, zones, and areas

Parameters:
- `x` (`int`, `required`): Target cell x coordinate
- `z` (`int`, `required`): Target cell z coordinate

### `rimworld/get_cells_info`

Inspect every map cell in a rectangle up to 1024 cells, including things, blueprints, frames, designations, zones, and areas

Parameters:
- `x` (`int`, `required`): Top-left cell x coordinate
- `z` (`int`, `required`): Top-left cell z coordinate
- `width` (`int`, `optional`, default `1`): Rectangle width in cells; width * height must not exceed 1024
- `height` (`int`, `optional`, default `1`): Rectangle height in cells; width * height must not exceed 1024

### `rimworld/find_random_cell_near`

Use RimWorld's expanding-radius random cell search to find a nearby cell or footprint that satisfies generic map criteria

Parameters:
- `x` (`int`, `required`): Origin cell x coordinate
- `z` (`int`, `required`): Origin cell z coordinate
- `startingSearchRadius` (`int`, `optional`, default `5`): Initial search radius in cells
- `maxSearchRadius` (`int`, `optional`, default `60`): Maximum search radius in cells
- `width` (`int`, `optional`, default `1`): Footprint width to validate at each candidate cell
- `height` (`int`, `optional`, default `1`): Footprint height to validate at each candidate cell
- `footprintAnchor` (`string`, `optional`, default `"top_left"`): Interpret the candidate cell as the footprint's top_left or center anchor
- `requireWalkable` (`bool`, `optional`, default `false`): Require every footprint cell to be walkable
- `requireStandable` (`bool`, `optional`, default `false`): Require every footprint cell to be standable
- `requireNotFogged` (`bool`, `optional`, default `false`): Require every footprint cell to be unfogged
- `requireNoImpassableThings` (`bool`, `optional`, default `false`): Reject footprint cells containing impassable things such as walls or solid rocks
- `reachablePawnName` (`string`, `optional`, default `null`): Optional current-map pawn name; when provided, the returned anchor cell must be reachable by that pawn
- `reachablePawnId` (`string`, `optional`, default `null`): Optional stable current-map pawn id from rimworld/list_colonists; when provided, the returned anchor cell must be reachable by that pawn
- `designatorId` (`string`, `optional`, default `null`): Optional architect designator id; when provided, every footprint cell must pass that designator's CanDesignateCell validation

### `rimworld/flood_fill_cells`

Analyze a contiguous area from one root cell using RimWorld's generic cell flood-fill algorithm and the same reusable footprint criteria as find_random_cell_near

Parameters:
- `x` (`int`, `required`): Root cell x coordinate
- `z` (`int`, `required`): Root cell z coordinate
- `maxCellsToProcess` (`int`, `optional`, default `256`): Maximum number of cells to process before stopping
- `minimumCellCount` (`int`, `optional`, default `0`): Optional minimum contiguous cell count to satisfy before stopping early
- `maxReturnedCells` (`int`, `optional`, default `64`): Maximum number of matching cells to include in the returned sample list
- `width` (`int`, `optional`, default `1`): Footprint width to validate at each visited cell
- `height` (`int`, `optional`, default `1`): Footprint height to validate at each visited cell
- `footprintAnchor` (`string`, `optional`, default `"top_left"`): Interpret each visited cell as the footprint's top_left or center anchor
- `requireWalkable` (`bool`, `optional`, default `false`): Require every footprint cell to be walkable
- `requireStandable` (`bool`, `optional`, default `false`): Require every footprint cell to be standable
- `requireNotFogged` (`bool`, `optional`, default `false`): Require every footprint cell to be unfogged
- `requireNoImpassableThings` (`bool`, `optional`, default `false`): Reject footprint cells containing impassable things such as walls or solid rocks
- `reachablePawnName` (`string`, `optional`, default `null`): Optional current-map pawn name; when provided, each visited anchor cell must be reachable by that pawn
- `reachablePawnId` (`string`, `optional`, default `null`): Optional stable current-map pawn id from rimworld/list_colonists; when provided, each visited anchor cell must be reachable by that pawn
- `designatorId` (`string`, `optional`, default `null`): Optional architect designator id; when provided, every footprint cell must pass that designator's CanDesignateCell validation

### `rimworld/get_ui_state`

Get the current RimWorld window stack and input state for background-safe UI automation

Parameters: none.

### `rimworld/list_main_tabs`

List RimWorld main tabs such as Work, Assign, Research, and mod-provided tabs with stable main-tab target ids

Parameters:
- `includeHidden` (`bool`, `optional`, default `false`): Include tabs whose worker currently reports them as hidden or unavailable

### `rimworld/open_main_tab`

Open one RimWorld main tab by stable target id, defName, label, or tab window type

Parameters:
- `mainTabId` (`string`, `required`): Stable main-tab target id from rimworld/list_main_tabs, or an exact defName / label / tab window type match

### `rimworld/close_main_tab`

Close the currently open RimWorld main tab, optionally asserting which tab is open first

Parameters:
- `mainTabId` (`string`, `optional`, default `null`): Optional stable main-tab target id or exact defName / label / tab window type expected to be open

### `rimworld/get_ui_layout`

Capture a generic structured layout snapshot of the current dialogs, windows, or main tabs, including actionable control target ids

Parameters:
- `surfaceId` (`string`, `optional`, default `null`): Optional surface target id such as a window target from rimworld/get_screen_targets or a main-tab target from rimworld/list_main_tabs
- `timeoutMs` (`int`, `optional`, default `2000`): Maximum time to wait for the requested UI surface to draw on screen

### `rimworld/click_ui_target`

Activate an actionable UI control target returned by rimworld/get_ui_layout on the next real draw frame

Parameters:
- `targetId` (`string`, `required`): Actionable ui-element target id returned by rimworld/get_ui_layout
- `timeoutMs` (`int`, `optional`, default `2000`): Maximum time to wait for the target control to be redrawn so the click can be injected

### `rimworld/set_hover_target`

Set a persistent virtual hover target for UI review and screenshots, using either an actionable ui-element target id or a current-map cell, pawn, or thing

Parameters:
- `targetId` (`string`, `optional`, default `null`): Optional actionable ui-element target id returned by rimworld/get_ui_layout
- `x` (`int?`, `optional`, default `null`): Current-map cell x coordinate when hovering a map cell
- `z` (`int?`, `optional`, default `null`): Current-map cell z coordinate when hovering a map cell
- `thingId` (`string`, `optional`, default `null`): Stable current-map thing id when hovering a spawned thing
- `pawnName` (`string`, `optional`, default `null`): Optional current-map pawn name when hovering a pawn
- `pawnId` (`string`, `optional`, default `null`): Optional stable current-map pawn id when hovering a pawn

### `rimworld/clear_hover_target`

Clear the current virtual hover target so screenshots and mouseover-driven UI return to the real cursor state

Parameters: none.

### `rimworld/press_accept`

Send semantic accept input to the active RimWorld window stack without requiring OS focus

Parameters: none.

### `rimworld/list_languages`

List installed RimWorld languages, including a recommended ASCII-safe switch query for each language and the currently active language

Parameters: none.

### `rimworld/press_cancel`

Send semantic cancel input to the active RimWorld window stack without requiring OS focus

Parameters: none.

### `rimworld/close_window`

Close an open RimWorld window by type name or, if omitted, the topmost window

Parameters:
- `windowType` (`string`, `optional`, default `null`): Optional short or full .NET type name of the target window

### `rimworld/open_window_by_type`

Open a RimWorld window by short or full .NET type name when the window exposes a public parameterless constructor

Parameters:
- `windowType` (`string`, `required`): Short or full .NET type name for a Verse.Window subtype, such as AchtungMod.SettingsToggles
- `replaceExisting` (`bool`, `optional`, default `true`): Close already-open windows of the same type before opening a fresh instance

### `rimworld/click_screen_target`

Semantically click a known actionable target id returned by rimworld/get_screen_targets without requiring OS focus

Parameters:
- `targetId` (`string`, `required`): Actionable target id such as a context-menu option target or window dismiss target

### `rimworld/switch_language`

Switch RimWorld to an installed language by the recommendedQuery from rimworld/list_languages or another exact language name match, mirroring the main-menu language picker and saving prefs

Parameters:
- `language` (`string`, `required`): Prefer the recommendedQuery from rimworld/list_languages; exact language ids and exact display/native/English names also work

### `rimworld/start_debug_game`

Start RimWorld's built-in quick test colony from the main menu

Parameters: none.

### `rimworld/go_to_main_menu`

Return to the RimWorld main menu entry scene, or no-op if already there

Parameters: none.

### `rimworld/list_colonists`

List player-controlled colonists available for selection and drafting, including stable pawn ids

Parameters:
- `currentMapOnly` (`bool`, `optional`, default `false`): True to only include the current map

### `rimworld/clear_selection`

Clear the current map selection

Parameters: none.

### `rimworld/select_pawn`

Select a single colonist by name or stable pawn id

Parameters:
- `pawnName` (`string`, `optional`, default `null`): Optional colonist name, short name, or full name
- `pawnId` (`string`, `optional`, default `null`): Optional stable colonist pawn id from rimworld/list_colonists
- `append` (`bool`, `optional`, default `false`): True to append to the current selection instead of replacing it

### `rimworld/deselect_pawn`

Deselect a single selected pawn by name or stable pawn id

Parameters:
- `pawnName` (`string`, `optional`, default `null`): Optional selected pawn name
- `pawnId` (`string`, `optional`, default `null`): Optional stable selected pawn id from rimworld/list_colonists

### `rimworld/set_draft`

Draft or undraft a colonist by name or stable pawn id

Parameters:
- `pawnName` (`string`, `optional`, default `null`): Optional colonist name
- `pawnId` (`string`, `optional`, default `null`): Optional stable colonist pawn id from rimworld/list_colonists
- `drafted` (`bool`, `optional`, default `true`): True to draft, false to undraft

### `rimworld/get_selected_pawn_inventory_state`

Read the selected pawn's carried thing and inventory contents, including Pick Up And Haul tracked items when available

Parameters: none.

### `rimworld/get_selection_semantics`

Get structured details for the current selection, including inspect strings, inspect-tab types, and the current selection fingerprint

Parameters: none.

### `rimworld/list_selected_gizmos`

List the current selection's actionable grouped gizmos using deterministic selection-scoped gizmo ids

Parameters: none.

### `rimworld/execute_gizmo`

Execute one grouped gizmo for the current selection by gizmo id returned from rimworld/list_selected_gizmos

Parameters:
- `gizmoId` (`string`, `required`): Selection-scoped gizmo id returned by rimworld/list_selected_gizmos

### `rimworld/list_messages`

List live RimWorld messages with native message ids and structured look-target metadata

Parameters:
- `limit` (`int`, `optional`, default `12`): Maximum number of live messages to return

### `rimworld/list_letters`

List current letter-stack entries with native letter ids, semantic letter content, and structured look-target metadata

Parameters:
- `limit` (`int`, `optional`, default `40`): Maximum number of letters to return

### `rimworld/open_letter`

Open a specific letter by native letter id, mirroring a normal left-click on the letter stack entry

Parameters:
- `letterId` (`string`, `required`): Letter id returned by rimworld/list_letters

### `rimworld/dismiss_letter`

Dismiss a specific dismissible letter by native letter id, mirroring a normal right-click on the letter stack entry

Parameters:
- `letterId` (`string`, `required`): Letter id returned by rimworld/list_letters

### `rimworld/list_alerts`

List active RimWorld alerts with structured culprit targets and alert-snapshot-scoped alert ids

Parameters:
- `limit` (`int`, `optional`, default `40`): Maximum number of active alerts to return

### `rimworld/activate_alert`

Activate one alert by alert id returned from rimworld/list_alerts, mirroring a normal left-click on the alert readout

Parameters:
- `alertId` (`string`, `required`): Alert id returned by rimworld/list_alerts

### `rimworld/get_camera_state`

Get the current map camera position, zoom, and visible rect

Parameters: none.

### `rimworld/get_screen_targets`

Get current screen-space targets such as open windows and active context-menu geometry

Parameters: none.

### `rimworld/jump_camera_to_pawn`

Jump the camera to a pawn by name or stable pawn id

Parameters:
- `pawnName` (`string`, `optional`, default `null`): Optional pawn name on the current map
- `pawnId` (`string`, `optional`, default `null`): Optional stable current-map pawn id from rimworld/list_colonists

### `rimworld/jump_camera_to_cell`

Jump the camera to a map cell

Parameters:
- `x` (`int`, `required`): Cell x coordinate
- `z` (`int`, `required`): Cell z coordinate

### `rimworld/move_camera`

Move the camera by a cell offset

Parameters:
- `deltaX` (`float`, `required`): Delta x in map cells
- `deltaZ` (`float`, `required`): Delta z in map cells

### `rimworld/zoom_camera`

Adjust the current camera zoom/root size

Parameters:
- `delta` (`float`, `required`): Positive values zoom out, negative values zoom in

### `rimworld/set_camera_zoom`

Set the current camera root size directly

Parameters:
- `rootSize` (`float`, `required`): Desired camera root size

### `rimworld/frame_pawns`

Frame a comma-separated list of pawns by name and/or stable pawn id so they fit in view

Parameters:
- `pawnNamesCsv` (`string`, `optional`, default `null`): Comma-separated pawn names. If omitted together with pawnIdsCsv, uses the current selection.
- `pawnIdsCsv` (`string`, `optional`, default `null`): Comma-separated stable pawn ids from rimworld/list_colonists. If omitted together with pawnNamesCsv, uses the current selection.

### `rimworld/take_screenshot`

Take an in-game screenshot and return the saved file path plus optional target metadata

Parameters:
- `fileName` (`string`, `optional`, default `null`): Optional screenshot file name without extension
- `includeTargets` (`bool`, `optional`, default `true`): Include current screen target metadata such as windows and context menus
- `suppressMessage` (`bool`, `optional`, default `true`): Suppress RimWorld's screenshot-taken message during this automated capture
- `clipTargetId` (`string`, `optional`, default `null`): Optional target id from rimworld/get_screen_targets to crop around
- `clipPadding` (`int`, `optional`, default `8`): Logical screen-pixel padding to include around the clip target

### `rimworld/list_saves`

List saved RimWorld games

Parameters: none.

### `rimworld/spawn_thing`

Spawn a thing on the current map at a target cell

Parameters:
- `defName` (`string`, `required`): ThingDef defName to spawn
- `x` (`int`, `required`): Target cell x coordinate
- `z` (`int`, `required`): Target cell z coordinate
- `stackCount` (`int`, `optional`, default `1`): Optional stack count. Clamped to the thing's stack limit.

### `rimworld/save_game`

Save the current game to a named save

Parameters:
- `saveName` (`string`, `required`): Save name without extension

### `rimworld/load_game`

Load a named RimWorld save

Parameters:
- `saveName` (`string`, `required`): Save name without extension

### `rimworld/open_context_menu`

Dispatch a live map click at a target pawn or cell and capture the resulting context menu when one remains open

Parameters:
- `targetPawnName` (`string`, `optional`, default `null`): Optional target pawn name on the current map.
- `targetPawnId` (`string`, `optional`, default `null`): Optional stable target pawn id from rimworld/list_colonists.
- `x` (`int`, `optional`, default `0`): Target cell x coordinate when no pawn name or id is given
- `z` (`int`, `optional`, default `0`): Target cell z coordinate when no pawn name or id is given
- `mode` (`string`, `optional`, default `"vanilla"`): Compatibility hint. 'vanilla', 'auto', and 'live' are accepted; the tool always routes through the live play-UI click path.
- `button` (`string`, `optional`, default `"right"`): Mouse button to inject. Supported values are 'left', 'right', and 'middle'.
- `holdDurationMs` (`int`, `optional`, default `0`): How long to hold the mouse button down before releasing it. Use this for mods that distinguish tap from hold on map clicks.
- `modifiers` (`string`, `optional`, default `null`): Optional comma-, space-, or plus-separated event modifiers such as 'shift', 'ctrl', 'alt', or 'command'.

### `rimworld/right_click_cell`

Dispatch a live map click interaction for the current pawn selection so vanilla and modded handlers see the same input path as a real click

Parameters:
- `targetPawnName` (`string`, `optional`, default `null`): Optional target pawn name on the current map.
- `targetPawnId` (`string`, `optional`, default `null`): Optional stable target pawn id from rimworld/list_colonists.
- `x` (`int`, `optional`, default `0`): Target cell x coordinate when no pawn name or id is given
- `z` (`int`, `optional`, default `0`): Target cell z coordinate when no pawn name or id is given
- `button` (`string`, `optional`, default `"right"`): Mouse button to inject. Supported values are 'left', 'right', and 'middle'.
- `holdDurationMs` (`int`, `optional`, default `0`): How long to hold the mouse button down before releasing it. Use this for mods that distinguish tap from hold on map clicks.
- `modifiers` (`string`, `optional`, default `null`): Optional comma-, space-, or plus-separated event modifiers such as 'shift', 'ctrl', 'alt', or 'command'.

### `rimworld/get_context_menu_options`

Get the currently opened debug context menu options

Parameters: none.

### `rimworld/execute_context_menu_option`

Execute a context menu option by index or label

Parameters:
- `optionIndex` (`int`, `optional`, default `-1`): 1-based option index. Use -1 to resolve by label instead.
- `label` (`string`, `optional`, default `null`): Exact or partial menu label to execute when optionIndex is -1

### `rimworld/close_context_menu`

Close the currently opened debug context menu

Parameters: none.
