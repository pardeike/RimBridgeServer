using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lib.GAB.Tools;

namespace RimBridgeServer;

public class RimBridgeTools
{
    [Tool("rimbridge/ping", Description = "Connectivity test. Returns 'pong'.")]
    public object Ping()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/get_game_info", Description = "Get basic information about the current RimWorld game")]
    public object GetGameInfo()
    {
        return InvokeAlias();
    }

    [Tool("rimbridge/get_operation", Description = "Get the latest journal snapshot for a specific operation id")]
    public object GetOperation([ToolParameter(Description = "Operation id returned in tool metadata")] string operationId)
    {
        return InvokeAlias(Arguments((nameof(operationId), operationId)));
    }

    [Tool("rimbridge/get_bridge_status", Description = "Get the current bridge and RimWorld state snapshot without mutating game state")]
    public object GetBridgeStatus()
    {
        return InvokeAlias();
    }

    [Tool("rimbridge/list_operations", Description = "List recent bridge operations from the in-memory operation journal")]
    public object ListOperations([ToolParameter(Description = "Maximum number of operations to return")] int limit = 20)
    {
        return InvokeAlias(Arguments((nameof(limit), limit)));
    }

    [Tool("rimbridge/list_operation_events", Description = "List recent bridge operation lifecycle events from the in-memory event journal")]
    public object ListOperationEvents(
        [ToolParameter(Description = "Maximum number of events to return")] int limit = 50,
        [ToolParameter(Description = "Optional event type filter, such as operation.failed")] string eventType = null,
        [ToolParameter(Description = "Only include events with a sequence greater than this cursor")] long afterSequence = 0,
        [ToolParameter(Description = "Include diagnostic bridge operations such as status and journal reads")] bool includeDiagnostics = false)
    {
        return InvokeAlias(Arguments((nameof(limit), limit), (nameof(eventType), eventType), (nameof(afterSequence), afterSequence), (nameof(includeDiagnostics), includeDiagnostics)));
    }

    [Tool("rimbridge/list_logs", Description = "List recent captured RimWorld and bridge log entries from the in-memory log journal")]
    public object ListLogs(
        [ToolParameter(Description = "Maximum number of log entries to return")] int limit = 50,
        [ToolParameter(Description = "Minimum level to include: info, warning, error, or fatal")] string minimumLevel = "info",
        [ToolParameter(Description = "Only include log entries with a sequence greater than this cursor")] long afterSequence = 0)
    {
        return InvokeAlias(Arguments((nameof(limit), limit), (nameof(minimumLevel), minimumLevel), (nameof(afterSequence), afterSequence)));
    }

    [Tool("rimbridge/wait_for_operation", Description = "Wait for an operation in the journal to reach a terminal status")]
    public object WaitForOperation(
        [ToolParameter(Description = "Operation id returned in tool metadata")] string operationId,
        [ToolParameter(Description = "Maximum time to wait in milliseconds")] int timeoutMs = 10000,
        [ToolParameter(Description = "Polling interval in milliseconds")] int pollIntervalMs = 50)
    {
        return InvokeAlias(Arguments((nameof(operationId), operationId), (nameof(timeoutMs), timeoutMs), (nameof(pollIntervalMs), pollIntervalMs)));
    }

    [Tool("rimbridge/wait_for_game_loaded", Description = "Wait until RimWorld has finished loading a playable game and, optionally, until screen fade is complete")]
    public object WaitForGameLoaded(
        [ToolParameter(Description = "Maximum time to wait in milliseconds")] int timeoutMs = 30000,
        [ToolParameter(Description = "Polling interval in milliseconds")] int pollIntervalMs = 100,
        [ToolParameter(Description = "Wait until RimWorld's screen fade has fully cleared")] bool waitForScreenFade = true,
        [ToolParameter(Description = "Pause the game before returning success if it is still running")] bool pauseIfNeeded = false)
    {
        return InvokeAlias(Arguments((nameof(timeoutMs), timeoutMs), (nameof(pollIntervalMs), pollIntervalMs), (nameof(waitForScreenFade), waitForScreenFade), (nameof(pauseIfNeeded), pauseIfNeeded)));
    }

