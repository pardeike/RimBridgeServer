using System.Text.Json.Nodes;

namespace RimBridgeServer.LiveSmoke;

internal delegate Task SmokeScenarioRunner(SmokeScenarioContext context, CancellationToken cancellationToken);

internal sealed class SmokeScenarioDefinition
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required SmokeScenarioRunner RunAsync { get; init; }
}

internal static class SmokeScenarioCatalog
{
    public const string DebugGameLoadScenarioName = "debug-game-load";
    public const string ContextMenuCancelRoundTripScenarioName = "context-menu-cancel-roundtrip";
    public const string SelectionRoundTripScenarioName = "selection-roundtrip";
    public const string SaveLoadRoundTripScenarioName = "save-load-roundtrip";
    public const string ScreenshotCaptureScenarioName = "screenshot-capture";
    private const string SaveLoadRoundTripSaveName = "rimbridge_live_smoke_save_load_roundtrip";

    private static readonly IReadOnlyDictionary<string, SmokeScenarioDefinition> Definitions =
        new Dictionary<string, SmokeScenarioDefinition>(StringComparer.Ordinal)
        {
            [DebugGameLoadScenarioName] = new SmokeScenarioDefinition
            {
                Name = DebugGameLoadScenarioName,
                Description = "Start RimWorld's debug colony from the main menu and capture the resulting operation/log window.",
                RunAsync = RunDebugGameLoadAsync
            },
            [ContextMenuCancelRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = ContextMenuCancelRoundTripScenarioName,
                Description = "Ensure a playable game exists, open a real context menu, and dismiss it again through background-safe semantic input.",
                RunAsync = RunContextMenuCancelRoundTripAsync
            },
            [SelectionRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = SelectionRoundTripScenarioName,
                Description = "Ensure a playable game exists, then exercise selection and camera tools around a real colonist.",
                RunAsync = RunSelectionRoundTripAsync
            },
            [SaveLoadRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = SaveLoadRoundTripScenarioName,
                Description = "Ensure a playable game exists, save it to a stable test slot, load it again, and verify the colony comes back.",
                RunAsync = RunSaveLoadRoundTripAsync
            },
            [ScreenshotCaptureScenarioName] = new SmokeScenarioDefinition
            {
                Name = ScreenshotCaptureScenarioName,
                Description = "Ensure a playable game exists, frame a real pawn, capture a screenshot, and verify the file artifact.",
                RunAsync = RunScreenshotCaptureAsync
            }
        };

    public static string DefaultScenarioName => DebugGameLoadScenarioName;

    public static IReadOnlyList<string> ScenarioNames => List().Select(definition => definition.Name).ToList();

    public static IReadOnlyList<SmokeScenarioDefinition> List()
    {
        return Definitions.Values.OrderBy(definition => definition.Name, StringComparer.Ordinal).ToList();
    }

    public static SmokeScenarioDefinition GetOrThrow(string scenarioName)
    {
        if (Definitions.TryGetValue(scenarioName, out var definition))
            return definition;

        throw new InvalidOperationException($"Unknown scenario '{scenarioName}'. Use --list-scenarios to see the available options.");
    }

