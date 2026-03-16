using System.Buffers.Binary;
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
    public const string DebugActionDiscoveryScenarioName = "debug-action-discovery";
    public const string ScreenTargetClickRoundTripScenarioName = "screen-target-click-roundtrip";
    public const string ScreenTargetClipScenarioName = "screen-target-clip";
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
            [DebugActionDiscoveryScenarioName] = new SmokeScenarioDefinition
            {
                Name = DebugActionDiscoveryScenarioName,
                Description = "Ensure a playable game exists, discover debug-action roots and children, then execute one low-side-effect direct leaf action by stable path.",
                RunAsync = RunDebugActionDiscoveryAsync
            },
            [ScreenTargetClickRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = ScreenTargetClickRoundTripScenarioName,
                Description = "Ensure a playable game exists, then click real dismiss and option target ids returned by rimworld/get_screen_targets.",
                RunAsync = RunScreenTargetClickRoundTripAsync
            },
            [ScreenTargetClipScenarioName] = new SmokeScenarioDefinition
            {
                Name = ScreenTargetClipScenarioName,
                Description = "Ensure a playable game exists, then capture a screenshot clipped to a real target id returned by rimworld/get_screen_targets.",
                RunAsync = RunScreenTargetClipAsync
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

        if (context.HumanVerificationEnabled)
        {
            await context.CaptureHumanVerificationScreenshotAsync(
                "human_verify.debug_game_loaded",
                "debug_game_loaded",
                "Loaded debug colony after the harness waited for the game to become playable.",
                [
                    "A playable RimWorld colony should be visible on the map.",
                    "At least one colonist should be present on screen or selectable in the colony.",
                    "This image is taken after the harness verified that a debug quick-test colony finished loading."
                ],
                cancellationToken);
        }

        context.SetScenarioData("colonists", JsonNodeHelpers.GetPath(colonists.StructuredContent, "colonists"));

        var observation = await observationWindow.CaptureAsync(
            "final_bridge_status",
            "collect_operation_events",
            "collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunDebugActionDiscoveryAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("debug_actions.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("debug_actions.snapshot_bridge_status", cancellationToken);

        var roots = await context.CallGameToolAsync("debug_actions.list_roots", "rimworld/list_debug_action_roots", new
        {
            includeHidden = false
        }, cancellationToken);
        context.EnsureSucceeded(roots, "Listing RimWorld debug-action roots");

        var rootArray = JsonNodeHelpers.ReadArray(roots.StructuredContent, "roots");
        if (rootArray.Count == 0)
            throw new InvalidOperationException("Debug-action discovery returned no visible roots.");

        var outputRootPath = ResolveDebugActionRootPath(rootArray, "Outputs");
        var outputChildren = await context.CallGameToolAsync("debug_actions.list_output_children", "rimworld/list_debug_action_children", new
        {
            path = outputRootPath,
            includeHidden = false
        }, cancellationToken);
        context.EnsureSucceeded(outputChildren, $"Listing debug-action children under '{outputRootPath}'");

        var outputChildArray = JsonNodeHelpers.ReadArray(outputChildren.StructuredContent, "children");
        if (outputChildArray.Count == 0)
            throw new InvalidOperationException($"Debug-action root '{outputRootPath}' did not expose any visible child nodes.");

        var executablePath = ResolveExecutableDebugActionPath(outputChildArray);
        var executableNode = await context.CallGameToolAsync("debug_actions.get_executable", "rimworld/get_debug_action", new
        {
            path = executablePath,
            includeChildren = false
        }, cancellationToken);
        context.EnsureSucceeded(executableNode, $"Reading debug-action metadata for '{executablePath}'");

        if (JsonNodeHelpers.ReadBoolean(executableNode.StructuredContent, "node", "execution", "supported") != true)
            throw new InvalidOperationException($"Debug action '{executablePath}' was not reported as directly executable.");

        var execute = await context.CallGameToolAsync("debug_actions.execute", "rimworld/execute_debug_action", new
        {
            path = executablePath
        }, cancellationToken);
        context.EnsureSucceeded(execute, $"Executing debug action '{executablePath}'");

        var executedPath = JsonNodeHelpers.ReadString(execute.StructuredContent, "path");
        if (!string.Equals(executablePath, executedPath, StringComparison.Ordinal))
            throw new InvalidOperationException($"Debug action execution returned path '{executedPath}' instead of '{executablePath}'.");

        var executeEffects = JsonNodeHelpers.GetPath(execute.StructuredContent, "effects");
        if (executeEffects == null)
            throw new InvalidOperationException($"Debug action '{executablePath}' did not return execution effects.");

        var settingsRootPath = ResolveDebugActionRootPath(rootArray, "Settings");
        var settingChildren = await context.CallGameToolAsync("debug_actions.list_setting_children", "rimworld/list_debug_action_children", new
        {
            path = settingsRootPath,
            includeHidden = false
        }, cancellationToken);
        context.EnsureSucceeded(settingChildren, $"Listing debug-action children under '{settingsRootPath}'");

        var settingChildArray = JsonNodeHelpers.ReadArray(settingChildren.StructuredContent, "children");
        var settingPath = ResolveSafeDebugSettingPath(settingChildArray);
        var settingNode = await context.CallGameToolAsync("debug_actions.get_setting", "rimworld/get_debug_action", new
        {
            path = settingPath,
            includeChildren = false
        }, cancellationToken);
        context.EnsureSucceeded(settingNode, $"Reading debug setting '{settingPath}'");

        var originalSettingValue = JsonNodeHelpers.ReadBoolean(settingNode.StructuredContent, "node", "on") == true;

        var flipSetting = await context.CallGameToolAsync("debug_actions.flip_setting", "rimworld/set_debug_setting", new
        {
            path = settingPath,
            enabled = !originalSettingValue
        }, cancellationToken);
        context.EnsureSucceeded(flipSetting, $"Setting debug toggle '{settingPath}' to '{!originalSettingValue}'");

        if (JsonNodeHelpers.ReadBoolean(flipSetting.StructuredContent, "value") != !originalSettingValue)
            throw new InvalidOperationException($"Debug setting '{settingPath}' did not report the requested value after the first toggle.");

        var restoreSetting = await context.CallGameToolAsync("debug_actions.restore_setting", "rimworld/set_debug_setting", new
        {
            path = settingPath,
            enabled = originalSettingValue
        }, cancellationToken);
        context.EnsureSucceeded(restoreSetting, $"Restoring debug toggle '{settingPath}' to '{originalSettingValue}'");

        if (JsonNodeHelpers.ReadBoolean(restoreSetting.StructuredContent, "value") != originalSettingValue)
            throw new InvalidOperationException($"Debug setting '{settingPath}' did not restore its original value.");

        context.SetSummaryValue("outputRootPath", outputRootPath);
        context.SetSummaryValue("executedPath", executablePath);
        context.SetSummaryValue("settingsRootPath", settingsRootPath);
        context.SetSummaryValue("settingPath", settingPath);
        context.SetSummaryValue("rootCount", rootArray.Count.ToString());
        context.SetSummaryValue("outputChildCount", outputChildArray.Count.ToString());
        context.SetSummaryValue("settingChildCount", settingChildArray.Count.ToString());
        context.SetScenarioData("roots", new JsonArray(rootArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
        context.SetScenarioData("outputChildren", new JsonArray(outputChildArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
        context.SetScenarioData("settingChildren", new JsonArray(settingChildArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
        context.SetScenarioData("execute", execute.StructuredContent);
        context.SetScenarioData("flipSetting", flipSetting.StructuredContent);
        context.SetScenarioData("restoreSetting", restoreSetting.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "debug_actions.final_bridge_status",
            "debug_actions.collect_operation_events",
            "debug_actions.collect_logs",
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

        var openContextMenuResult = await OpenVanillaContextMenuNearPawnAsync(
            context,
            "context_menu",
            pawnName,
            pawnPosition,
            cancellationToken);
        var openContextMenu = openContextMenuResult.OpenContextMenu;
        var openedCell = openContextMenuResult.Cell;

        var uiStateAfterOpen = await context.CallGameToolAsync("context_menu.get_ui_state_after_open", "rimworld/get_ui_state", new { }, cancellationToken);
        context.EnsureSucceeded(uiStateAfterOpen, "Reading RimWorld UI state after opening a context menu");

        var floatMenuOpenAfterOpen = JsonNodeHelpers.ReadBoolean(uiStateAfterOpen.StructuredContent, "floatMenuOpen") == true;
        if (!floatMenuOpenAfterOpen)
            throw new InvalidOperationException("Opening the context menu did not expose a float menu on the RimWorld window stack.");

        var screenTargets = await context.CallGameToolAsync("context_menu.get_screen_targets", "rimworld/get_screen_targets", new { }, cancellationToken);
        context.EnsureSucceeded(screenTargets, "Reading screen-space target metadata after opening a context menu");
        var contextMenuTargets = JsonNodeHelpers.GetPath(screenTargets.StructuredContent, "targets", "contextMenu");
        if (contextMenuTargets == null)
            throw new InvalidOperationException("Screen target metadata did not expose the active context menu.");

        var targetOptionCount = JsonNodeHelpers.ReadInt32(contextMenuTargets, "optionCount").GetValueOrDefault();
        var openedOptionCount = JsonNodeHelpers.ReadInt32(openContextMenu.StructuredContent, "optionCount").GetValueOrDefault();
        if (targetOptionCount != openedOptionCount)
            throw new InvalidOperationException("Screen target metadata did not report the same number of context-menu options as the open-context-menu call.");

        var optionTargets = JsonNodeHelpers.ReadArray(contextMenuTargets, "options");
        var firstOptionRect = optionTargets.Count > 0 ? JsonNodeHelpers.GetPath(optionTargets[0], "rect") : null;
        if (firstOptionRect == null
            || JsonNodeHelpers.ReadDouble(firstOptionRect, "width").GetValueOrDefault() <= 0
            || JsonNodeHelpers.ReadDouble(firstOptionRect, "height").GetValueOrDefault() <= 0)
        {
            throw new InvalidOperationException("Context-menu target metadata did not include a valid rect for the first option.");
        }

        if (context.HumanVerificationEnabled)
        {
            await context.CaptureHumanVerificationScreenshotAsync(
                "human_verify.context_menu_open",
                "context_menu_open",
                "Active float menu before the harness closes it with semantic cancel input.",
                [
                    $"The colonist '{pawnName}' should be selected.",
                    "A float menu should be visible near the upper-left area of the game view.",
                    "This image is the state the harness validates before sending rimworld/press_cancel."
                ],
                cancellationToken);
        }

        var pressCancel = await context.CallGameToolAsync("context_menu.press_cancel", "rimworld/press_cancel", new { }, cancellationToken);
        context.EnsureSucceeded(pressCancel, "Sending semantic cancel input to close the context menu");

        var afterCancel = JsonNodeHelpers.GetPath(pressCancel.StructuredContent, "after");
        var floatMenuOpenAfterCancel = JsonNodeHelpers.ReadBoolean(afterCancel, "floatMenuOpen") == true;
        var closedWindowTypes = JsonNodeHelpers.ReadArray(pressCancel.StructuredContent, "closedWindowTypes");
        if (floatMenuOpenAfterCancel || closedWindowTypes.Any(type => string.Equals(JsonNodeHelpers.ReadString(type), "Verse.FloatMenu", StringComparison.Ordinal)) == false)
            throw new InvalidOperationException("Semantic cancel input did not close the context-menu float menu.");

        if (context.HumanVerificationEnabled)
        {
            await context.CaptureHumanVerificationScreenshotAsync(
                "human_verify.context_menu_after_cancel",
                "context_menu_after_cancel",
                "Scene immediately after semantic cancel input closed the float menu.",
                [
                    "The float menu should be gone.",
                    $"The colonist '{pawnName}' should still be selected.",
                    "This confirms the menu was dismissed without depending on desktop focus."
                ],
                cancellationToken);
        }

        context.SetSummaryValue("selectedPawn", pawnName);
        context.SetSummaryValue("targetCell", $"{openedCell.X},{openedCell.Z}");
        context.SetSummaryValue("openedWindowType", JsonNodeHelpers.ReadString(uiStateAfterOpen.StructuredContent, "topWindowType"));
        context.SetSummaryValue("windowCountBeforeOpen", baselineWindowCount.ToString());
        context.SetSummaryValue("windowCountAfterOpen", JsonNodeHelpers.ReadString(uiStateAfterOpen.StructuredContent, "windowCount"));
        context.SetSummaryValue("windowCountAfterCancel", JsonNodeHelpers.ReadString(afterCancel, "windowCount"));
        context.SetScenarioData("baselineUiState", baselineUiState);
        context.SetScenarioData("openContextMenu", openContextMenu.StructuredContent);
        context.SetScenarioData("uiStateAfterOpen", uiStateAfterOpen.StructuredContent);
        context.SetScenarioData("screenTargets", JsonNodeHelpers.GetPath(screenTargets.StructuredContent, "targets"));
        context.SetScenarioData("pressCancel", pressCancel.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "context_menu.final_bridge_status",
            "context_menu.collect_operation_events",
            "context_menu.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunScreenTargetClickRoundTripAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("target_click.wait_for_long_event_idle", cancellationToken);

        await EnsureNoDialogWindowsAsync(context, "target_click.normalize", cancellationToken);

        var colonists = await context.CallGameToolAsync("target_click.list_colonists", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing current-map colonists for the screen-target click roundtrip");

        var pawnName = ResolveFirstColonistName(colonists.StructuredContent);
        var pawnPosition = ResolvePawnPosition(colonists.StructuredContent, pawnName);
        context.Report.ColonistCount = JsonNodeHelpers.ReadInt32(colonists.StructuredContent, "count");

        var selectPawn = await context.CallGameToolAsync("target_click.select_pawn", "rimworld/select_pawn", new
        {
            pawnName,
            append = false
        }, cancellationToken);
        context.EnsureSucceeded(selectPawn, $"Selecting colonist '{pawnName}' before clicking screen targets");

        var observationWindow = await context.BeginObservationWindowAsync("target_click.snapshot_bridge_status", cancellationToken);

        var dismissMenu = await OpenVanillaContextMenuNearPawnAsync(
            context,
            "target_click.dismiss",
            pawnName,
            pawnPosition,
            cancellationToken);
        var dismissTargets = await context.CallGameToolAsync("target_click.dismiss.get_screen_targets", "rimworld/get_screen_targets", new { }, cancellationToken);
        context.EnsureSucceeded(dismissTargets, "Reading screen targets before clicking a dismiss target");

        var dismissContextMenuTargets = JsonNodeHelpers.GetPath(dismissTargets.StructuredContent, "targets", "contextMenu");
        if (dismissContextMenuTargets == null)
            throw new InvalidOperationException("Screen target metadata did not expose the active context menu before clicking its dismiss target.");

        var dismissTargetId = JsonNodeHelpers.ReadString(dismissContextMenuTargets, "dismissTargetId");
        if (string.IsNullOrWhiteSpace(dismissTargetId))
            throw new InvalidOperationException("Screen target metadata did not expose a dismiss target id for the active context menu.");

        if (context.HumanVerificationEnabled)
        {
            await context.CaptureHumanVerificationScreenshotAsync(
                "human_verify.screen_target_dismiss_open",
                "screen_target_dismiss_open",
                "Float menu just before the harness clicks its dismiss target id.",
                [
                    "A float menu should be visible.",
                    "The next automated step clicks the menu's dismissTargetId instead of sending a generic cancel input.",
                    "This shows the exact UI state referenced by the actionable target metadata."
                ],
                cancellationToken);
        }

        var clickDismiss = await context.CallGameToolAsync("target_click.dismiss.click_screen_target", "rimworld/click_screen_target", new
        {
            targetId = dismissTargetId
        }, cancellationToken);
        context.EnsureSucceeded(clickDismiss, "Clicking the context-menu dismiss target");

        if (JsonNodeHelpers.ReadBoolean(clickDismiss.StructuredContent, "after", "floatMenuOpen") == true)
            throw new InvalidOperationException("Clicking the dismiss target did not close the float menu.");
        if (!string.Equals(JsonNodeHelpers.ReadString(clickDismiss.StructuredContent, "targetKind"), "window_dismiss", StringComparison.Ordinal))
            throw new InvalidOperationException("Clicking the dismiss target did not report a window_dismiss target kind.");
        if (!string.Equals(JsonNodeHelpers.ReadString(clickDismiss.StructuredContent, "actionKind"), "dismiss_window", StringComparison.Ordinal))
            throw new InvalidOperationException("Clicking the dismiss target did not report a dismiss_window action kind.");

        var dismissClosedWindowTypes = JsonNodeHelpers.ReadArray(clickDismiss.StructuredContent, "closedWindowTypes");
        if (!dismissClosedWindowTypes.Any(type => string.Equals(JsonNodeHelpers.ReadString(type), "Verse.FloatMenu", StringComparison.Ordinal)))
            throw new InvalidOperationException("Clicking the dismiss target did not close a Verse.FloatMenu window.");

        var optionMenu = await OpenVanillaContextMenuNearPawnAsync(
            context,
            "target_click.option",
            pawnName,
            pawnPosition,
            cancellationToken,
            requireDirectExecutableOption: true);
        var optionTargets = await context.CallGameToolAsync("target_click.option.get_screen_targets", "rimworld/get_screen_targets", new { }, cancellationToken);
        context.EnsureSucceeded(optionTargets, "Reading screen targets before clicking a context-menu option target");

        var optionContextMenuTargets = JsonNodeHelpers.GetPath(optionTargets.StructuredContent, "targets", "contextMenu");
        if (optionContextMenuTargets == null)
            throw new InvalidOperationException("Screen target metadata did not expose the active context menu before clicking an option target.");

        var optionTargetNodes = JsonNodeHelpers.ReadArray(optionContextMenuTargets, "options");
        if (optionMenu.DirectOptionIndex <= 0 || optionMenu.DirectOptionIndex > optionTargetNodes.Count)
            throw new InvalidOperationException("Could not align the resolved executable menu option with the current screen target metadata.");

        var optionTarget = optionTargetNodes[optionMenu.DirectOptionIndex - 1];
        var optionTargetId = JsonNodeHelpers.ReadString(optionTarget, "targetId");
        var optionLabel = JsonNodeHelpers.ReadString(optionTarget, "label");
        if (string.IsNullOrWhiteSpace(optionTargetId))
            throw new InvalidOperationException("Screen target metadata did not expose a target id for the executable context-menu option.");

        if (context.HumanVerificationEnabled)
        {
            await context.CaptureHumanVerificationScreenshotAsync(
                "human_verify.screen_target_option_open",
                "screen_target_option_open",
                "Reopened float menu just before the harness clicks a specific context-menu option target id.",
                [
                    "A float menu should be visible again.",
                    $"The harness is about to click the option labeled '{optionLabel}' through rimworld/click_screen_target.",
                    "This demonstrates target-id-based action dispatch rather than generic UI input."
                ],
                cancellationToken);
        }

        var clickOption = await context.CallGameToolAsync("target_click.option.click_screen_target", "rimworld/click_screen_target", new
        {
            targetId = optionTargetId
        }, cancellationToken);
        context.EnsureSucceeded(clickOption, $"Clicking context-menu option target '{optionLabel}'");

        if (JsonNodeHelpers.ReadBoolean(clickOption.StructuredContent, "after", "floatMenuOpen") == true)
            throw new InvalidOperationException("Clicking the context-menu option target did not close the float menu.");
        if (!string.Equals(JsonNodeHelpers.ReadString(clickOption.StructuredContent, "targetKind"), "context_menu_option", StringComparison.Ordinal))
            throw new InvalidOperationException("Clicking the option target did not report a context_menu_option target kind.");
        if (!string.Equals(JsonNodeHelpers.ReadString(clickOption.StructuredContent, "actionKind"), "execute_context_menu_option", StringComparison.Ordinal))
            throw new InvalidOperationException("Clicking the option target did not report an execute_context_menu_option action kind.");
        if (JsonNodeHelpers.ReadInt32(clickOption.StructuredContent, "executedOptionIndex") != optionMenu.DirectOptionIndex)
            throw new InvalidOperationException("Clicking the option target did not execute the expected menu option index.");
        if (!string.Equals(JsonNodeHelpers.ReadString(clickOption.StructuredContent, "executedLabel"), optionLabel, StringComparison.Ordinal))
            throw new InvalidOperationException("Clicking the option target did not report the expected menu option label.");

        await context.WaitForLongEventIdleAsync("target_click.wait_for_long_event_idle_after_option_click", cancellationToken);

        if (context.HumanVerificationEnabled)
        {
            await context.CaptureHumanVerificationScreenshotAsync(
                "human_verify.screen_target_after_option_click",
                "screen_target_after_option_click",
                "Scene after the harness clicked the specific context-menu option target id.",
                [
                    "The float menu should now be closed.",
                    $"The clicked option was '{optionLabel}'.",
                    "This image is captured after the target-id click path completed and the game returned to idle."
                ],
                cancellationToken);
        }

        context.SetSummaryValue("selectedPawn", pawnName);
        context.SetSummaryValue("dismissTargetId", dismissTargetId);
        context.SetSummaryValue("dismissCell", $"{dismissMenu.Cell.X},{dismissMenu.Cell.Z}");
        context.SetSummaryValue("optionTargetId", optionTargetId);
        context.SetSummaryValue("optionCell", $"{optionMenu.Cell.X},{optionMenu.Cell.Z}");
        context.SetSummaryValue("executedOptionLabel", optionLabel);
        context.SetScenarioData("dismissScreenTargets", JsonNodeHelpers.GetPath(dismissTargets.StructuredContent, "targets"));
        context.SetScenarioData("dismissClick", clickDismiss.StructuredContent);
        context.SetScenarioData("optionScreenTargets", JsonNodeHelpers.GetPath(optionTargets.StructuredContent, "targets"));
        context.SetScenarioData("optionClick", clickOption.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "target_click.final_bridge_status",
            "target_click.collect_operation_events",
            "target_click.collect_logs",
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

    private static async Task RunScreenTargetClipAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("target_clip.wait_for_long_event_idle", cancellationToken);

        await EnsureNoDialogWindowsAsync(context, "target_clip.normalize", cancellationToken);

        var colonists = await context.CallGameToolAsync("target_clip.list_colonists", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing current-map colonists for the screen-target clip scenario");

        var pawnName = ResolveFirstColonistName(colonists.StructuredContent);
        var pawnPosition = ResolvePawnPosition(colonists.StructuredContent, pawnName);
        context.Report.ColonistCount = JsonNodeHelpers.ReadInt32(colonists.StructuredContent, "count");

        var selectPawn = await context.CallGameToolAsync("target_clip.select_pawn", "rimworld/select_pawn", new
        {
            pawnName,
            append = false
        }, cancellationToken);
        context.EnsureSucceeded(selectPawn, $"Selecting colonist '{pawnName}' before clipping a target screenshot");

        var observationWindow = await context.BeginObservationWindowAsync("target_clip.snapshot_bridge_status", cancellationToken);

        var optionMenu = await OpenVanillaContextMenuNearPawnAsync(
            context,
            "target_clip.option",
            pawnName,
            pawnPosition,
            cancellationToken,
            requireDirectExecutableOption: true);
        var optionTargets = await context.CallGameToolAsync("target_clip.option.get_screen_targets", "rimworld/get_screen_targets", new { }, cancellationToken);
        context.EnsureSucceeded(optionTargets, "Reading screen targets before taking a clipped screenshot");

        var optionContextMenuTargets = JsonNodeHelpers.GetPath(optionTargets.StructuredContent, "targets", "contextMenu");
        if (optionContextMenuTargets == null)
            throw new InvalidOperationException("Screen target metadata did not expose the active context menu before clipping a screenshot.");

        var optionTargetNodes = JsonNodeHelpers.ReadArray(optionContextMenuTargets, "options");
        if (optionMenu.DirectOptionIndex <= 0 || optionMenu.DirectOptionIndex > optionTargetNodes.Count)
            throw new InvalidOperationException("Could not align the executable context-menu option with the current screen target metadata for clipping.");

        var optionTarget = optionTargetNodes[optionMenu.DirectOptionIndex - 1];
        var optionTargetId = JsonNodeHelpers.ReadString(optionTarget, "targetId");
        var optionLabel = JsonNodeHelpers.ReadString(optionTarget, "label");
        var optionRect = JsonNodeHelpers.GetPath(optionTarget, "rect");
        if (string.IsNullOrWhiteSpace(optionTargetId) || optionRect == null)
            throw new InvalidOperationException("Screen target metadata did not expose a target id and rect for the clip target.");

        var clipPadding = 12;
        var screenshotFileName = BuildScreenshotFileName(context.Report.StartedAtUtc) + "_target_clip";
        var screenshot = await context.CallGameToolAsync("target_clip.take_screenshot", "rimworld/take_screenshot", new
        {
            fileName = screenshotFileName,
            includeTargets = true,
            clipTargetId = optionTargetId,
            clipPadding
        }, cancellationToken);
        context.EnsureSucceeded(screenshot, $"Capturing clipped screenshot for target '{optionTargetId}'");

        var clipped = JsonNodeHelpers.ReadBoolean(screenshot.StructuredContent, "clipped") == true;
        var screenshotPath = JsonNodeHelpers.ReadString(screenshot.StructuredContent, "path");
        var sourcePath = JsonNodeHelpers.ReadString(screenshot.StructuredContent, "sourcePath");
        var clipTargetKind = JsonNodeHelpers.ReadString(screenshot.StructuredContent, "clipTargetKind");
        var clipRect = JsonNodeHelpers.GetPath(screenshot.StructuredContent, "clipRect");
        if (!clipped
            || string.IsNullOrWhiteSpace(screenshotPath)
            || string.IsNullOrWhiteSpace(sourcePath)
            || string.Equals(sourcePath, screenshotPath, StringComparison.Ordinal)
            || clipRect == null)
        {
            throw new InvalidOperationException("The clipped screenshot response did not include the expected clipped path and clip metadata.");
        }

        if (!File.Exists(screenshotPath) || !File.Exists(sourcePath))
            throw new InvalidOperationException("The clipped screenshot response did not point to valid on-disk artifacts.");

        if (!string.Equals(JsonNodeHelpers.ReadString(screenshot.StructuredContent, "clipTargetId"), optionTargetId, StringComparison.Ordinal)
            || !string.Equals(clipTargetKind, "context_menu_option", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The clipped screenshot response did not preserve the requested target identity.");
        }

        var clippedImageSize = ReadPngDimensions(screenshotPath);
        var fullImageSize = ReadPngDimensions(sourcePath);
        var clipWidth = JsonNodeHelpers.ReadInt32(clipRect, "width").GetValueOrDefault();
        var clipHeight = JsonNodeHelpers.ReadInt32(clipRect, "height").GetValueOrDefault();
        if (clippedImageSize.Width != clipWidth || clippedImageSize.Height != clipHeight)
            throw new InvalidOperationException("The clipped screenshot image dimensions did not match the reported clip rect.");
        if (clippedImageSize.Width >= fullImageSize.Width || clippedImageSize.Height >= fullImageSize.Height)
            throw new InvalidOperationException("The clipped screenshot was not smaller than the full source screenshot.");

        if (context.HumanVerificationEnabled)
        {
            await context.ExportHumanVerificationArtifactAsync(
                "screen_target_option_clip",
                screenshotPath,
                "The clipped screenshot artifact produced from a real context-menu option target id.",
                [
                    $"The clipped image should focus on the option labeled '{optionLabel}'.",
                    "Only a small area around the target should be visible because the screenshot was cropped to the target rect plus padding.",
                    "This proves that target-id metadata can drive focused visual assertions without relying on full-frame screenshots."
                ],
                cancellationToken);
        }

        context.SetSummaryValue("selectedPawn", pawnName);
        context.SetSummaryValue("clipTargetId", optionTargetId);
        context.SetSummaryValue("clipTargetKind", clipTargetKind);
        context.SetSummaryValue("clipPadding", clipPadding.ToString());
        context.SetSummaryValue("clipWidth", clipWidth.ToString());
        context.SetSummaryValue("clipHeight", clipHeight.ToString());
        context.SetSummaryValue("clipLabel", optionLabel);
        context.SetScenarioData("optionScreenTargets", JsonNodeHelpers.GetPath(optionTargets.StructuredContent, "targets"));
        context.SetScenarioData("clippedScreenshot", screenshot.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "target_clip.final_bridge_status",
            "target_clip.collect_operation_events",
            "target_clip.collect_logs",
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

        if (context.HumanVerificationEnabled)
        {
            await context.CaptureHumanVerificationScreenshotAsync(
                "human_verify.save_load_after_reload",
                "save_load_after_reload",
                "Loaded colony after the harness completed a save and load roundtrip.",
                [
                    "A playable colony should be visible after the save was loaded back in.",
                    "This screenshot is taken only after the harness waited for idle and verified that colonists exist on the current map.",
                    "If you see the map here, the load finished before any optional cleanup happened."
                ],
                cancellationToken);
        }

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
            fileName = screenshotFileName,
            includeTargets = true
        }, cancellationToken);
        context.EnsureSucceeded(screenshot, $"Capturing screenshot '{screenshotFileName}'");

        var screenshotPath = JsonNodeHelpers.ReadString(screenshot.StructuredContent, "path");
        var screenshotSizeBytes = JsonNodeHelpers.ReadInt64(screenshot.StructuredContent, "sizeBytes");
        if (string.IsNullOrWhiteSpace(screenshotPath) || screenshotSizeBytes.GetValueOrDefault() <= 0)
            throw new InvalidOperationException($"Screenshot '{screenshotFileName}' did not report a valid file artifact.");

        var screenshotTargets = JsonNodeHelpers.GetPath(screenshot.StructuredContent, "screenTargets");
        if (screenshotTargets == null)
            throw new InvalidOperationException($"Screenshot '{screenshotFileName}' did not include screen target metadata.");

        var screenshotWindowCount = JsonNodeHelpers.ReadInt32(screenshotTargets, "uiState", "windowCount").GetValueOrDefault();
        if (screenshotWindowCount <= 0)
            throw new InvalidOperationException($"Screenshot '{screenshotFileName}' did not report any live UI windows in its target metadata.");

        var selectedPawns = JsonNodeHelpers.ReadArray(screenshotTargets, "selectedPawns");
        if (selectedPawns.Count == 0)
            throw new InvalidOperationException($"Screenshot '{screenshotFileName}' did not preserve the current selection in its target metadata.");

        if (context.HumanVerificationEnabled)
        {
            await context.ExportHumanVerificationArtifactAsync(
                "captured_screenshot",
                screenshotPath,
                "The actual image file produced by rimworld/take_screenshot during the live smoke run.",
                [
                    $"The colonist '{pawnName}' should be framed near the center of the image.",
                    "This file was created by the screenshot tool itself and then copied to the Desktop for inspection.",
                    "It should match the reported screenshot artifact from the live test."
                ],
                cancellationToken);
        }

        context.SetSummaryValue("selectedPawn", pawnName);
        context.SetSummaryValue("screenshotFileName", screenshotFileName);
        context.SetSummaryValue("screenshotPath", screenshotPath);
        context.SetSummaryValue("screenshotSizeBytes", screenshotSizeBytes?.ToString() ?? "0");
        context.SetScenarioData("camera", JsonNodeHelpers.GetPath(jumpCamera.StructuredContent, "camera"));
        context.SetScenarioData("screenTargets", screenshotTargets);
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

    private static string ResolveDebugActionRootPath(IReadOnlyList<JsonNode?> roots, string preferredPath)
    {
        foreach (var root in roots)
        {
            var path = JsonNodeHelpers.ReadString(root, "path");
            if (string.Equals(path, preferredPath, StringComparison.Ordinal))
                return path;
        }

        foreach (var root in roots)
        {
            var childCount = JsonNodeHelpers.ReadInt32(root, "visibleChildCount") ?? JsonNodeHelpers.ReadInt32(root, "childCount");
            var path = JsonNodeHelpers.ReadString(root, "path");
            if (!string.IsNullOrWhiteSpace(path) && childCount.GetValueOrDefault() > 0)
                return path;
        }

        throw new InvalidOperationException("No usable debug-action root path was returned by the bridge.");
    }

    private static string ResolveExecutableDebugActionPath(IReadOnlyList<JsonNode?> children)
    {
        foreach (var child in children)
        {
            var path = JsonNodeHelpers.ReadString(child, "path");
            if (string.Equals(path, @"Outputs\Tick Rates", StringComparison.Ordinal))
                return path;
        }

        foreach (var child in children)
        {
            if (JsonNodeHelpers.ReadBoolean(child, "execution", "supported") != true)
                continue;

            var path = JsonNodeHelpers.ReadString(child, "path");
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        throw new InvalidOperationException("The chosen debug-action root did not expose any directly executable child actions.");
    }

    private static string ResolveSafeDebugSettingPath(IReadOnlyList<JsonNode?> children)
    {
        foreach (var child in children)
        {
            var path = JsonNodeHelpers.ReadString(child, "path");
            if (string.Equals(path, @"Settings\Show Architect Menu Order", StringComparison.Ordinal))
                return path;
        }

        foreach (var child in children)
        {
            var path = JsonNodeHelpers.ReadString(child, "path");
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (JsonNodeHelpers.ReadBoolean(child, "hasSettingsToggle") != true)
                continue;
            if (path.StartsWith(@"Settings\Show ", StringComparison.Ordinal))
                return path;
        }

        throw new InvalidOperationException("The Settings tab did not expose a safe toggle suitable for the debug-action smoke scenario.");
    }

    private static async Task<(ToolInvocationResult OpenContextMenu, (int X, int Z) Cell, int DirectOptionIndex)> OpenVanillaContextMenuNearPawnAsync(
        SmokeScenarioContext context,
        string stepPrefix,
        string pawnName,
        (int X, int Z) pawnPosition,
        CancellationToken cancellationToken,
        bool requireDirectExecutableOption = false)
    {
        var attempt = 0;
        foreach (var candidateCell in BuildNearbyCells(pawnPosition.X, pawnPosition.Z))
        {
            attempt++;
            var result = await context.CallGameToolAsync($"{stepPrefix}.open_context_menu_{attempt}", "rimworld/open_context_menu", new
            {
                x = candidateCell.X,
                z = candidateCell.Z,
                mode = "vanilla"
            }, cancellationToken);
            context.EnsureSucceeded(result, $"Opening a vanilla context menu near colonist '{pawnName}'");

            if (JsonNodeHelpers.ReadInt32(result.StructuredContent, "optionCount").GetValueOrDefault() <= 0)
                continue;

            var directOptionIndex = ResolveDirectExecutableOptionIndex(result.StructuredContent);
            if (!requireDirectExecutableOption || directOptionIndex > 0)
                return (result, candidateCell, directOptionIndex);
        }

        if (requireDirectExecutableOption)
        {
            throw new InvalidOperationException(
                $"Could not open a context menu near colonist '{pawnName}' with a direct executable option target.");
        }

        throw new InvalidOperationException($"Could not open a context menu with executable options near colonist '{pawnName}'.");
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

    private static int ResolveDirectExecutableOptionIndex(JsonNode? structuredContent)
    {
        var options = JsonNodeHelpers.ReadArray(structuredContent, "options");
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (JsonNodeHelpers.ReadBoolean(option, "disabled") == true)
                continue;
            if (JsonNodeHelpers.ReadBoolean(option, "hasAction") != true)
                continue;

            var label = JsonNodeHelpers.ReadString(option, "label");
            if (string.IsNullOrWhiteSpace(label))
                continue;
            if (label.EndsWith("...", StringComparison.Ordinal))
                continue;

            return index + 1;
        }

        return -1;
    }

    private static string BuildScreenshotFileName(DateTimeOffset startedAtUtc)
    {
        return $"rimbridge_live_smoke_{startedAtUtc:yyyyMMdd_HHmmss}";
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        var header = File.ReadAllBytes(path).Take(24).ToArray();
        if (header.Length < 24
            || header[0] != 0x89
            || header[1] != 0x50
            || header[2] != 0x4E
            || header[3] != 0x47)
        {
            throw new InvalidOperationException($"File '{path}' is not a valid PNG image.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        return (width, height);
    }
}