    [Tool("rimbridge/wait_for_long_event_idle", Description = "Wait until RimWorld reports no long events in progress")]
    public object WaitForLongEventIdle(
        [ToolParameter(Description = "Maximum time to wait in milliseconds")] int timeoutMs = 30000,
        [ToolParameter(Description = "Polling interval in milliseconds")] int pollIntervalMs = 100)
    {
        return InvokeAlias(Arguments((nameof(timeoutMs), timeoutMs), (nameof(pollIntervalMs), pollIntervalMs)));
    }

    [Tool("rimworld/pause_game", Description = "Pause or unpause the game")]
    public object PauseGame([ToolParameter(Description = "True to pause, false to unpause")] bool pause = true)
    {
        return InvokeAlias(Arguments((nameof(pause), pause)));
    }

    [Tool("rimworld/list_debug_action_roots", Description = "List top-level RimWorld debug action roots using stable internal debug-action paths")]
    public object ListDebugActionRoots([ToolParameter(Description = "Include roots that are currently hidden in the active game state")] bool includeHidden = false)
    {
        return InvokeAlias(Arguments((nameof(includeHidden), includeHidden)));
    }

    [Tool("rimworld/list_debug_action_children", Description = "List direct children of a RimWorld debug action path")]
    public object ListDebugActionChildren(
        [ToolParameter(Description = "Stable debug action path returned by the discovery tools")] string path,
        [ToolParameter(Description = "Include child nodes that are currently hidden in the active game state")] bool includeHidden = false)
    {
        return InvokeAlias(Arguments((nameof(path), path), (nameof(includeHidden), includeHidden)));
    }

    [Tool("rimworld/get_debug_action", Description = "Get metadata for one RimWorld debug action path and, optionally, its immediate children")]
    public object GetDebugAction(
        [ToolParameter(Description = "Stable debug action path returned by the discovery tools")] string path,
        [ToolParameter(Description = "Include immediate child nodes in the response")] bool includeChildren = true,
        [ToolParameter(Description = "Include hidden child nodes when includeChildren is true")] bool includeHiddenChildren = false)
    {
        return InvokeAlias(Arguments((nameof(path), path), (nameof(includeChildren), includeChildren), (nameof(includeHiddenChildren), includeHiddenChildren)));
    }

    [Tool("rimworld/execute_debug_action", Description = "Execute a directly supported RimWorld debug action leaf by stable path")]
    public object ExecuteDebugAction([ToolParameter(Description = "Stable debug action path returned by the discovery tools")] string path)
    {
        return InvokeAlias(Arguments((nameof(path), path)));
    }

    [Tool("rimworld/set_debug_setting", Description = "Set a RimWorld debug setting toggle by stable path to a deterministic on/off state")]
    public object SetDebugSetting(
        [ToolParameter(Description = "Stable debug setting path returned by the discovery tools")] string path,
        [ToolParameter(Description = "Desired enabled state")] bool enabled)
    {
        return InvokeAlias(Arguments((nameof(path), path), (nameof(enabled), enabled)));
    }

    [Tool("rimworld/get_designator_state", Description = "Get the current Architect/designator selection state, including god mode and the selected designator")]
    public object GetDesignatorState()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/set_god_mode", Description = "Enable or disable RimWorld god mode deterministically")]
    public object SetGodMode([ToolParameter(Description = "True to enable god mode, false to disable it")] bool enabled = true)
    {
        return InvokeAlias(Arguments((nameof(enabled), enabled)));
    }

    [Tool("rimworld/list_architect_categories", Description = "List RimWorld Architect categories using stable category ids")]
    public object ListArchitectCategories(
        [ToolParameter(Description = "Include categories that are currently hidden")] bool includeHidden = false,
        [ToolParameter(Description = "Include categories even if they currently expose no designators")] bool includeEmpty = false)
    {
        return InvokeAlias(Arguments((nameof(includeHidden), includeHidden), (nameof(includeEmpty), includeEmpty)));
    }