    private static async Task RunDebugGameLoadAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.WaitForLongEventIdleAsync("wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("snapshot_bridge_status", cancellationToken);

        var startDebugGame = await context.CallGameToolAsync("start_debug_game", "rimworld/start_debug_game", new { }, cancellationToken);
        context.EnsureSucceeded(startDebugGame, "Starting RimWorld debug game");

        var operationId = context.RequireOperationId(startDebugGame, "Starting RimWorld debug game");
        await context.WaitForOperationAsync("wait_for_operation", operationId, cancellationToken);
        await context.WaitForGameLoadedAsync("wait_for_game_loaded", cancellationToken);

        var colonists = await context.CallGameToolAsync("list_colonists", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing current-map colonists");

        context.Report.ColonistCount = JsonNodeHelpers.ReadInt32(colonists.StructuredContent, "count");
        if (context.Report.ColonistCount.GetValueOrDefault() <= 0)
            throw new InvalidOperationException("The debug game loaded but did not expose any colonists on the current map.");

        context.SetScenarioData("colonists", JsonNodeHelpers.GetPath(colonists.StructuredContent, "colonists"));

        var observation = await observationWindow.CaptureAsync(
            "final_bridge_status",
            "collect_operation_events",
            "collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunContextMenuCancelRoundTripAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("context_menu.wait_for_long_event_idle", cancellationToken);

        var baselineUiState = await EnsureNoDialogWindowsAsync(context, "context_menu.normalize", cancellationToken);
        var baselineWindowCount = JsonNodeHelpers.ReadInt32(baselineUiState, "windowCount").GetValueOrDefault();

        var colonists = await context.CallGameToolAsync("context_menu.list_colonists", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing current-map colonists for the context-menu cancel roundtrip");

        var pawnName = ResolveFirstColonistName(colonists.StructuredContent);
        var pawnPosition = ResolvePawnPosition(colonists.StructuredContent, pawnName);
        context.Report.ColonistCount = JsonNodeHelpers.ReadInt32(colonists.StructuredContent, "count");

        var selectPawn = await context.CallGameToolAsync("context_menu.select_pawn", "rimworld/select_pawn", new
        {
            pawnName,
            append = false
        }, cancellationToken);
        context.EnsureSucceeded(selectPawn, $"Selecting colonist '{pawnName}' before opening a context menu");

        var observationWindow = await context.BeginObservationWindowAsync("context_menu.snapshot_bridge_status", cancellationToken);

        ToolInvocationResult? openContextMenu = null;
        (int X, int Z) openedCell = default;
        foreach (var candidateCell in BuildNearbyCells(pawnPosition.X, pawnPosition.Z))
        {
            var result = await context.CallGameToolAsync("context_menu.open_context_menu", "rimworld/open_context_menu", new
            {
                x = candidateCell.X,
                z = candidateCell.Z,
                mode = "vanilla"
            }, cancellationToken);
            context.EnsureSucceeded(result, $"Opening a vanilla context menu near colonist '{pawnName}'");

            if (JsonNodeHelpers.ReadInt32(result.StructuredContent, "optionCount").GetValueOrDefault() > 0)
            {
                openContextMenu = result;
                openedCell = candidateCell;
                break;
            }
        }

        if (openContextMenu == null)
            throw new InvalidOperationException($"Could not open a context menu with executable options near colonist '{pawnName}'.");

        var uiStateAfterOpen = await context.CallGameToolAsync("context_menu.get_ui_state_after_open", "rimworld/get_ui_state", new { }, cancellationToken);
        context.EnsureSucceeded(uiStateAfterOpen, "Reading RimWorld UI state after opening a context menu");

        var floatMenuOpenAfterOpen = JsonNodeHelpers.ReadBoolean(uiStateAfterOpen.StructuredContent, "floatMenuOpen") == true;
        if (!floatMenuOpenAfterOpen)
            throw new InvalidOperationException("Opening the context menu did not expose a float menu on the RimWorld window stack.");

        var pressCancel = await context.CallGameToolAsync("context_menu.press_cancel", "rimworld/press_cancel", new { }, cancellationToken);
        context.EnsureSucceeded(pressCancel, "Sending semantic cancel input to close the context menu");

        var afterCancel = JsonNodeHelpers.GetPath(pressCancel.StructuredContent, "after");
        var floatMenuOpenAfterCancel = JsonNodeHelpers.ReadBoolean(afterCancel, "floatMenuOpen") == true;
        var closedWindowTypes = JsonNodeHelpers.ReadArray(pressCancel.StructuredContent, "closedWindowTypes");
        if (floatMenuOpenAfterCancel || closedWindowTypes.Any(type => string.Equals(JsonNodeHelpers.ReadString(type), "Verse.FloatMenu", StringComparison.Ordinal)) == false)
            throw new InvalidOperationException("Semantic cancel input did not close the context-menu float menu.");

        context.SetSummaryValue("selectedPawn", pawnName);
        context.SetSummaryValue("targetCell", $"{openedCell.X},{openedCell.Z}");
        context.SetSummaryValue("openedWindowType", JsonNodeHelpers.ReadString(uiStateAfterOpen.StructuredContent, "topWindowType"));
        context.SetSummaryValue("windowCountBeforeOpen", baselineWindowCount.ToString());
        context.SetSummaryValue("windowCountAfterOpen", JsonNodeHelpers.ReadString(uiStateAfterOpen.StructuredContent, "windowCount"));
        context.SetSummaryValue("windowCountAfterCancel", JsonNodeHelpers.ReadString(afterCancel, "windowCount"));
        context.SetScenarioData("baselineUiState", baselineUiState);
        context.SetScenarioData("openContextMenu", openContextMenu.StructuredContent);
        context.SetScenarioData("uiStateAfterOpen", uiStateAfterOpen.StructuredContent);
        context.SetScenarioData("pressCancel", pressCancel.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "context_menu.final_bridge_status",
            "context_menu.collect_operation_events",
            "context_menu.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunSelectionRoundTripAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("selection.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("selection.snapshot_bridge_status", cancellationToken);

        var colonists = await context.CallGameToolAsync("selection.list_colonists", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing current-map colonists for selection roundtrip");

        var pawnName = ResolveFirstColonistName(colonists.StructuredContent);
        context.Report.ColonistCount = JsonNodeHelpers.ReadInt32(colonists.StructuredContent, "count");
        context.SetSummaryValue("selectedPawn", pawnName);

        var selectPawn = await context.CallGameToolAsync("selection.select_pawn", "rimworld/select_pawn", new
        {
            pawnName,
            append = false
        }, cancellationToken);
        context.EnsureSucceeded(selectPawn, $"Selecting colonist '{pawnName}'");
        var selectedCount = JsonNodeHelpers.ReadInt32(selectPawn.StructuredContent, "selectedCount");
        if (selectedCount != 1)
            throw new InvalidOperationException($"Selecting colonist '{pawnName}' did not produce exactly one selected pawn.");

        var selectedPawnName = JsonNodeHelpers.ReadString(selectPawn.StructuredContent, "selected", "name");
        if (!string.Equals(selectedPawnName, pawnName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The selected colonist response did not match '{pawnName}'.");

        var jumpCamera = await context.CallGameToolAsync("selection.jump_camera_to_pawn", "rimworld/jump_camera_to_pawn", new
        {
            pawnName
        }, cancellationToken);
        context.EnsureSucceeded(jumpCamera, $"Jumping camera to colonist '{pawnName}'");

        var cameraState = await context.CallGameToolAsync("selection.get_camera_state", "rimworld/get_camera_state", new { }, cancellationToken);
        context.EnsureSucceeded(cameraState, "Reading camera state after jumping to the selected pawn");

        var clearSelection = await context.CallGameToolAsync("selection.clear_selection", "rimworld/clear_selection", new { }, cancellationToken);
        context.EnsureSucceeded(clearSelection, "Clearing the current selection");
        if (JsonNodeHelpers.ReadInt32(clearSelection.StructuredContent, "selectedCount") != 0)
            throw new InvalidOperationException("Clearing the current selection did not leave RimWorld with zero selected pawns.");

        context.SetSummaryValue("cameraMap", JsonNodeHelpers.ReadString(cameraState.StructuredContent, "map"));
        context.SetSummaryValue("cameraRootSize", JsonNodeHelpers.ReadString(cameraState.StructuredContent, "rootSize"));
        context.SetSummaryValue("selectionCleared", "true");
        context.SetScenarioData("selectedPawn", JsonValue.Create(pawnName));
        context.SetScenarioData("cameraState", cameraState.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "selection.final_bridge_status",
            "selection.collect_operation_events",
            "selection.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunSaveLoadRoundTripAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("save_load.wait_for_long_event_idle_before_save", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("save_load.snapshot_bridge_status", cancellationToken);

        var saveGame = await context.CallGameToolAsync("save_load.save_game", "rimworld/save_game", new
        {
            saveName = SaveLoadRoundTripSaveName
        }, cancellationToken);
        context.EnsureSucceeded(saveGame, $"Saving RimWorld game to '{SaveLoadRoundTripSaveName}'");

        var savePath = JsonNodeHelpers.ReadString(saveGame.StructuredContent, "path");
        var saveExists = JsonNodeHelpers.ReadBoolean(saveGame.StructuredContent, "exists");
        var sizeBytes = JsonNodeHelpers.ReadInt64(saveGame.StructuredContent, "sizeBytes");
        if (string.IsNullOrWhiteSpace(savePath) || saveExists != true || sizeBytes.GetValueOrDefault() <= 0)
            throw new InvalidOperationException($"Save '{SaveLoadRoundTripSaveName}' did not produce a valid on-disk artifact.");

        var listSaves = await context.CallGameToolAsync("save_load.list_saves", "rimworld/list_saves", new { }, cancellationToken);
        context.EnsureSucceeded(listSaves, "Listing saves after writing the live smoke save");
        if (!SaveListContains(listSaves.StructuredContent, SaveLoadRoundTripSaveName))
            throw new InvalidOperationException($"Save '{SaveLoadRoundTripSaveName}' was not returned by rimworld/list_saves after being written.");

        var loadGame = await context.CallGameToolAsync("save_load.load_game", "rimworld/load_game", new
        {
            saveName = SaveLoadRoundTripSaveName
        }, cancellationToken);
        context.EnsureSucceeded(loadGame, $"Loading RimWorld save '{SaveLoadRoundTripSaveName}'");

        await context.WaitForLongEventIdleAsync("save_load.wait_for_long_event_idle_after_load", cancellationToken);
        await context.WaitForGameLoadedAsync("save_load.wait_for_game_loaded_after_load", cancellationToken);

        var colonists = await context.CallGameToolAsync("save_load.list_colonists_after_load", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing colonists after reloading the save");

        context.Report.ColonistCount = JsonNodeHelpers.ReadInt32(colonists.StructuredContent, "count");
        if (context.Report.ColonistCount.GetValueOrDefault() <= 0)
            throw new InvalidOperationException($"Reloading save '{SaveLoadRoundTripSaveName}' did not restore any current-map colonists.");

        context.SetSummaryValue("saveName", SaveLoadRoundTripSaveName);
        context.SetSummaryValue("savePath", savePath);
        context.SetSummaryValue("saveSizeBytes", sizeBytes?.ToString() ?? "0");
        context.SetScenarioData("saveGame", saveGame.StructuredContent);
        context.SetScenarioData("listSaves", JsonNodeHelpers.GetPath(listSaves.StructuredContent, "saves"));
        context.SetScenarioData("colonistsAfterLoad", JsonNodeHelpers.GetPath(colonists.StructuredContent, "colonists"));

        var observation = await observationWindow.CaptureAsync(
            "save_load.final_bridge_status",
            "save_load.collect_operation_events",
            "save_load.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunScreenshotCaptureAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);

        var colonists = await context.CallGameToolAsync("screenshot.list_colonists", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing current-map colonists before capturing a screenshot");

        var pawnName = ResolveFirstColonistName(colonists.StructuredContent);
        context.Report.ColonistCount = JsonNodeHelpers.ReadInt32(colonists.StructuredContent, "count");

        var jumpCamera = await context.CallGameToolAsync("screenshot.jump_camera_to_pawn", "rimworld/jump_camera_to_pawn", new
        {
            pawnName
        }, cancellationToken);
        context.EnsureSucceeded(jumpCamera, $"Jumping camera to colonist '{pawnName}' before capturing a screenshot");

        await context.WaitForLongEventIdleAsync("screenshot.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("screenshot.snapshot_bridge_status", cancellationToken);
        var screenshotFileName = BuildScreenshotFileName(context.Report.StartedAtUtc);

        var screenshot = await context.CallGameToolAsync("screenshot.take_screenshot", "rimworld/take_screenshot", new
        {
            fileName = screenshotFileName
        }, cancellationToken);
        context.EnsureSucceeded(screenshot, $"Capturing screenshot '{screenshotFileName}'");

        var screenshotPath = JsonNodeHelpers.ReadString(screenshot.StructuredContent, "path");
        var screenshotSizeBytes = JsonNodeHelpers.ReadInt64(screenshot.StructuredContent, "sizeBytes");
        if (string.IsNullOrWhiteSpace(screenshotPath) || screenshotSizeBytes.GetValueOrDefault() <= 0)
            throw new InvalidOperationException($"Screenshot '{screenshotFileName}' did not report a valid file artifact.");

        context.SetSummaryValue("selectedPawn", pawnName);
        context.SetSummaryValue("screenshotFileName", screenshotFileName);
        context.SetSummaryValue("screenshotPath", screenshotPath);
        context.SetSummaryValue("screenshotSizeBytes", screenshotSizeBytes?.ToString() ?? "0");
        context.SetScenarioData("camera", JsonNodeHelpers.GetPath(jumpCamera.StructuredContent, "camera"));
        context.SetScenarioData("screenshot", screenshot.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "screenshot.final_bridge_status",
            "screenshot.collect_operation_events",
            "screenshot.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static string ResolveFirstColonistName(JsonNode? structuredContent)
    {
        var colonists = JsonNodeHelpers.ReadArray(structuredContent, "colonists");
        foreach (var colonist in colonists)
        {
            var name = JsonNodeHelpers.ReadString(colonist, "name");
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        throw new InvalidOperationException("The current map did not expose any colonists that could be used for the selection roundtrip scenario.");
    }

    private static (int X, int Z) ResolvePawnPosition(JsonNode? structuredContent, string pawnName)
    {
        var colonists = JsonNodeHelpers.ReadArray(structuredContent, "colonists");
        foreach (var colonist in colonists)
        {
            if (!string.Equals(JsonNodeHelpers.ReadString(colonist, "name"), pawnName, StringComparison.OrdinalIgnoreCase))
                continue;

            var x = JsonNodeHelpers.ReadInt32(colonist, "position", "x");
            var z = JsonNodeHelpers.ReadInt32(colonist, "position", "z");
            if (x.HasValue && z.HasValue)
                return (x.Value, z.Value);
        }

        throw new InvalidOperationException($"Could not resolve a current-map position for colonist '{pawnName}'.");
    }

    private static IEnumerable<(int X, int Z)> BuildNearbyCells(int x, int z)
    {
        yield return (x + 1, z);
        yield return (x - 1, z);
        yield return (x, z + 1);
        yield return (x, z - 1);
        yield return (x + 2, z);
        yield return (x, z + 2);
    }

    private static bool SaveListContains(JsonNode? structuredContent, string saveName)
    {
        var saves = JsonNodeHelpers.ReadArray(structuredContent, "saves");
        return saves.Any(save => string.Equals(JsonNodeHelpers.ReadString(save, "name"), saveName, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<JsonNode?> EnsureNoDialogWindowsAsync(SmokeScenarioContext context, string stepPrefix, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var uiState = await context.CallGameToolAsync(
                $"{stepPrefix}.get_ui_state_{attempt}",
                "rimworld/get_ui_state",
                new { },
                cancellationToken);
            context.EnsureSucceeded(uiState, "Reading RimWorld UI state before the input smoke scenario");

            if (JsonNodeHelpers.ReadBoolean(uiState.StructuredContent, "nonImmediateDialogWindowOpen") != true)
                return uiState.StructuredContent;

            var topWindowType = JsonNodeHelpers.ReadString(uiState.StructuredContent, "topWindowType");
            if (string.Equals(topWindowType, "LudeonTK.Dialog_DevPalette", StringComparison.OrdinalIgnoreCase)
                || string.Equals(topWindowType, "Dialog_DevPalette", StringComparison.OrdinalIgnoreCase))
            {
                context.Note($"Closing the dev palette before the input roundtrip scenario (attempt {attempt}).");
                var closeWindow = await context.CallGameToolAsync(
                    $"{stepPrefix}.close_window_{attempt}",
                    "rimworld/close_window",
                    new
                    {
                        windowType = "Dialog_DevPalette"
                    },
                    cancellationToken);
                context.EnsureSucceeded(closeWindow, "Closing the pre-existing RimWorld dev palette before the input smoke scenario");
                continue;
            }

            context.Note($"Dismissing a pre-existing dialog window before the input roundtrip scenario (attempt {attempt}).");
            var pressCancel = await context.CallGameToolAsync(
                $"{stepPrefix}.press_cancel_{attempt}",
                "rimworld/press_cancel",
                new { },
                cancellationToken);
            context.EnsureSucceeded(pressCancel, "Closing a pre-existing RimWorld dialog before the input smoke scenario");
        }

        var finalState = await context.CallGameToolAsync(
            $"{stepPrefix}.get_ui_state_final",
            "rimworld/get_ui_state",
            new { },
            cancellationToken);
        context.EnsureSucceeded(finalState, "Reading final RimWorld UI state after dismissing pre-existing dialogs");

        if (JsonNodeHelpers.ReadBoolean(finalState.StructuredContent, "nonImmediateDialogWindowOpen") == true)
            throw new InvalidOperationException("Could not reach a clean UI state before starting the input roundtrip scenario.");

        return finalState.StructuredContent;
    }

    private static string BuildScreenshotFileName(DateTimeOffset startedAtUtc)
    {
        return $"rimbridge_live_smoke_{startedAtUtc:yyyyMMdd_HHmmss}";
    }
}
