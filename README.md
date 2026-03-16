# RimBridgeServer

RimBridgeServer lets you control RimWorld from outside the game. This is useful for testing mods automatically or building tools that work with RimWorld.

Project planning and the implementation roadmap live in [`docs/architecture.md`](docs/architecture.md) and [`docs/progress-log.md`](docs/progress-log.md).

## What does it do?

This mod creates a connection point (called a "server") inside RimWorld. Other programs can connect to this server to:

- Check if the game is running
- Pause or unpause the game
- Get information about the current game
- Inspect colonists, selection, camera, saves, screenshots, and screen-space UI targets
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
- **Debuggable**: Exposes context-menu and screenshot tools that are useful for mod repro cases

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

### Game control
- **`rimworld/get_game_info`** - Get information about the current game
- **`rimworld/pause_game`** - Pause or unpause the game
- **`rimworld/list_debug_action_roots`** - List the top-level debug menu tabs and their stable internal paths
- **`rimworld/list_debug_action_children`** - List direct children under one debug menu path
- **`rimworld/get_debug_action`** - Get one debug node with metadata such as tab, toggle state, and execution mode
- **`rimworld/execute_debug_action`** - Execute a directly supported debug action and return captured side effects such as logs and opened windows
- **`rimworld/set_debug_setting`** - Set a debug settings toggle to a deterministic on/off state by stable path
- **`rimworld/get_ui_state`** - Inspect the current RimWorld window stack and UI/input state
- **`rimworld/press_accept`** - Send semantic accept input to the active RimWorld window stack
- **`rimworld/press_cancel`** - Send semantic cancel input to the active RimWorld window stack
- **`rimworld/close_window`** - Close an open RimWorld window by type name or close the topmost window
- **`rimworld/click_screen_target`** - Semantically click a known actionable target id returned by `rimworld/get_screen_targets`
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
- **`rimworld/get_achtung_state`** - Report Achtung debug settings relevant to menu repro cases
- **`rimworld/set_achtung_show_drafted_orders_when_undrafted`** - Toggle the compatibility mode that re-enables the old merged-menu behavior
- **`rimworld/open_context_menu`** - Open a debug context menu at a pawn or cell
- **`rimworld/get_context_menu_options`** - Return the currently opened debug menu options
- **`rimworld/execute_context_menu_option`** - Execute one menu option by index or label
- **`rimworld/close_context_menu`** - Close the currently opened debug context menu

`rimworld/start_debug_game` mirrors RimWorld's own dev quick-test flow. It only works from the main menu and returns a detailed state snapshot when the request is rejected.

`rimworld/open_context_menu` supports `mode: auto`, `mode: achtung`, and `mode: vanilla`. When Achtung is loaded, `auto` prefers Achtung's merged multi-pawn menu via reflection so external tools can reproduce issues against the same action builder the player sees in-game.

### Debug menu mapping

`rimworld/list_debug_action_roots` exposes the same internal graph that powers RimWorld's debug dialog, including the three important tabs: `Actions/tools`, `Settings`, and `Output`. The bridge keeps the stable internal paths such as `Outputs\\Tick Rates` and `Settings\\Show Architect Menu Order`, while also returning normalized tab metadata so clients can stay close to the in-game UI model.

`rimworld/execute_debug_action` is intentionally side-effect aware. Instead of pretending every debug action has a typed function return, the bridge captures what actually happened around the execution: new log entries, window opens/closes, and before/after game state snapshots. This makes the `Output` tab useful without bespoke wrappers for each individual output command.

`rimworld/set_debug_setting` builds deterministic semantics on top of the same graph. Settings nodes already expose their current `on` state and a direct toggle action, so the bridge can move them to an explicit target value and report whether anything changed.

### Example workflow for Achtung Issue #95

1. Start or load a prepared colony.
2. Use `rimworld/list_colonists` to find the colonists involved in the repro.
3. Use `rimworld/select_pawn` and `rimworld/frame_pawns` to put the scene on screen.
4. If you need a controlled before/after comparison, use `rimworld/set_achtung_show_drafted_orders_when_undrafted` to switch between the legacy and fixed menu behavior without changing the save.
5. Use `rimworld/open_context_menu` with `mode: achtung` on the target pawn.
6. Inspect the returned options or call `rimworld/take_screenshot` for visual proof.
7. Use `rimworld/save_game` to preserve the repro state for later development cycles.

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