    [Tool("rimworld/list_architect_designators", Description = "List Architect designators for one category, flattening dropdown widgets into actionable child designators")]
    public object ListArchitectDesignators(
        [ToolParameter(Description = "Stable category id from rimworld/list_architect_categories or the raw category defName")] string categoryId,
        [ToolParameter(Description = "Include designators that are currently hidden")] bool includeHidden = false)
    {
        return InvokeAlias(Arguments((nameof(categoryId), categoryId), (nameof(includeHidden), includeHidden)));
    }

    [Tool("rimworld/select_architect_designator", Description = "Select an Architect designator by stable id without relying on foreground UI interaction")]
    public object SelectArchitectDesignator([ToolParameter(Description = "Stable designator id returned by rimworld/list_architect_designators")] string designatorId)
    {
        return InvokeAlias(Arguments((nameof(designatorId), designatorId)));
    }

    [Tool("rimworld/apply_architect_designator", Description = "Apply an Architect designator to one cell or a rectangle, with optional dry-run validation")]
    public object ApplyArchitectDesignator(
        [ToolParameter(Description = "Stable designator id returned by rimworld/list_architect_designators")] string designatorId,
        [ToolParameter(Description = "Target cell x coordinate")] int x,
        [ToolParameter(Description = "Target cell z coordinate")] int z,
        [ToolParameter(Description = "Rectangle width in cells starting at x/z")] int width = 1,
        [ToolParameter(Description = "Rectangle height in cells starting at x/z")] int height = 1,
        [ToolParameter(Description = "Validate placement without mutating the map")] bool dryRun = false,
        [ToolParameter(Description = "Keep the designator selected after the call completes")] bool keepSelected = true)
    {
        return InvokeAlias(Arguments((nameof(designatorId), designatorId), (nameof(x), x), (nameof(z), z), (nameof(width), width), (nameof(height), height), (nameof(dryRun), dryRun), (nameof(keepSelected), keepSelected)));
    }

    [Tool("rimworld/get_cell_info", Description = "Inspect one map cell, including things, blueprints, frames, and designations")]
    public object GetCellInfo(
        [ToolParameter(Description = "Target cell x coordinate")] int x,
        [ToolParameter(Description = "Target cell z coordinate")] int z)
    {
        return InvokeAlias(Arguments((nameof(x), x), (nameof(z), z)));
    }

    [Tool("rimworld/get_ui_state", Description = "Get the current RimWorld window stack and input state for background-safe UI automation")]
    public object GetUiState()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/press_accept", Description = "Send semantic accept input to the active RimWorld window stack without requiring OS focus")]
    public object PressAccept()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/press_cancel", Description = "Send semantic cancel input to the active RimWorld window stack without requiring OS focus")]
    public object PressCancel()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/close_window", Description = "Close an open RimWorld window by type name or, if omitted, the topmost window")]
    public object CloseWindow([ToolParameter(Description = "Optional short or full .NET type name of the target window")] string windowType = null)
    {
        return InvokeAlias(Arguments((nameof(windowType), windowType)));
    }

    [Tool("rimworld/click_screen_target", Description = "Semantically click a known actionable target id returned by rimworld/get_screen_targets without requiring OS focus")]
    public object ClickScreenTarget([ToolParameter(Description = "Actionable target id such as a context-menu option target or window dismiss target")] string targetId)
    {
        return InvokeAlias(Arguments((nameof(targetId), targetId)));
    }

    [Tool("rimworld/start_debug_game", Description = "Start RimWorld's built-in quick test colony from the main menu")]
    public object StartDebugGame()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/list_colonists", Description = "List player-controlled colonists available for selection and drafting")]
    public object ListColonists([ToolParameter(Description = "True to only include the current map")] bool currentMapOnly = false)
    {
        return InvokeAlias(Arguments((nameof(currentMapOnly), currentMapOnly)));
    }

