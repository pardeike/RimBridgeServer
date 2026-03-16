# RimBridgeServer

RimBridgeServer lets you control RimWorld from outside the game. This is useful for testing mods automatically or building tools that work with RimWorld.

Project planning and the implementation roadmap live in [`docs/architecture.md`](docs/architecture.md) and [`docs/progress-log.md`](docs/progress-log.md).

## What does it do?

This mod creates a connection point (called a "server") inside RimWorld. Other programs can connect to this server to:

- Check if the game is running
- Pause or unpause the game
- Get information about the current game
- Inspect colonists, selection, camera, saves, and screenshots
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
- **`rimbridge/wait_for_operation`** - Wait until a recorded operation reaches a terminal status
- **`rimbridge/wait_for_game_loaded`** - Wait until RimWorld has finished loading a playable game
- **`rimbridge/wait_for_long_event_idle`** - Wait until RimWorld reports no long event in progress

### Game control
- **`rimworld/get_game_info`** - Get information about the current game
- **`rimworld/pause_game`** - Pause or unpause the game
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

### Camera and screenshots
- **`rimworld/get_camera_state`** - Report camera position, zoom, and current view rect
- **`rimworld/jump_camera_to_pawn`** - Jump the camera to a pawn and select it
- **`rimworld/jump_camera_to_cell`** - Jump the camera to a specific map cell
- **`rimworld/move_camera`** - Move the camera by a cell offset
- **`rimworld/zoom_camera`** - Adjust camera zoom relative to the current root size
- **`rimworld/set_camera_zoom`** - Set camera root size directly
- **`rimworld/frame_pawns`** - Frame several pawns together in view
- **`rimworld/take_screenshot`** - Save an in-game screenshot and return the file path

### Context-menu debugging
- **`rimworld/get_achtung_state`** - Report Achtung debug settings relevant to menu repro cases
- **`rimworld/set_achtung_show_drafted_orders_when_undrafted`** - Toggle the compatibility mode that re-enables the old merged-menu behavior
- **`rimworld/open_context_menu`** - Open a debug context menu at a pawn or cell
- **`rimworld/get_context_menu_options`** - Return the currently opened debug menu options
- **`rimworld/execute_context_menu_option`** - Execute one menu option by index or label
- **`rimworld/close_context_menu`** - Close the currently opened debug context menu

`rimworld/start_debug_game` mirrors RimWorld's own dev quick-test flow. It only works from the main menu and returns a detailed state snapshot when the request is rejected.

`rimworld/open_context_menu` supports `mode: auto`, `mode: achtung`, and `mode: vanilla`. When Achtung is loaded, `auto` prefers Achtung's merged multi-pawn menu via reflection so external tools can reproduce issues against the same action builder the player sees in-game.

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

For test automation, prefer the explicit wait tools over blind sleeps. `rimbridge/wait_for_game_loaded`, `rimbridge/wait_for_long_event_idle`, and `rimbridge/wait_for_operation` provide bounded waits with state snapshots so scripts can move quickly when the game is ready and fail with useful diagnostics when it is not.

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
lib/            - External libraries
.vscode/        - Visual Studio Code settings
```

## License

This project uses the MIT License. See the `LICENSE` file for details.

## Dependencies

This mod includes [Lib.GAB](https://github.com/pardeike/Lib.GAB), which provides the GABP protocol implementation.