    [Tool("rimworld/clear_selection", Description = "Clear the current map selection")]
    public object ClearSelection()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/select_pawn", Description = "Select a single colonist by name")]
    public object SelectPawn(
        [ToolParameter(Description = "Colonist name, short name, or full name")] string pawnName,
        [ToolParameter(Description = "True to append to the current selection instead of replacing it")] bool append = false)
    {
        return InvokeAlias(Arguments((nameof(pawnName), pawnName), (nameof(append), append)));
    }

    [Tool("rimworld/deselect_pawn", Description = "Deselect a single selected pawn by name")]
    public object DeselectPawn([ToolParameter(Description = "Selected pawn name")] string pawnName)
    {
        return InvokeAlias(Arguments((nameof(pawnName), pawnName)));
    }

    [Tool("rimworld/set_draft", Description = "Draft or undraft a colonist by name")]
    public object SetDraft(
        [ToolParameter(Description = "Colonist name")] string pawnName,
        [ToolParameter(Description = "True to draft, false to undraft")] bool drafted = true)
    {
        return InvokeAlias(Arguments((nameof(pawnName), pawnName), (nameof(drafted), drafted)));
    }

    [Tool("rimworld/get_camera_state", Description = "Get the current map camera position, zoom, and visible rect")]
    public object GetCameraState()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/get_screen_targets", Description = "Get current screen-space targets such as open windows and active context-menu geometry")]
    public object GetScreenTargets()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/jump_camera_to_pawn", Description = "Jump the camera to a pawn by name")]
    public object JumpCameraToPawn([ToolParameter(Description = "Pawn name on the current map")] string pawnName)
    {
        return InvokeAlias(Arguments((nameof(pawnName), pawnName)));
    }

    [Tool("rimworld/jump_camera_to_cell", Description = "Jump the camera to a map cell")]
    public object JumpCameraToCell(
        [ToolParameter(Description = "Cell x coordinate")] int x,
        [ToolParameter(Description = "Cell z coordinate")] int z)
    {
        return InvokeAlias(Arguments((nameof(x), x), (nameof(z), z)));
    }

    [Tool("rimworld/move_camera", Description = "Move the camera by a cell offset")]
    public object MoveCamera(
        [ToolParameter(Description = "Delta x in map cells")] float deltaX,
        [ToolParameter(Description = "Delta z in map cells")] float deltaZ)
    {
        return InvokeAlias(Arguments((nameof(deltaX), deltaX), (nameof(deltaZ), deltaZ)));
    }

    [Tool("rimworld/zoom_camera", Description = "Adjust the current camera zoom/root size")]
    public object ZoomCamera([ToolParameter(Description = "Positive values zoom out, negative values zoom in")] float delta)
    {
        return InvokeAlias(Arguments((nameof(delta), delta)));
    }

    [Tool("rimworld/set_camera_zoom", Description = "Set the current camera root size directly")]
    public object SetCameraZoom([ToolParameter(Description = "Desired camera root size")] float rootSize)
    {
        return InvokeAlias(Arguments((nameof(rootSize), rootSize)));
    }

    [Tool("rimworld/frame_pawns", Description = "Frame a comma-separated list of pawns so they fit in view")]
    public object FramePawns([ToolParameter(Description = "Comma-separated pawn names. If omitted, uses the current selection.")] string pawnNamesCsv = null)
    {
        return InvokeAlias(Arguments((nameof(pawnNamesCsv), pawnNamesCsv)));
    }

    [Tool("rimworld/take_screenshot", Description = "Take an in-game screenshot and return the saved file path plus optional target metadata")]
    public object TakeScreenshot(
        [ToolParameter(Description = "Optional screenshot file name without extension")] string fileName = null,
        [ToolParameter(Description = "Include current screen target metadata such as windows and context menus")] bool includeTargets = true,
        [ToolParameter(Description = "Suppress RimWorld's screenshot-taken message during this automated capture")] bool suppressMessage = true,
        [ToolParameter(Description = "Optional target id from rimworld/get_screen_targets to crop around")] string clipTargetId = null,
        [ToolParameter(Description = "Logical screen-pixel padding to include around the clip target")] int clipPadding = 8)
    {
        return InvokeAlias(Arguments((nameof(fileName), fileName), (nameof(includeTargets), includeTargets), (nameof(suppressMessage), suppressMessage), (nameof(clipTargetId), clipTargetId), (nameof(clipPadding), clipPadding)));
    }

    [Tool("rimworld/get_achtung_state", Description = "Get Achtung-specific debug state when the mod is loaded")]
    public object GetAchtungState()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/set_achtung_show_drafted_orders_when_undrafted", Description = "Enable or disable Achtung's compatibility mode that merges drafted-only orders into undrafted menus")]
    public object SetAchtungShowDraftedOrdersWhenUndrafted(
        [ToolParameter(Description = "True to re-enable the old merged-menu behavior, false to use the fixed behavior")] bool enabled)
    {
        return InvokeAlias(Arguments((nameof(enabled), enabled)));
    }

    [Tool("rimworld/list_saves", Description = "List saved RimWorld games")]
    public object ListSaves()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/spawn_thing", Description = "Spawn a thing on the current map at a target cell")]
    public object SpawnThing(
        [ToolParameter(Description = "ThingDef defName to spawn")] string defName,
        [ToolParameter(Description = "Target cell x coordinate")] int x,
        [ToolParameter(Description = "Target cell z coordinate")] int z,
        [ToolParameter(Description = "Optional stack count. Clamped to the thing's stack limit.")] int stackCount = 1)
    {
        return InvokeAlias(Arguments((nameof(defName), defName), (nameof(x), x), (nameof(z), z), (nameof(stackCount), stackCount)));
    }

    [Tool("rimworld/save_game", Description = "Save the current game to a named save")]
    public object SaveGame([ToolParameter(Description = "Save name without extension")] string saveName)
    {
        return InvokeAlias(Arguments((nameof(saveName), saveName)));
    }

    [Tool("rimworld/load_game", Description = "Load a named RimWorld save")]
    public object LoadGame([ToolParameter(Description = "Save name without extension")] string saveName)
    {
        return InvokeAlias(Arguments((nameof(saveName), saveName)));
    }

    [Tool("rimworld/open_context_menu", Description = "Open a debug context menu at a target pawn or cell using Achtung when available")]
    public object OpenContextMenu(
        [ToolParameter(Description = "Target pawn name on the current map. Optional if x/z are provided.")] string targetPawnName = null,
        [ToolParameter(Description = "Target cell x coordinate when no pawn name is given")] int x = 0,
        [ToolParameter(Description = "Target cell z coordinate when no pawn name is given")] int z = 0,
        [ToolParameter(Description = "Context menu provider: auto, achtung, or vanilla")] string mode = "auto")
    {
        return InvokeAlias(Arguments((nameof(targetPawnName), targetPawnName), (nameof(x), x), (nameof(z), z), (nameof(mode), mode)));
    }

    [Tool("rimworld/get_context_menu_options", Description = "Get the currently opened debug context menu options")]
    public object GetContextMenuOptions()
    {
        return InvokeAlias();
    }

    [Tool("rimworld/execute_context_menu_option", Description = "Execute a context menu option by index or label")]
    public object ExecuteContextMenuOption(
        [ToolParameter(Description = "1-based option index. Use -1 to resolve by label instead.")] int optionIndex = -1,
        [ToolParameter(Description = "Exact or partial menu label to execute when optionIndex is -1")] string label = null)
    {
        return InvokeAlias(Arguments((nameof(optionIndex), optionIndex), (nameof(label), label)));
    }

    [Tool("rimworld/close_context_menu", Description = "Close the currently opened debug context menu")]
    public object CloseContextMenu()
    {
        return InvokeAlias();
    }

    private static object InvokeAlias(Dictionary<string, object> arguments = null, [CallerMemberName] string memberName = null)
    {
        return LegacyToolExecution.InvokeAlias(memberName, arguments);
    }

    private static Dictionary<string, object> Arguments(params (string Name, object Value)[] arguments)
    {
        var result = new Dictionary<string, object>(arguments.Length, System.StringComparer.Ordinal);
        foreach (var argument in arguments)
            result[argument.Name] = argument.Value;

        return result;
    }
}
