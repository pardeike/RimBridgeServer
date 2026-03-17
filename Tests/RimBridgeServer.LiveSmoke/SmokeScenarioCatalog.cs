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
    public const string DebugActionPawnTargetScenarioName = "debug-action-pawn-target";
    public const string ArchitectFloorDropdownScenarioName = "architect-floor-dropdown";
    public const string ArchitectStatefulTargetingScenarioName = "architect-stateful-targeting";
    public const string ArchitectWallPlacementScenarioName = "architect-wall-placement";
    public const string ArchitectZoneAreaDragScenarioName = "architect-zone-area-drag";
    public const string ScreenTargetClickRoundTripScenarioName = "screen-target-click-roundtrip";
    public const string ScreenTargetClipScenarioName = "screen-target-clip";
    public const string ScriptColonistPrisonScenarioName = "script-colonist-prison";
    public const string ScriptWallSequenceScenarioName = "script-wall-sequence";
    public const string SelectionRoundTripScenarioName = "selection-roundtrip";
    public const string SemanticDiagnosticsRoundTripScenarioName = "semantic-diagnostics-roundtrip";
    public const string ModSettingsDiscoveryScenarioName = "mod-settings-discovery";
    public const string ModSettingsRoundTripScenarioName = "mod-settings-roundtrip";
    public const string ModConfigurationRoundTripScenarioName = "mod-configuration-roundtrip";
    public const string MainTabNavigationScenarioName = "main-tab-navigation";
    public const string UiLayoutRoundTripScenarioName = "ui-layout-roundtrip";
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
            [DebugActionPawnTargetScenarioName] = new SmokeScenarioDefinition
            {
                Name = DebugActionPawnTargetScenarioName,
                Description = "Ensure a playable game exists, then execute pawn-target debug actions such as Toggle Job Logging and Log Job Details by stable path.",
                RunAsync = RunDebugActionPawnTargetAsync
            },
            [ArchitectFloorDropdownScenarioName] = new SmokeScenarioDefinition
            {
                Name = ArchitectFloorDropdownScenarioName,
                Description = "Ensure a playable game exists, then verify a dropdown child floor designator can be selected and applied over a rectangle.",
                RunAsync = RunArchitectFloorDropdownAsync
            },
            [ArchitectStatefulTargetingScenarioName] = new SmokeScenarioDefinition
            {
                Name = ArchitectStatefulTargetingScenarioName,
                Description = "Ensure a playable game exists, then verify explicit allowed-area selection, existing-zone targeting, and cleanup helpers.",
                RunAsync = RunArchitectStatefulTargetingAsync
            },
            [ArchitectWallPlacementScenarioName] = new SmokeScenarioDefinition
            {
                Name = ArchitectWallPlacementScenarioName,
                Description = "Ensure a playable game exists, then verify wall placement becomes blueprint-based versus direct-build depending on god mode.",
                RunAsync = RunArchitectWallPlacementAsync
            },
            [ArchitectZoneAreaDragScenarioName] = new SmokeScenarioDefinition
            {
                Name = ArchitectZoneAreaDragScenarioName,
                Description = "Ensure a playable game exists, then verify stockpile zones and home areas can be created through rectangle drag semantics.",
                RunAsync = RunArchitectZoneAreaDragAsync
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
            [ScriptColonistPrisonScenarioName] = new SmokeScenarioDefinition
            {
                Name = ScriptColonistPrisonScenarioName,
                Description = "Ensure a playable game exists, then use a file-backed Lua fixture to draft colonists, group them, wall them in, and release them inside the prison as one scripted acceptance flow.",
                RunAsync = RunScriptColonistPrisonAsync
            },
            [ScriptWallSequenceScenarioName] = new SmokeScenarioDefinition
            {
                Name = ScriptWallSequenceScenarioName,
                Description = "Ensure a playable game exists, then execute a JSON script that toggles god mode, places two walls, and captures a screenshot as one batch.",
                RunAsync = RunScriptWallSequenceAsync
            },
            [SelectionRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = SelectionRoundTripScenarioName,
                Description = "Ensure a playable game exists, then exercise selection and camera tools around a real colonist.",
                RunAsync = RunSelectionRoundTripAsync
            },
            [SemanticDiagnosticsRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = SemanticDiagnosticsRoundTripScenarioName,
                Description = "Ensure a playable game exists, then smoke the newer discovery, stable-id, selection semantics, notification, alert, wait, and observability surfaces in one roundtrip.",
                RunAsync = RunSemanticDiagnosticsRoundTripAsync
            },
            [ModSettingsDiscoveryScenarioName] = new SmokeScenarioDefinition
            {
                Name = ModSettingsDiscoveryScenarioName,
                Description = "Inspect loaded mod-settings surfaces and verify RimBridgeServer exposes a deterministic semantic ModSettings payload.",
                RunAsync = RunModSettingsDiscoveryAsync
            },
            [ModSettingsRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = ModSettingsRoundTripScenarioName,
                Description = "Round-trip transient and persisted mod-settings updates, reload from disk, and verify semantic Dialog_ModSettings UI state.",
                RunAsync = RunModSettingsRoundTripAsync
            },
            [ModConfigurationRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = ModConfigurationRoundTripScenarioName,
                Description = "Inspect installed mod inventory, disable and re-enable one active non-core mod, restore its original load-order slot, and verify restart-needed semantics against the loaded session.",
                RunAsync = RunModConfigurationRoundTripAsync
            },
            [MainTabNavigationScenarioName] = new SmokeScenarioDefinition
            {
                Name = MainTabNavigationScenarioName,
                Description = "Ensure a playable game exists, then open the Work main tab, verify it through UI state and screen targets, capture a clipped screenshot, and close it again.",
                RunAsync = RunMainTabNavigationAsync
            },
            [UiLayoutRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = UiLayoutRoundTripScenarioName,
                Description = "Ensure a playable game exists, capture a generic layout snapshot for the Work tab, clip to a real control target, click it, verify the state change, and restore it.",
                RunAsync = RunUiLayoutRoundTripAsync
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

    private static async Task RunModSettingsDiscoveryAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.WaitForLongEventIdleAsync("mod_settings.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("mod_settings.snapshot_bridge_status", cancellationToken);

        var listSurfaces = await context.CallGameToolAsync("mod_settings.list_surfaces", "rimworld/list_mod_settings_surfaces", new
        {
            includeWithoutSettings = false
        }, cancellationToken);
        context.EnsureSucceeded(listSurfaces, "Listing loaded mod settings surfaces");

        var surfaces = JsonNodeHelpers.ReadArray(listSurfaces.StructuredContent, "surfaces");
        if (surfaces.Count == 0)
            throw new InvalidOperationException("Mod settings discovery returned no readable settings surfaces.");

        var rimBridgeSurface = surfaces.FirstOrDefault(surface =>
            string.Equals(JsonNodeHelpers.ReadString(surface, "packageId"), "brrainz.rimbridgeserver", StringComparison.OrdinalIgnoreCase));
        if (rimBridgeSurface == null)
            throw new InvalidOperationException("Mod settings discovery did not include the RimBridgeServer settings surface.");

        var modId = JsonNodeHelpers.ReadString(rimBridgeSurface, "modId");
        if (string.IsNullOrWhiteSpace(modId))
            throw new InvalidOperationException("The RimBridgeServer mod settings surface did not return a stable modId.");

        var getSettings = await context.CallGameToolAsync("mod_settings.get_settings", "rimworld/get_mod_settings", new
        {
            modId
        }, cancellationToken);
        context.EnsureSucceeded(getSettings, $"Reading semantic settings for '{modId}'");

        var root = JsonNodeHelpers.GetPath(getSettings.StructuredContent, "root");
        if (root == null)
            throw new InvalidOperationException("Mod settings readback did not include a root semantic node.");

        var rootChildren = JsonNodeHelpers.ReadArray(root, "children");
        if (!rootChildren.Any(child => string.Equals(JsonNodeHelpers.ReadString(child, "name"), "SemanticHarnessSmokeToggle", StringComparison.Ordinal)))
            throw new InvalidOperationException("The semantic mod settings payload did not include the expected SemanticHarnessSmokeToggle field.");
        if (!rootChildren.Any(child => string.Equals(JsonNodeHelpers.ReadString(child, "name"), "SemanticHarnessSmokeValue", StringComparison.Ordinal)))
            throw new InvalidOperationException("The semantic mod settings payload did not include the expected SemanticHarnessSmokeValue field.");

        context.SetSummaryValue("modSettingsSurfaceCount", surfaces.Count.ToString());
        context.SetSummaryValue("rimBridgeModSettingsId", modId);
        context.SetScenarioData("modSettingsSurface", rimBridgeSurface);
        context.SetScenarioData("modSettingsRoot", root);

        var observation = await observationWindow.CaptureAsync(
            "mod_settings.final_bridge_status",
            "mod_settings.collect_operation_events",
            "mod_settings.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunModSettingsRoundTripAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.WaitForLongEventIdleAsync("mod_settings_roundtrip.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("mod_settings_roundtrip.snapshot_bridge_status", cancellationToken);

        var listSurfaces = await context.CallGameToolAsync("mod_settings_roundtrip.list_surfaces", "rimworld/list_mod_settings_surfaces", new
        {
            includeWithoutSettings = false
        }, cancellationToken);
        context.EnsureSucceeded(listSurfaces, "Listing loaded mod settings surfaces for the roundtrip");

        var surfaces = JsonNodeHelpers.ReadArray(listSurfaces.StructuredContent, "surfaces");
        var rimBridgeSurface = surfaces.FirstOrDefault(surface =>
            string.Equals(JsonNodeHelpers.ReadString(surface, "packageId"), "brrainz.rimbridgeserver", StringComparison.OrdinalIgnoreCase));
        if (rimBridgeSurface == null)
            throw new InvalidOperationException("The mod settings roundtrip could not find the RimBridgeServer settings surface.");

        var modId = JsonNodeHelpers.ReadString(rimBridgeSurface, "modId");
        if (string.IsNullOrWhiteSpace(modId))
            throw new InvalidOperationException("The mod settings roundtrip did not receive a stable RimBridgeServer modId.");

        var originalSettings = await context.CallGameToolAsync("mod_settings_roundtrip.get_original", "rimworld/get_mod_settings", new
        {
            modId
        }, cancellationToken);
        context.EnsureSucceeded(originalSettings, $"Reading original settings for '{modId}'");

        var originalToggle = ReadRequiredSemanticBooleanField(originalSettings.StructuredContent, "SemanticHarnessSmokeToggle");
        var originalValue = ReadRequiredSemanticIntField(originalSettings.StructuredContent, "SemanticHarnessSmokeValue");
        var transientToggle = !originalToggle;
        var transientValue = originalValue == 0 ? 1 : 0;
        var persistedToggle = !originalToggle;
        var persistedValue = originalValue == int.MaxValue ? originalValue - 1 : originalValue + 1;
        var restoreWritten = false;

        try
        {
            var transientUpdate = await context.CallGameToolAsync("mod_settings_roundtrip.update_transient", "rimworld/update_mod_settings", new
            {
                modId,
                values = new Dictionary<string, object>
                {
                    ["SemanticHarnessSmokeToggle"] = transientToggle,
                    ["SemanticHarnessSmokeValue"] = transientValue
                },
                write = false
            }, cancellationToken);
            context.EnsureSucceeded(transientUpdate, $"Applying transient in-memory mod settings for '{modId}'");

            var transientSettings = await context.CallGameToolAsync("mod_settings_roundtrip.get_transient", "rimworld/get_mod_settings", new
            {
                modId
            }, cancellationToken);
            context.EnsureSucceeded(transientSettings, $"Reading transient in-memory settings for '{modId}'");

            if (ReadRequiredSemanticBooleanField(transientSettings.StructuredContent, "SemanticHarnessSmokeToggle") != transientToggle)
                throw new InvalidOperationException("The transient mod settings update did not change the expected boolean value.");
            if (ReadRequiredSemanticIntField(transientSettings.StructuredContent, "SemanticHarnessSmokeValue") != transientValue)
                throw new InvalidOperationException("The transient mod settings update did not change the expected numeric value.");

            var reloadSettings = await context.CallGameToolAsync("mod_settings_roundtrip.reload", "rimworld/reload_mod_settings", new
            {
                modId
            }, cancellationToken);
            context.EnsureSucceeded(reloadSettings, $"Reloading mod settings for '{modId}' from disk");

            var reloadedSettings = await context.CallGameToolAsync("mod_settings_roundtrip.get_reloaded", "rimworld/get_mod_settings", new
            {
                modId
            }, cancellationToken);
            context.EnsureSucceeded(reloadedSettings, $"Reading reloaded settings for '{modId}'");

            if (ReadRequiredSemanticBooleanField(reloadedSettings.StructuredContent, "SemanticHarnessSmokeToggle") != originalToggle)
                throw new InvalidOperationException("Reloading mod settings from disk did not restore the original boolean value.");
            if (ReadRequiredSemanticIntField(reloadedSettings.StructuredContent, "SemanticHarnessSmokeValue") != originalValue)
                throw new InvalidOperationException("Reloading mod settings from disk did not restore the original numeric value.");

            var persistedUpdate = await context.CallGameToolAsync("mod_settings_roundtrip.update_persisted", "rimworld/update_mod_settings", new
            {
                modId,
                values = new Dictionary<string, object>
                {
                    ["SemanticHarnessSmokeToggle"] = persistedToggle,
                    ["SemanticHarnessSmokeValue"] = persistedValue
                },
                write = true
            }, cancellationToken);
            context.EnsureSucceeded(persistedUpdate, $"Persisting mod settings for '{modId}'");

            var persistedSettings = await context.CallGameToolAsync("mod_settings_roundtrip.get_persisted", "rimworld/get_mod_settings", new
            {
                modId
            }, cancellationToken);
            context.EnsureSucceeded(persistedSettings, $"Reading persisted settings for '{modId}'");

            if (ReadRequiredSemanticBooleanField(persistedSettings.StructuredContent, "SemanticHarnessSmokeToggle") != persistedToggle)
                throw new InvalidOperationException("Persisting mod settings did not update the boolean field.");
            if (ReadRequiredSemanticIntField(persistedSettings.StructuredContent, "SemanticHarnessSmokeValue") != persistedValue)
                throw new InvalidOperationException("Persisting mod settings did not update the numeric field.");

            var openSettingsDialog = await context.CallGameToolAsync("mod_settings_roundtrip.open_dialog", "rimworld/open_mod_settings", new
            {
                modId,
                replaceExisting = true
            }, cancellationToken);
            context.EnsureSucceeded(openSettingsDialog, $"Opening the native mod settings dialog for '{modId}'");

            var uiState = await context.CallGameToolAsync("mod_settings_roundtrip.get_ui_state", "rimworld/get_ui_state", new { }, cancellationToken);
            context.EnsureSucceeded(uiState, "Reading UI state with the mod settings dialog open");

            var windows = JsonNodeHelpers.ReadArray(uiState.StructuredContent, "windows");
            var modSettingsDialog = windows.FirstOrDefault(window =>
                string.Equals(JsonNodeHelpers.ReadString(window, "semanticKind"), "mod_settings_dialog", StringComparison.Ordinal));
            if (modSettingsDialog == null)
                throw new InvalidOperationException("UI state did not report an open mod settings dialog through semantic window metadata.");

            if (!string.Equals(JsonNodeHelpers.ReadString(modSettingsDialog, "semanticDetails", "mod", "modId"), modId, StringComparison.Ordinal))
                throw new InvalidOperationException("UI state did not associate the open mod settings dialog with the requested modId.");

            var closeSettingsDialog = await context.CallGameToolAsync("mod_settings_roundtrip.close_dialog", "rimworld/close_window", new
            {
                windowType = "Dialog_ModSettings"
            }, cancellationToken);
            context.EnsureSucceeded(closeSettingsDialog, "Closing the native mod settings dialog");

            var restoreSettings = await context.CallGameToolAsync("mod_settings_roundtrip.restore", "rimworld/update_mod_settings", new
            {
                modId,
                values = new Dictionary<string, object>
                {
                    ["SemanticHarnessSmokeToggle"] = originalToggle,
                    ["SemanticHarnessSmokeValue"] = originalValue
                },
                write = true
            }, cancellationToken);
            context.EnsureSucceeded(restoreSettings, $"Restoring persisted settings for '{modId}'");
            restoreWritten = true;

            var restoredSettings = await context.CallGameToolAsync("mod_settings_roundtrip.get_restored", "rimworld/get_mod_settings", new
            {
                modId
            }, cancellationToken);
            context.EnsureSucceeded(restoredSettings, $"Reading restored settings for '{modId}'");

            if (ReadRequiredSemanticBooleanField(restoredSettings.StructuredContent, "SemanticHarnessSmokeToggle") != originalToggle)
                throw new InvalidOperationException("Restoring persisted mod settings did not recover the original boolean value.");
            if (ReadRequiredSemanticIntField(restoredSettings.StructuredContent, "SemanticHarnessSmokeValue") != originalValue)
                throw new InvalidOperationException("Restoring persisted mod settings did not recover the original numeric value.");

            context.SetSummaryValue("rimBridgeModSettingsId", modId);
            context.SetSummaryValue("originalToggle", originalToggle.ToString());
            context.SetSummaryValue("originalValue", originalValue.ToString());
            context.SetSummaryValue("persistedToggle", persistedToggle.ToString());
            context.SetSummaryValue("persistedValue", persistedValue.ToString());
            context.SetScenarioData("modSettingsSurface", rimBridgeSurface);
            context.SetScenarioData("openDialog", openSettingsDialog.StructuredContent);
        }
        finally
        {
            if (!restoreWritten)
            {
                try
                {
                    var cleanupRestore = await context.CallGameToolAsync("mod_settings_roundtrip.cleanup_restore", "rimworld/update_mod_settings", new
                    {
                        modId,
                        values = new Dictionary<string, object>
                        {
                            ["SemanticHarnessSmokeToggle"] = originalToggle,
                            ["SemanticHarnessSmokeValue"] = originalValue
                        },
                        write = true
                    }, cancellationToken);
                    if (!cleanupRestore.Success)
                        context.Note($"Cleanup warning: restoring mod settings for '{modId}' did not succeed.");
                }
                catch (Exception ex)
                {
                    context.Note($"Cleanup warning: restoring mod settings for '{modId}' failed: {ex.Message}");
                }
            }
        }

        var observation = await observationWindow.CaptureAsync(
            "mod_settings_roundtrip.final_bridge_status",
            "mod_settings_roundtrip.collect_operation_events",
            "mod_settings_roundtrip.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunModConfigurationRoundTripAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.WaitForLongEventIdleAsync("mod_configuration.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("mod_configuration.snapshot_bridge_status", cancellationToken);
        string targetModId = string.Empty;
        string targetPackageId = string.Empty;
        string baselineConfigurationHash = string.Empty;
        string baselineLoadedSessionHash = string.Empty;
        var baselineRestartRequired = false;
        int? baselineIndex = null;
        var restoreReached = false;

        try
        {
            var listMods = await context.CallGameToolAsync("mod_configuration.list_mods", "rimworld/list_mods", new
            {
                includeInactive = true
            }, cancellationToken);
            context.EnsureSucceeded(listMods, "Listing installed mods for configuration discovery");

            var allMods = JsonNodeHelpers.ReadArray(listMods.StructuredContent, "mods");
            if (allMods.Count == 0)
                throw new InvalidOperationException("Mod configuration discovery returned no installed mods.");

            var initialStatus = await context.CallGameToolAsync("mod_configuration.get_status_initial", "rimworld/get_mod_configuration_status", new { }, cancellationToken);
            context.EnsureSucceeded(initialStatus, "Reading the initial mod configuration status");

            baselineConfigurationHash = ReadRequiredString(initialStatus.StructuredContent, "currentConfigurationHash");
            baselineLoadedSessionHash = ReadRequiredString(initialStatus.StructuredContent, "loadedSessionHash");
            baselineRestartRequired = JsonNodeHelpers.ReadBoolean(initialStatus.StructuredContent, "restartRequired") == true;

            var initialActiveMods = JsonNodeHelpers.ReadArray(initialStatus.StructuredContent, "activeMods");
            var targetMod = ResolvePreferredActiveNonCoreMod(initialActiveMods, "brrainz.rimbridgeserver");
            targetModId = ReadRequiredString(targetMod, "modId");
            targetPackageId = ReadRequiredString(targetMod, "packageId");
            baselineIndex = JsonNodeHelpers.ReadInt32(targetMod, "activeLoadOrder");
            if (!baselineIndex.HasValue)
                throw new InvalidOperationException("The chosen mod did not report an active load-order index.");

            var disableMod = await context.CallGameToolAsync("mod_configuration.disable", "rimworld/set_mod_enabled", new
            {
                modId = targetModId,
                enabled = false,
                save = true
            }, cancellationToken);
            context.EnsureSucceeded(disableMod, $"Disabling active mod '{targetPackageId}'");

            if (JsonNodeHelpers.ReadBoolean(disableMod.StructuredContent, "currentEnabled") != false)
                throw new InvalidOperationException("Disabling the target mod did not report currentEnabled=false.");
            if (JsonNodeHelpers.ReadBoolean(disableMod.StructuredContent, "mod", "enabled") != false)
                throw new InvalidOperationException("The disabled mod payload still reported enabled=true.");
            if (JsonNodeHelpers.ReadBoolean(disableMod.StructuredContent, "mod", "loadedInSession") != true)
                throw new InvalidOperationException("Disabling a loaded mod should still report loadedInSession=true until RimWorld is restarted.");

            var disabledStatus = JsonNodeHelpers.GetPath(disableMod.StructuredContent, "configurationStatus");
            if (disabledStatus == null)
                throw new InvalidOperationException("Disabling a mod did not return an embedded configurationStatus payload.");

            var disabledConfigurationHash = ReadRequiredString(disabledStatus, "currentConfigurationHash");
            if (string.Equals(disabledConfigurationHash, baselineConfigurationHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Disabling the target mod did not change the configuration hash.");

            var reenableMod = await context.CallGameToolAsync("mod_configuration.reenable", "rimworld/set_mod_enabled", new
            {
                modId = targetModId,
                enabled = true,
                save = true
            }, cancellationToken);
            context.EnsureSucceeded(reenableMod, $"Re-enabling active mod '{targetPackageId}'");

            if (JsonNodeHelpers.ReadBoolean(reenableMod.StructuredContent, "currentEnabled") != true)
                throw new InvalidOperationException("Re-enabling the target mod did not report currentEnabled=true.");

            var reenabledIndex = JsonNodeHelpers.ReadInt32(reenableMod.StructuredContent, "mod", "activeLoadOrder");
            if (!reenabledIndex.HasValue)
                throw new InvalidOperationException("Re-enabling the target mod did not report an active load-order index.");

            JsonNode? finalStatusNode;
            ToolInvocationResult? reorderMod = null;
            if (reenabledIndex.Value != baselineIndex.Value)
            {
                reorderMod = await context.CallGameToolAsync("mod_configuration.reorder", "rimworld/reorder_mod", new
                {
                    modId = targetModId,
                    targetIndex = baselineIndex.Value,
                    save = true
                }, cancellationToken);
                context.EnsureSucceeded(reorderMod, $"Restoring mod '{targetPackageId}' to load-order index {baselineIndex.Value}");
                finalStatusNode = JsonNodeHelpers.GetPath(reorderMod.StructuredContent, "configurationStatus");
                if (finalStatusNode == null)
                    throw new InvalidOperationException("Reordering a mod did not return an embedded configurationStatus payload.");
            }
            else
            {
                finalStatusNode = JsonNodeHelpers.GetPath(reenableMod.StructuredContent, "configurationStatus");
                if (finalStatusNode == null)
                    throw new InvalidOperationException("Re-enabling a mod did not return an embedded configurationStatus payload.");
            }

            var finalStatus = await context.CallGameToolAsync("mod_configuration.get_status_final", "rimworld/get_mod_configuration_status", new { }, cancellationToken);
            context.EnsureSucceeded(finalStatus, "Reading final mod configuration status after restoration");

            var finalConfigurationHash = ReadRequiredString(finalStatus.StructuredContent, "currentConfigurationHash");
            if (!string.Equals(finalConfigurationHash, baselineConfigurationHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Restoring the mod configuration did not recover the original configuration hash.");

            var finalLoadedSessionHash = ReadRequiredString(finalStatus.StructuredContent, "loadedSessionHash");
            if (!string.Equals(finalLoadedSessionHash, baselineLoadedSessionHash, StringComparison.Ordinal))
                throw new InvalidOperationException("The loaded-session hash changed while exercising the mod configuration surface.");

            var finalRestartRequired = JsonNodeHelpers.ReadBoolean(finalStatus.StructuredContent, "restartRequired") == true;
            if (finalRestartRequired != baselineRestartRequired)
                throw new InvalidOperationException("Restoring the mod configuration did not return restartRequired to its original state.");

            var finalActiveMods = JsonNodeHelpers.ReadArray(finalStatus.StructuredContent, "activeMods");
            var finalTargetMod = ResolveModById(finalActiveMods, targetModId);
            if (JsonNodeHelpers.ReadInt32(finalTargetMod, "activeLoadOrder") != baselineIndex)
                throw new InvalidOperationException("The restored mod did not return to its original active load-order index.");

            restoreReached = true;

            context.SetSummaryValue("modId", targetModId);
            context.SetSummaryValue("packageId", targetPackageId);
            context.SetSummaryValue("baselineIndex", baselineIndex.Value.ToString());
            context.SetSummaryValue("baselineRestartRequired", baselineRestartRequired.ToString());
            context.SetScenarioData("listMods", listMods.StructuredContent);
            context.SetScenarioData("initialStatus", initialStatus.StructuredContent);
            context.SetScenarioData("disableMod", disableMod.StructuredContent);
            context.SetScenarioData("reenableMod", reenableMod.StructuredContent);
            context.SetScenarioData("reorderMod", reorderMod?.StructuredContent);
            context.SetScenarioData("finalStatus", finalStatus.StructuredContent);
        }
        finally
        {
            if (!restoreReached && !string.IsNullOrWhiteSpace(targetModId) && baselineIndex.HasValue)
            {
                try
                {
                    var cleanupEnable = await context.CallGameToolAsync("mod_configuration.cleanup_enable", "rimworld/set_mod_enabled", new
                    {
                        modId = targetModId,
                        enabled = true,
                        save = true
                    }, cancellationToken);
                    if (!cleanupEnable.Success)
                    {
                        context.Note($"Cleanup warning: enabling mod '{targetModId}' did not succeed.");
                    }
                    else
                    {
                        var cleanupReorder = await context.CallGameToolAsync("mod_configuration.cleanup_reorder", "rimworld/reorder_mod", new
                        {
                            modId = targetModId,
                            targetIndex = baselineIndex.Value,
                            save = true
                        }, cancellationToken);
                        if (!cleanupReorder.Success)
                            context.Note($"Cleanup warning: reordering mod '{targetModId}' back to index {baselineIndex.Value} did not succeed.");
                    }
                }
                catch (Exception ex)
                {
                    context.Note($"Cleanup warning: restoring mod configuration for '{targetModId}' failed: {ex.Message}");
                }
            }
        }

        var observation = await observationWindow.CaptureAsync(
            "mod_configuration.final_bridge_status",
            "mod_configuration.collect_operation_events",
            "mod_configuration.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunMainTabNavigationAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("main_tab.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("main_tab.snapshot_bridge_status", cancellationToken);

        var listMainTabs = await context.CallGameToolAsync("main_tab.list_main_tabs", "rimworld/list_main_tabs", new
        {
            includeHidden = false
        }, cancellationToken);
        context.EnsureSucceeded(listMainTabs, "Listing visible RimWorld main tabs");

        var tabs = JsonNodeHelpers.ReadArray(listMainTabs.StructuredContent, "tabs");
        if (tabs.Count == 0)
            throw new InvalidOperationException("Main-tab discovery returned no visible tabs.");

        var workTab = ResolveMainTabNode(tabs, "Work", "RimWorld.MainTabWindow_Work");
        var mainTabId = ReadRequiredString(workTab, "targetId");

        var openMainTab = await context.CallGameToolAsync("main_tab.open", "rimworld/open_main_tab", new
        {
            mainTabId
        }, cancellationToken);
        context.EnsureSucceeded(openMainTab, $"Opening main tab '{mainTabId}'");

        var uiState = await context.CallGameToolAsync("main_tab.get_ui_state", "rimworld/get_ui_state", new { }, cancellationToken);
        context.EnsureSucceeded(uiState, "Reading UI state after opening the Work main tab");

        if (JsonNodeHelpers.ReadBoolean(uiState.StructuredContent, "mainTabOpen") != true)
            throw new InvalidOperationException("UI state did not report an open main tab after opening Work.");
        if (!string.Equals(JsonNodeHelpers.ReadString(uiState.StructuredContent, "openMainTabId"), mainTabId, StringComparison.Ordinal))
            throw new InvalidOperationException("UI state did not report the expected main-tab target id after opening Work.");
        if (!string.Equals(JsonNodeHelpers.ReadString(uiState.StructuredContent, "openMainTabType"), "RimWorld.MainTabWindow_Work", StringComparison.Ordinal))
            throw new InvalidOperationException("UI state did not report the Work main-tab window type.");

        var screenTargets = await context.CallGameToolAsync("main_tab.get_screen_targets", "rimworld/get_screen_targets", new { }, cancellationToken);
        context.EnsureSucceeded(screenTargets, "Reading screen targets after opening the Work main tab");

        var mainTabTarget = JsonNodeHelpers.GetPath(screenTargets.StructuredContent, "targets", "mainTab");
        if (mainTabTarget == null)
            throw new InvalidOperationException("Screen targets did not expose the open main tab.");
        if (!string.Equals(JsonNodeHelpers.ReadString(mainTabTarget, "targetId"), mainTabId, StringComparison.Ordinal))
            throw new InvalidOperationException("Screen targets did not return the expected Work main-tab target id.");

        var screenshotFileName = BuildScreenshotFileName(context.Report.StartedAtUtc) + "_work_tab";
        var screenshot = await context.CallGameToolAsync("main_tab.take_screenshot", "rimworld/take_screenshot", new
        {
            fileName = screenshotFileName,
            includeTargets = true,
            clipTargetId = mainTabId
        }, cancellationToken);
        context.EnsureSucceeded(screenshot, $"Capturing clipped screenshot for main tab '{mainTabId}'");

        if (JsonNodeHelpers.ReadBoolean(screenshot.StructuredContent, "clipped") != true)
            throw new InvalidOperationException("The main-tab screenshot was not reported as clipped.");
        if (!string.Equals(JsonNodeHelpers.ReadString(screenshot.StructuredContent, "clipTargetId"), mainTabId, StringComparison.Ordinal))
            throw new InvalidOperationException("The main-tab screenshot did not preserve the expected clip target id.");
        if (JsonNodeHelpers.ReadInt64(screenshot.StructuredContent, "sizeBytes").GetValueOrDefault() <= 0)
            throw new InvalidOperationException("The main-tab screenshot did not report a valid file artifact.");

        if (context.HumanVerificationEnabled)
        {
            var screenshotPath = ReadRequiredString(screenshot.StructuredContent, "path");
            await context.ExportHumanVerificationArtifactAsync(
                "work_tab_screenshot",
                screenshotPath,
                "Clipped screenshot of the open RimWorld Work main tab.",
                [
                    "The screenshot should show the Work/Priorities tab clipped to the tab window area.",
                    "This verifies that built-in main tabs can be opened and clipped just like modal windows."
                ],
                cancellationToken);
        }

        var closeMainTab = await context.CallGameToolAsync("main_tab.close", "rimworld/close_main_tab", new
        {
            mainTabId
        }, cancellationToken);
        context.EnsureSucceeded(closeMainTab, $"Closing main tab '{mainTabId}'");

        var closedUiState = await context.CallGameToolAsync("main_tab.get_ui_state_closed", "rimworld/get_ui_state", new { }, cancellationToken);
        context.EnsureSucceeded(closedUiState, "Reading UI state after closing the Work main tab");

        if (JsonNodeHelpers.ReadBoolean(closedUiState.StructuredContent, "mainTabOpen") == true)
            throw new InvalidOperationException("UI state still reported an open main tab after closing Work.");

        context.SetSummaryValue("mainTabId", mainTabId);
        context.SetSummaryValue("mainTabType", ReadRequiredString(mainTabTarget, "type"));
        context.SetScenarioData("mainTabs", new JsonArray(tabs.Select(JsonNodeHelpers.CloneNode).ToArray()));
        context.SetScenarioData("openMainTab", openMainTab.StructuredContent);
        context.SetScenarioData("uiState", uiState.StructuredContent);
        context.SetScenarioData("screenTargets", JsonNodeHelpers.GetPath(screenTargets.StructuredContent, "targets"));
        context.SetScenarioData("screenshot", screenshot.StructuredContent);
        context.SetScenarioData("closeMainTab", closeMainTab.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "main_tab.final_bridge_status",
            "main_tab.collect_operation_events",
            "main_tab.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunUiLayoutRoundTripAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("ui_layout.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("ui_layout.snapshot_bridge_status", cancellationToken);

        var openMainTab = await context.CallGameToolAsync("ui_layout.open_work_tab", "rimworld/open_main_tab", new
        {
            mainTabId = "main-tab:Work"
        }, cancellationToken);
        context.EnsureSucceeded(openMainTab, "Opening the Work main tab before capturing UI layout");

        var initialLayout = await context.CallGameToolAsync("ui_layout.capture_initial", "rimworld/get_ui_layout", new
        {
            surfaceId = "main-tab:Work",
            timeoutMs = 3000
        }, cancellationToken);
        context.EnsureSucceeded(initialLayout, "Capturing the initial Work main-tab layout");

        var surface = ResolveUiLayoutSurface(initialLayout.StructuredContent, "main-tab:Work");
        var checkbox = ResolveUiLayoutElement(surface, "checkbox");
        var initialTargetId = ReadRequiredString(checkbox, "targetId");
        var initialChecked = JsonNodeHelpers.ReadBoolean(checkbox, "isChecked");
        if (!initialChecked.HasValue)
            throw new InvalidOperationException("The captured Work-tab checkbox did not expose a boolean checked state.");

        var clippedScreenshot = await context.CallGameToolAsync("ui_layout.clip_checkbox", "rimworld/take_screenshot", new
        {
            fileName = BuildScreenshotFileName(context.Report.StartedAtUtc) + "_ui_checkbox",
            includeTargets = true,
            clipTargetId = initialTargetId
        }, cancellationToken);
        context.EnsureSucceeded(clippedScreenshot, $"Capturing a screenshot clipped to UI target '{initialTargetId}'");
        if (JsonNodeHelpers.ReadBoolean(clippedScreenshot.StructuredContent, "clipped") != true)
            throw new InvalidOperationException("The UI control screenshot was not reported as clipped.");

        var clickCheckbox = await context.CallGameToolAsync("ui_layout.click_checkbox", "rimworld/click_ui_target", new
        {
            targetId = initialTargetId,
            timeoutMs = 3000
        }, cancellationToken);
        context.EnsureSucceeded(clickCheckbox, $"Clicking UI target '{initialTargetId}'");

        var toggledLayout = await context.CallGameToolAsync("ui_layout.capture_toggled", "rimworld/get_ui_layout", new
        {
            surfaceId = "main-tab:Work",
            timeoutMs = 3000
        }, cancellationToken);
        context.EnsureSucceeded(toggledLayout, "Capturing the Work main-tab layout after clicking the checkbox");

        var toggledSurface = ResolveUiLayoutSurface(toggledLayout.StructuredContent, "main-tab:Work");
        var toggledCheckbox = ResolveUiLayoutElement(toggledSurface, "checkbox");
        var toggledChecked = JsonNodeHelpers.ReadBoolean(toggledCheckbox, "isChecked");
        if (!toggledChecked.HasValue)
            throw new InvalidOperationException("The toggled Work-tab checkbox did not expose a boolean checked state.");
        if (toggledChecked.Value == initialChecked.Value)
            throw new InvalidOperationException("Clicking the captured Work-tab checkbox did not change its checked state.");

        var restoreTargetId = ReadRequiredString(toggledCheckbox, "targetId");
        var restoreCheckbox = await context.CallGameToolAsync("ui_layout.restore_checkbox", "rimworld/click_ui_target", new
        {
            targetId = restoreTargetId,
            timeoutMs = 3000
        }, cancellationToken);
        context.EnsureSucceeded(restoreCheckbox, $"Restoring UI target '{restoreTargetId}' to its original state");

        var restoredLayout = await context.CallGameToolAsync("ui_layout.capture_restored", "rimworld/get_ui_layout", new
        {
            surfaceId = "main-tab:Work",
            timeoutMs = 3000
        }, cancellationToken);
        context.EnsureSucceeded(restoredLayout, "Capturing the Work main-tab layout after restoring the checkbox");

        var restoredSurface = ResolveUiLayoutSurface(restoredLayout.StructuredContent, "main-tab:Work");
        var restoredCheckbox = ResolveUiLayoutElement(restoredSurface, "checkbox");
        var restoredChecked = JsonNodeHelpers.ReadBoolean(restoredCheckbox, "isChecked");
        if (restoredChecked != initialChecked)
            throw new InvalidOperationException("Restoring the Work-tab checkbox did not return it to the original checked state.");

        var closeMainTab = await context.CallGameToolAsync("ui_layout.close_work_tab", "rimworld/close_main_tab", new
        {
            mainTabId = "main-tab:Work"
        }, cancellationToken);
        context.EnsureSucceeded(closeMainTab, "Closing the Work main tab after the UI layout roundtrip");

        context.SetSummaryValue("uiTargetId", initialTargetId);
        context.SetSummaryValue("checkboxSource", ReadRequiredString(checkbox, "source"));
        context.SetScenarioData("initialLayout", initialLayout.StructuredContent);
        context.SetScenarioData("clippedScreenshot", clippedScreenshot.StructuredContent);
        context.SetScenarioData("clickCheckbox", clickCheckbox.StructuredContent);
        context.SetScenarioData("toggledLayout", toggledLayout.StructuredContent);
        context.SetScenarioData("restoreCheckbox", restoreCheckbox.StructuredContent);
        context.SetScenarioData("restoredLayout", restoredLayout.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "ui_layout.final_bridge_status",
            "ui_layout.collect_operation_events",
            "ui_layout.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunDebugActionPawnTargetAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("debug_actions_pawn.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("debug_actions_pawn.snapshot_bridge_status", cancellationToken);

        var roots = await context.CallGameToolAsync("debug_actions_pawn.list_roots", "rimworld/list_debug_action_roots", new
        {
            includeHidden = false
        }, cancellationToken);
        context.EnsureSucceeded(roots, "Listing RimWorld debug-action roots for the pawn-target scenario");

        var rootArray = JsonNodeHelpers.ReadArray(roots.StructuredContent, "roots");
        var actionsRootPath = ResolveDebugActionRootPath(rootArray, "Actions");

        var actionChildren = await context.CallGameToolAsync("debug_actions_pawn.list_action_children", "rimworld/list_debug_action_children", new
        {
            path = actionsRootPath,
            includeHidden = false
        }, cancellationToken);
        context.EnsureSucceeded(actionChildren, $"Listing debug-action children under '{actionsRootPath}'");

        var actionChildArray = JsonNodeHelpers.ReadArray(actionChildren.StructuredContent, "children");
        var toggleJobLoggingPath = ResolveDebugActionPath(actionChildArray, @"Actions\T: Toggle Job Logging");
        var logJobDetailsPath = ResolveDebugActionPath(actionChildArray, @"Actions\T: Log Job Details");

        var colonists = await context.CallGameToolAsync("debug_actions_pawn.list_colonists", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing current-map colonists for the pawn-target debug-action scenario");

        var pawnName = ResolveFirstColonistName(colonists.StructuredContent);

        var toggleNode = await context.CallGameToolAsync("debug_actions_pawn.get_toggle", "rimworld/get_debug_action", new
        {
            path = toggleJobLoggingPath,
            includeChildren = false
        }, cancellationToken);
        context.EnsureSucceeded(toggleNode, $"Reading debug-action metadata for '{toggleJobLoggingPath}'");

        if (!string.Equals(JsonNodeHelpers.ReadString(toggleNode.StructuredContent, "node", "execution", "kind"), "PawnTarget", StringComparison.Ordinal))
            throw new InvalidOperationException($"Debug action '{toggleJobLoggingPath}' was not reported as a pawn-target action.");
        if (JsonNodeHelpers.ReadBoolean(toggleNode.StructuredContent, "node", "execution", "supported") != true)
            throw new InvalidOperationException($"Debug action '{toggleJobLoggingPath}' was not reported as executable with a pawn target.");
        if (!string.Equals(JsonNodeHelpers.ReadString(toggleNode.StructuredContent, "node", "execution", "requiredTargetKind"), "pawn", StringComparison.Ordinal))
            throw new InvalidOperationException($"Debug action '{toggleJobLoggingPath}' did not report the required target kind as 'pawn'.");

        var originalToggleState = JsonNodeHelpers.ReadBoolean(toggleNode.StructuredContent, "node", "on") == true;
        var currentToggleState = originalToggleState;

        try
        {
            if (currentToggleState)
            {
                var disableToggle = await context.CallGameToolAsync("debug_actions_pawn.disable_toggle", "rimworld/execute_debug_action", new
                {
                    path = toggleJobLoggingPath,
                    pawnName
                }, cancellationToken);
                context.EnsureSucceeded(disableToggle, $"Disabling pawn job logging for '{pawnName}'");
                currentToggleState = false;
            }

            var enableToggle = await context.CallGameToolAsync("debug_actions_pawn.enable_toggle", "rimworld/execute_debug_action", new
            {
                path = toggleJobLoggingPath,
                pawnName
            }, cancellationToken);
            context.EnsureSucceeded(enableToggle, $"Enabling pawn job logging for '{pawnName}'");
            currentToggleState = true;

            var toggleTargetName = JsonNodeHelpers.ReadString(enableToggle.StructuredContent, "targetPawn", "name");
            if (!string.Equals(toggleTargetName, pawnName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Debug-action execution targeted '{toggleTargetName}' instead of '{pawnName}'.");

            var logJobDetails = await context.CallGameToolAsync("debug_actions_pawn.log_job_details", "rimworld/execute_debug_action", new
            {
                path = logJobDetailsPath,
                pawnName
            }, cancellationToken);
            context.EnsureSucceeded(logJobDetails, $"Logging job details for '{pawnName}'");

            var detailLogArray = JsonNodeHelpers.ReadArray(logJobDetails.StructuredContent, "effects", "logs");
            if (detailLogArray.Count == 0)
                throw new InvalidOperationException($"Debug action '{logJobDetailsPath}' did not emit any captured logs for '{pawnName}'.");

            context.SetSummaryValue("actionsRootPath", actionsRootPath);
            context.SetSummaryValue("targetPawn", pawnName);
            context.SetSummaryValue("toggleJobLoggingPath", toggleJobLoggingPath);
            context.SetSummaryValue("logJobDetailsPath", logJobDetailsPath);
            context.SetSummaryValue("jobDetailLogCount", detailLogArray.Count.ToString());
            context.SetScenarioData("roots", new JsonArray(rootArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("actionChildren", new JsonArray(actionChildArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("toggleNode", toggleNode.StructuredContent);
            context.SetScenarioData("enableToggle", enableToggle.StructuredContent);
            context.SetScenarioData("logJobDetails", logJobDetails.StructuredContent);
        }
        finally
        {
            if (currentToggleState != originalToggleState)
            {
                var restoreToggle = await context.CallGameToolAsync("debug_actions_pawn.restore_toggle", "rimworld/execute_debug_action", new
                {
                    path = toggleJobLoggingPath,
                    pawnName
                }, cancellationToken);
                context.EnsureSucceeded(restoreToggle, $"Restoring pawn job logging for '{pawnName}'");
                context.SetScenarioData("restoreToggle", restoreToggle.StructuredContent);
            }
        }

        var observation = await observationWindow.CaptureAsync(
            "debug_actions_pawn.final_bridge_status",
            "debug_actions_pawn.collect_operation_events",
            "debug_actions_pawn.collect_logs",
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

    private static async Task RunSemanticDiagnosticsRoundTripAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("semantic.wait_for_long_event_idle", cancellationToken);

        var baselineUiState = await EnsureNoDialogWindowsAsync(context, "semantic.normalize", cancellationToken);
        var observationWindow = await context.BeginObservationWindowAsync(
            "semantic.snapshot_bridge_status",
            cancellationToken,
            new SmokeObservationWindowOptions
            {
                IncludeDiagnosticEvents = true
            });

        var bridgeStatus = await context.CallGameToolAsync("semantic.bridge_status", "rimbridge/get_bridge_status", new { }, cancellationToken);
        context.EnsureSucceeded(bridgeStatus, "Reading bridge status for the semantic diagnostics roundtrip");
        if (JsonNodeHelpers.ReadBoolean(bridgeStatus.StructuredContent, "state", "automationReady") != true)
            throw new InvalidOperationException("The semantic diagnostics roundtrip requires an automation-ready game.");

        var capabilityList = await context.CallGameToolAsync("semantic.list_capabilities", "rimbridge/list_capabilities", new
        {
            query = "notification",
            includeParameters = true,
            limit = 32
        }, cancellationToken);
        context.EnsureSucceeded(capabilityList, "Listing bridge capabilities for discovery smoke coverage");

        var notificationCapabilities = JsonNodeHelpers.ReadArray(capabilityList.StructuredContent, "capabilities");
        if (notificationCapabilities.Count < 6)
            throw new InvalidOperationException("Capability discovery did not return the expected notification-related capabilities.");
        if (!notificationCapabilities.Any(capability => string.Equals(JsonNodeHelpers.ReadString(capability, "id"), "rimbridge.core/notifications/list-messages", StringComparison.Ordinal)))
            throw new InvalidOperationException("Capability discovery did not include the list-messages notification capability.");
        if (!notificationCapabilities.Any(capability => JsonNodeHelpers.ReadArray(capability, "aliases").Any(alias => string.Equals(JsonNodeHelpers.ReadString(alias), "rimworld/activate_alert", StringComparison.Ordinal))))
            throw new InvalidOperationException("Capability discovery did not include the activate-alert alias.");

        var capabilityDetail = await context.CallGameToolAsync("semantic.get_capability", "rimbridge/get_capability", new
        {
            capabilityIdOrAlias = "rimworld/execute_gizmo"
        }, cancellationToken);
        context.EnsureSucceeded(capabilityDetail, "Reading a single capability descriptor for alias resolution");

        var resolvedCapabilityId = JsonNodeHelpers.ReadString(capabilityDetail.StructuredContent, "capability", "id");
        var capabilityParameterNames = JsonNodeHelpers.ReadArray(capabilityDetail.StructuredContent, "capability", "parameters")
            .Select(parameter => JsonNodeHelpers.ReadString(parameter, "name"))
            .ToList();
        if (string.IsNullOrWhiteSpace(resolvedCapabilityId) || !capabilityParameterNames.Contains("gizmoId", StringComparer.Ordinal))
            throw new InvalidOperationException("Capability detail lookup did not return the expected execute-gizmo descriptor.");

        var colonists = await context.CallGameToolAsync("semantic.list_colonists", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing current-map colonists for stable-id smoke coverage");

        var firstColonist = ResolveFirstColonist(colonists.StructuredContent);
        var pawnId = ReadRequiredString(firstColonist, "pawnId");
        var pawnName = ReadRequiredString(firstColonist, "name");
        var mapId = ReadRequiredString(firstColonist, "mapId");
        var initialDrafted = JsonNodeHelpers.ReadBoolean(firstColonist, "drafted") == true;
        context.Report.ColonistCount = JsonNodeHelpers.ReadInt32(colonists.StructuredContent, "count");

        var selectPawn = await context.CallGameToolAsync("semantic.select_pawn", "rimworld/select_pawn", new
        {
            pawnId,
            append = false
        }, cancellationToken);
        context.EnsureSucceeded(selectPawn, $"Selecting colonist '{pawnName}' by stable pawn id");
        if (!string.Equals(JsonNodeHelpers.ReadString(selectPawn.StructuredContent, "selected", "pawnId"), pawnId, StringComparison.Ordinal))
            throw new InvalidOperationException("Selecting a colonist by stable pawn id did not return the expected pawn.");

        var selectionSemantics = await context.CallGameToolAsync("semantic.get_selection_semantics", "rimworld/get_selection_semantics", new { }, cancellationToken);
        context.EnsureSucceeded(selectionSemantics, "Reading selection semantics for the selected colonist");
        if (JsonNodeHelpers.ReadBoolean(selectionSemantics.StructuredContent, "hasSelection") != true
            || JsonNodeHelpers.ReadInt32(selectionSemantics.StructuredContent, "selectedCount") != 1)
        {
            throw new InvalidOperationException("Selection semantics did not report exactly one selected object.");
        }

        var selectedObjects = JsonNodeHelpers.ReadArray(selectionSemantics.StructuredContent, "selectedObjects");
        if (selectedObjects.Count == 0)
            throw new InvalidOperationException("Selection semantics did not return a selected object payload.");
        if (!string.Equals(JsonNodeHelpers.ReadString(selectedObjects[0], "details", "pawnId"), pawnId, StringComparison.Ordinal)
            || !string.Equals(JsonNodeHelpers.ReadString(selectedObjects[0], "details", "mapId"), mapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Selection semantics did not preserve the selected pawn's stable identity and map id.");
        }

        var gizmos = await context.CallGameToolAsync("semantic.list_selected_gizmos", "rimworld/list_selected_gizmos", new { }, cancellationToken);
        context.EnsureSucceeded(gizmos, "Listing grouped gizmos for the selected colonist");

        var gizmoNodes = JsonNodeHelpers.ReadArray(gizmos.StructuredContent, "gizmos");
        var draftGizmo = ResolveDraftGizmo(gizmoNodes);
        var draftGizmoId = ReadRequiredString(draftGizmo, "id");
        var draftGizmoLabel = ReadRequiredString(draftGizmo, "label");

        var executeGizmo = await context.CallGameToolAsync("semantic.execute_gizmo", "rimworld/execute_gizmo", new
        {
            gizmoId = draftGizmoId
        }, cancellationToken);
        context.EnsureSucceeded(executeGizmo, $"Executing grouped gizmo '{draftGizmoLabel}'");

        var colonistsAfterGizmo = await context.CallGameToolAsync("semantic.list_colonists_after_gizmo", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonistsAfterGizmo, "Listing colonists after executing the grouped draft gizmo");

        var draftedAfterGizmo = ResolveColonistDrafted(colonistsAfterGizmo.StructuredContent, pawnId);
        if (draftedAfterGizmo == initialDrafted)
            throw new InvalidOperationException("Executing the grouped draft gizmo did not change the selected pawn's draft state.");

        var restoreDraft = await context.CallGameToolAsync("semantic.restore_draft_state", "rimworld/set_draft", new
        {
            pawnId,
            drafted = initialDrafted
        }, cancellationToken);
        context.EnsureSucceeded(restoreDraft, $"Restoring colonist '{pawnName}' to its original draft state");
        if (JsonNodeHelpers.ReadBoolean(restoreDraft.StructuredContent, "drafted") != initialDrafted)
            throw new InvalidOperationException("Restoring the selected pawn's draft state did not succeed.");

        var jumpCamera = await context.CallGameToolAsync("semantic.jump_camera_to_pawn", "rimworld/jump_camera_to_pawn", new
        {
            pawnId
        }, cancellationToken);
        context.EnsureSucceeded(jumpCamera, $"Jumping the camera to colonist '{pawnName}' by stable pawn id");

        var cameraState = await context.CallGameToolAsync("semantic.get_camera_state", "rimworld/get_camera_state", new { }, cancellationToken);
        context.EnsureSucceeded(cameraState, "Reading camera state after jumping to the selected colonist");
        if (!string.Equals(JsonNodeHelpers.ReadString(cameraState.StructuredContent, "mapId"), mapId, StringComparison.Ordinal))
            throw new InvalidOperationException("Camera state did not stay on the selected pawn's map after jumping by stable pawn id.");

        var baselineMessageIds = ReadIdSet(
            await context.CallGameToolAsync("semantic.list_messages_before", "rimworld/list_messages", new { limit = 20 }, cancellationToken),
            "messages");
        var baselineLetterIds = ReadIdSet(
            await context.CallGameToolAsync("semantic.list_letters_before", "rimworld/list_letters", new { limit = 40 }, cancellationToken),
            "letters");

        var screenshotFileName = BuildScreenshotFileName(context.Report.StartedAtUtc) + "_semantic_notifications";
        var screenshot = await context.CallGameToolAsync("semantic.take_screenshot", "rimworld/take_screenshot", new
        {
            fileName = screenshotFileName,
            includeTargets = false,
            suppressMessage = false
        }, cancellationToken);
        context.EnsureSucceeded(screenshot, "Capturing a screenshot to generate a deterministic live message");

        var messagesAfterScreenshot = await context.CallGameToolAsync("semantic.list_messages_after", "rimworld/list_messages", new
        {
            limit = 20
        }, cancellationToken);
        context.EnsureSucceeded(messagesAfterScreenshot, "Listing live messages after capturing a screenshot");

        var screenshotMessage = ResolveNewNodeById(messagesAfterScreenshot.StructuredContent, "messages", baselineMessageIds, node =>
            JsonNodeHelpers.ReadString(node, "text").IndexOf(screenshotFileName, StringComparison.OrdinalIgnoreCase) >= 0);
        var screenshotMessageId = ReadRequiredString(screenshotMessage, "id");

        var wandererJoin = await context.CallGameToolAsync("semantic.debug_action_wanderer_join", "rimworld/execute_debug_action", new
        {
            path = @"Actions\Do incident\WandererJoin"
        }, cancellationToken);
        context.EnsureSucceeded(wandererJoin, "Executing the WandererJoin debug action to create a deterministic choice letter");

        var lettersAfterChoice = await context.CallGameToolAsync("semantic.list_letters_after_choice", "rimworld/list_letters", new
        {
            limit = 40
        }, cancellationToken);
        context.EnsureSucceeded(lettersAfterChoice, "Listing letters after the WandererJoin debug action");

        var choiceLetter = ResolveNewNodeById(lettersAfterChoice.StructuredContent, "letters", baselineLetterIds, node =>
            JsonNodeHelpers.ReadInt32(node, "choiceCount").GetValueOrDefault() > 0
            && JsonNodeHelpers.ReadBoolean(node, "canDismissWithRightClick") == false);
        var choiceLetterId = ReadRequiredString(choiceLetter, "id");
        var choiceLetterLabel = ReadRequiredString(choiceLetter, "label");
        if (JsonNodeHelpers.ReadInt32(choiceLetter, "choiceCount").GetValueOrDefault() <= 0
            || string.IsNullOrWhiteSpace(JsonNodeHelpers.ReadString(choiceLetter, "text")))
        {
            throw new InvalidOperationException("The deterministic choice letter did not expose its semantic text and options.");
        }

        var openLetter = await context.CallGameToolAsync("semantic.open_letter", "rimworld/open_letter", new
        {
            letterId = choiceLetterId
        }, cancellationToken);
        context.EnsureSucceeded(openLetter, $"Opening choice letter '{choiceLetterLabel}'");

        if (JsonNodeHelpers.ReadInt32(openLetter.StructuredContent, "effects", "windowCountDelta").GetValueOrDefault() <= 0
            && JsonNodeHelpers.ReadBoolean(openLetter.StructuredContent, "effects", "topWindowChanged") != true)
        {
            throw new InvalidOperationException("Opening the choice letter did not visibly change the RimWorld window stack.");
        }

        var openedLetterWindowType = JsonNodeHelpers.ReadString(openLetter.StructuredContent, "stateAfter", "uiState", "FocusedWindowType");
        if (!string.IsNullOrWhiteSpace(openedLetterWindowType))
        {
            var closeLetterWindow = await context.CallGameToolAsync("semantic.close_choice_letter_window", "rimworld/close_window", new
            {
                windowType = openedLetterWindowType
            }, cancellationToken);
            context.EnsureSucceeded(closeLetterWindow, $"Closing the opened choice letter window '{openedLetterWindowType}'");
        }

        await EnsureNoDialogWindowsAsync(context, "semantic.after_open_letter", cancellationToken);

        var letterIdsAfterChoice = ReadIdSet(lettersAfterChoice, "letters");
        var traderCaravan = await context.CallGameToolAsync("semantic.debug_action_trader_caravan", "rimworld/execute_debug_action", new
        {
            path = @"Actions\Do incident\TraderCaravanArrival"
        }, cancellationToken);
        context.EnsureSucceeded(traderCaravan, "Executing the TraderCaravanArrival debug action to create a dismissible standard letter");

        var lettersAfterStandard = await context.CallGameToolAsync("semantic.list_letters_after_standard", "rimworld/list_letters", new
        {
            limit = 40
        }, cancellationToken);
        context.EnsureSucceeded(lettersAfterStandard, "Listing letters after the trader caravan debug action");

        var dismissibleLetter = ResolveNewNodeById(lettersAfterStandard.StructuredContent, "letters", letterIdsAfterChoice, node =>
            JsonNodeHelpers.ReadBoolean(node, "canDismissWithRightClick") == true);
        var dismissibleLetterId = ReadRequiredString(dismissibleLetter, "id");
        var dismissibleLetterLabel = ReadRequiredString(dismissibleLetter, "label");

        var dismissLetter = await context.CallGameToolAsync("semantic.dismiss_letter", "rimworld/dismiss_letter", new
        {
            letterId = dismissibleLetterId
        }, cancellationToken);
        context.EnsureSucceeded(dismissLetter, $"Dismissing standard letter '{dismissibleLetterLabel}'");
        if (JsonNodeHelpers.ReadBoolean(dismissLetter.StructuredContent, "dismissed") != true)
            throw new InvalidOperationException("Dismissing the standard letter did not remove it from the letter stack.");

        var lettersAfterDismiss = await context.CallGameToolAsync("semantic.list_letters_after_dismiss", "rimworld/list_letters", new
        {
            limit = 40
        }, cancellationToken);
        context.EnsureSucceeded(lettersAfterDismiss, "Listing letters after dismissing the standard letter");
        if (ReadIdSet(lettersAfterDismiss, "letters").Contains(dismissibleLetterId))
            throw new InvalidOperationException("The dismissed standard letter still appeared in the letter stack.");

        await EnsureNoDialogWindowsAsync(context, "semantic.before_alerts", cancellationToken);

        ToolInvocationResult alerts = await context.CallGameToolAsync("semantic.list_alerts_initial", "rimworld/list_alerts", new
        {
            limit = 20
        }, cancellationToken);
        context.EnsureSucceeded(alerts, "Listing active alerts before alert activation");

        for (var attempt = 1; JsonNodeHelpers.ReadInt32(alerts.StructuredContent, "returnedCount").GetValueOrDefault() == 0 && attempt <= 8; attempt++)
        {
            context.Note($"No alerts were active yet. Letting the colony run briefly before retrying alert activation (attempt {attempt}).");

            var unpause = await context.CallGameToolAsync($"semantic.unpause_for_alerts_{attempt}", "rimworld/pause_game", new
            {
                pause = false
            }, cancellationToken);
            context.EnsureSucceeded(unpause, "Unpausing the game to surface startup-delayed alerts");

            await Task.Delay(3000, cancellationToken);

            var repause = await context.CallGameToolAsync($"semantic.repause_after_alerts_{attempt}", "rimworld/pause_game", new
            {
                pause = true
            }, cancellationToken);
            context.EnsureSucceeded(repause, "Re-pausing the game after allowing alerts to populate");

            alerts = await context.CallGameToolAsync($"semantic.list_alerts_retry_{attempt}", "rimworld/list_alerts", new
            {
                limit = 20
            }, cancellationToken);
            context.EnsureSucceeded(alerts, "Listing active alerts after advancing in-game time");
        }

        var alertNodes = JsonNodeHelpers.ReadArray(alerts.StructuredContent, "alerts");
        var alert = alertNodes.FirstOrDefault(node =>
            JsonNodeHelpers.ReadBoolean(node, "activatable") == true
            && JsonNodeHelpers.ReadBoolean(node, "anyCulpritValid") == true)
            ?? alertNodes.FirstOrDefault(node => JsonNodeHelpers.ReadBoolean(node, "activatable") == true);
        if (alert == null)
            throw new InvalidOperationException("No activatable alert was available to smoke the alert activation surface.");

        var alertId = ReadRequiredString(alert, "id");
        var alertLabel = ReadRequiredString(alert, "label");
        var activateAlert = await context.CallGameToolAsync("semantic.activate_alert", "rimworld/activate_alert", new
        {
            alertId
        }, cancellationToken);
        context.EnsureSucceeded(activateAlert, $"Activating alert '{alertLabel}'");

        var threatPoints = await context.CallGameToolAsync("semantic.debug_action_current_threat_points", "rimworld/execute_debug_action", new
        {
            path = @"Outputs\Current Threat Points"
        }, cancellationToken);
        context.EnsureSucceeded(threatPoints, "Executing the Current Threat Points debug action for observability coverage");

        var threatPointsOperationId = context.RequireOperationId(threatPoints, "Executing the Current Threat Points debug action");
        await context.WaitForOperationAsync("semantic.wait_for_threat_points_operation", threatPointsOperationId, cancellationToken);

        var trackedOperation = await context.CallGameToolAsync("semantic.get_operation", "rimbridge/get_operation", new
        {
            operationId = threatPointsOperationId
        }, cancellationToken);
        context.EnsureSucceeded(trackedOperation, $"Reading retained operation '{threatPointsOperationId}'");
        if (!string.Equals(JsonNodeHelpers.ReadString(trackedOperation.StructuredContent, "trackedOperation", "OperationId"), threatPointsOperationId, StringComparison.Ordinal)
            || JsonNodeHelpers.ReadBoolean(trackedOperation.StructuredContent, "trackedOperation", "Success") != true
            || !HasCompletedOperationStatus(trackedOperation.StructuredContent, "trackedOperation", "Status"))
        {
            throw new InvalidOperationException("The retained operation lookup did not return the completed debug-action operation.");
        }

        var recentOperations = await context.CallGameToolAsync("semantic.list_operations", "rimbridge/list_operations", new
        {
            limit = 20,
            includeResults = true
        }, cancellationToken);
        context.EnsureSucceeded(recentOperations, "Listing recent operations with retained result payloads");

        var operationNodes = JsonNodeHelpers.ReadArray(recentOperations.StructuredContent, "operations");
        var listedOperation = operationNodes.FirstOrDefault(operation =>
            string.Equals(JsonNodeHelpers.ReadString(operation, "OperationId"), threatPointsOperationId, StringComparison.Ordinal));
        if (listedOperation == null
            || JsonNodeHelpers.ReadBoolean(listedOperation, "HasResult") != true
            || string.IsNullOrWhiteSpace(JsonNodeHelpers.ReadString(listedOperation, "CapabilityId")))
        {
            throw new InvalidOperationException("The recent operations listing did not retain the expected completed debug-action operation.");
        }

        var operationEvents = await context.CallGameToolAsync("semantic.list_operation_events", "rimbridge/list_operation_events", new
        {
            operationId = threatPointsOperationId,
            limit = 20
        }, cancellationToken);
        context.EnsureSucceeded(operationEvents, "Listing operation events for the debug-action operation");

        var operationEventNodes = JsonNodeHelpers.ReadArray(operationEvents.StructuredContent, "events");
        if (!operationEventNodes.Any(operationEvent =>
            string.Equals(JsonNodeHelpers.ReadString(operationEvent, "OperationId"), threatPointsOperationId, StringComparison.Ordinal)
            && string.Equals(JsonNodeHelpers.ReadString(operationEvent, "EventType"), "operation.completed", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The operation event journal did not include a completion event for the debug-action operation.");
        }

        var logs = await context.CallGameToolAsync("semantic.list_logs", "rimbridge/list_logs", new
        {
            operationId = threatPointsOperationId,
            limit = 20,
            minimumLevel = "info"
        }, cancellationToken);
        context.EnsureSucceeded(logs, "Listing correlated logs for the debug-action operation");

        var logNodes = JsonNodeHelpers.ReadArray(logs.StructuredContent, "logs");
        if (!logNodes.Any(log =>
            string.Equals(JsonNodeHelpers.ReadString(log, "OperationId"), threatPointsOperationId, StringComparison.Ordinal)
            && string.Equals(JsonNodeHelpers.ReadString(log, "CapabilityId"), "rimbridge.core/debug_actions/execute-debug-action", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(JsonNodeHelpers.ReadString(log, "Message")) == false))
        {
            throw new InvalidOperationException("The correlated log journal did not return the expected debug-action log entry.");
        }

        context.SetSummaryValue("selectedPawn", pawnName);
        context.SetSummaryValue("selectedPawnId", pawnId);
        context.SetSummaryValue("selectedMapId", mapId);
        context.SetSummaryValue("draftGizmoLabel", draftGizmoLabel);
        context.SetSummaryValue("screenshotMessageId", screenshotMessageId);
        context.SetSummaryValue("choiceLetterId", choiceLetterId);
        context.SetSummaryValue("dismissedLetterId", dismissibleLetterId);
        context.SetSummaryValue("activatedAlertId", alertId);
        context.SetSummaryValue("observedOperationId", threatPointsOperationId);
        context.SetSummaryValue("baselineWindowCount", JsonNodeHelpers.ReadString(baselineUiState, "windowCount"));
        context.SetSummaryValue("finalAlertCount", JsonNodeHelpers.ReadString(alerts.StructuredContent, "returnedCount"));

        context.SetScenarioData("bridgeStatus", bridgeStatus.StructuredContent);
        context.SetScenarioData("capabilityList", capabilityList.StructuredContent);
        context.SetScenarioData("capabilityDetail", capabilityDetail.StructuredContent);
        context.SetScenarioData("colonists", colonists.StructuredContent);
        context.SetScenarioData("selectionSemantics", selectionSemantics.StructuredContent);
        context.SetScenarioData("selectedGizmos", gizmos.StructuredContent);
        context.SetScenarioData("executeGizmo", executeGizmo.StructuredContent);
        context.SetScenarioData("cameraState", cameraState.StructuredContent);
        context.SetScenarioData("messagesAfterScreenshot", messagesAfterScreenshot.StructuredContent);
        context.SetScenarioData("lettersAfterChoice", lettersAfterChoice.StructuredContent);
        context.SetScenarioData("openLetter", openLetter.StructuredContent);
        context.SetScenarioData("lettersAfterStandard", lettersAfterStandard.StructuredContent);
        context.SetScenarioData("dismissLetter", dismissLetter.StructuredContent);
        context.SetScenarioData("alerts", alerts.StructuredContent);
        context.SetScenarioData("trackedOperation", trackedOperation.StructuredContent);
        context.SetScenarioData("recentOperations", recentOperations.StructuredContent);
        context.SetScenarioData("operationEventsByOperation", operationEvents.StructuredContent);
        context.SetScenarioData("logsByOperation", logs.StructuredContent);

        var clearSelection = await context.CallGameToolAsync("semantic.clear_selection", "rimworld/clear_selection", new { }, cancellationToken);
        context.EnsureSucceeded(clearSelection, "Clearing the selection at the end of the semantic diagnostics roundtrip");

        var observation = await observationWindow.CaptureAsync(
            "semantic.final_bridge_status",
            "semantic.collect_operation_events",
            "semantic.collect_logs",
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

    private static async Task RunScriptWallSequenceAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("script_wall.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("script_wall.snapshot_bridge_status", cancellationToken);
        var initialDesignatorState = await context.CallGameToolAsync("script_wall.get_designator_state_before", "rimworld/get_designator_state", new { }, cancellationToken);
        context.EnsureSucceeded(initialDesignatorState, "Reading initial architect state for the script wall sequence");
        var originalGodMode = JsonNodeHelpers.ReadBoolean(initialDesignatorState.StructuredContent, "godMode") == true;

        Exception? scenarioError = null;

        try
        {
            var categories = await context.CallGameToolAsync("script_wall.list_categories", "rimworld/list_architect_categories", new
            {
                includeHidden = false,
                includeEmpty = false
            }, cancellationToken);
            context.EnsureSucceeded(categories, "Listing architect categories for the script wall sequence");

            var categoryArray = JsonNodeHelpers.ReadArray(categories.StructuredContent, "categories");
            var structureCategoryId = ResolveArchitectCategoryId(categoryArray, "Structure");

            var designators = await context.CallGameToolAsync("script_wall.list_structure_designators", "rimworld/list_architect_designators", new
            {
                categoryId = structureCategoryId,
                includeHidden = false
            }, cancellationToken);
            context.EnsureSucceeded(designators, "Listing structure designators for the script wall sequence");

            var designatorArray = JsonNodeHelpers.ReadArray(designators.StructuredContent, "designators");
            var wallDesignatorId = ResolveArchitectBuildDesignatorId(designatorArray, "Wall");

            var colonists = await context.CallGameToolAsync("script_wall.list_colonists", "rimworld/list_colonists", new
            {
                currentMapOnly = true
            }, cancellationToken);
            context.EnsureSucceeded(colonists, "Listing current-map colonists for the script wall sequence");

            var blockedCells = new HashSet<string>(StringComparer.Ordinal);
            var firstWallCell = await FindAcceptedArchitectPlacementCellAsync(
                context,
                "script_wall.find_first_cell",
                wallDesignatorId,
                colonists.StructuredContent,
                blockedCells,
                cancellationToken,
                minRadius: 18,
                maxRadius: 30);
            var secondWallCell = await FindAcceptedArchitectPlacementCellAsync(
                context,
                "script_wall.find_second_cell",
                wallDesignatorId,
                colonists.StructuredContent,
                blockedCells,
                cancellationToken,
                minRadius: 18,
                maxRadius: 34);

            var screenshotFileName = "rimbridge_script_wall_sequence_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var scriptJson = new JsonObject
            {
                ["name"] = "script-wall-sequence",
                ["continueOnError"] = false,
                ["steps"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "god_on",
                        ["call"] = "rimworld/set_god_mode",
                        ["arguments"] = new JsonObject
                        {
                            ["enabled"] = true
                        }
                    },
                    new JsonObject
                    {
                        ["id"] = "wall_a",
                        ["call"] = "rimworld/apply_architect_designator",
                        ["arguments"] = new JsonObject
                        {
                            ["designatorId"] = wallDesignatorId,
                            ["x"] = firstWallCell.X,
                            ["z"] = firstWallCell.Z,
                            ["keepSelected"] = false
                        }
                    },
                    new JsonObject
                    {
                        ["id"] = "wall_b",
                        ["call"] = "rimworld/apply_architect_designator",
                        ["arguments"] = new JsonObject
                        {
                            ["designatorId"] = wallDesignatorId,
                            ["x"] = secondWallCell.X,
                            ["z"] = secondWallCell.Z,
                            ["keepSelected"] = false
                        }
                    },
                    new JsonObject
                    {
                        ["id"] = "capture",
                        ["call"] = "rimworld/take_screenshot",
                        ["arguments"] = new JsonObject
                        {
                            ["fileName"] = screenshotFileName,
                            ["includeTargets"] = false
                        }
                    }
                }
            }.ToJsonString();

            var runScript = await context.CallGameToolAsync("script_wall.run_script", "rimbridge/run_script", new
            {
                scriptJson,
                includeStepResults = true
            }, cancellationToken);
            context.EnsureSucceeded(runScript, "Running the script-driven wall placement sequence");

            var scriptReport = JsonNodeHelpers.ReadObject(runScript.StructuredContent, "script");
            if (JsonNodeHelpers.ReadInt32(scriptReport, "stepCount").GetValueOrDefault() != 4)
                throw new InvalidOperationException("Expected the wall sequence script to report exactly four steps.");
            if (JsonNodeHelpers.ReadInt32(scriptReport, "executedStepCount").GetValueOrDefault() != 4)
                throw new InvalidOperationException("Expected the wall sequence script to execute all four steps.");
            if (JsonNodeHelpers.ReadBoolean(scriptReport, "success") != true)
                throw new InvalidOperationException("Expected the wall sequence script report to succeed.");

            var scriptSteps = JsonNodeHelpers.ReadArray(scriptReport, "steps");
            var captureStep = scriptSteps.FirstOrDefault(step => string.Equals(JsonNodeHelpers.ReadString(step, "id"), "capture", StringComparison.Ordinal));
            var scriptScreenshotPath = JsonNodeHelpers.ReadString(captureStep, "result", "path");
            if (string.IsNullOrWhiteSpace(scriptScreenshotPath) || !File.Exists(scriptScreenshotPath))
                throw new InvalidOperationException("Expected the capture step to produce a valid screenshot artifact.");

            var firstCellInfo = await context.CallGameToolAsync("script_wall.get_first_cell_info", "rimworld/get_cell_info", new
            {
                x = firstWallCell.X,
                z = firstWallCell.Z
            }, cancellationToken);
            context.EnsureSucceeded(firstCellInfo, $"Reading cell info for script-placed wall cell ({firstWallCell.X}, {firstWallCell.Z})");
            AssertCellDoesNotContainBuildBlueprint(firstCellInfo.StructuredContent, "Wall");
            AssertCellContainsSolidThing(firstCellInfo.StructuredContent, "Wall");

            var secondCellInfo = await context.CallGameToolAsync("script_wall.get_second_cell_info", "rimworld/get_cell_info", new
            {
                x = secondWallCell.X,
                z = secondWallCell.Z
            }, cancellationToken);
            context.EnsureSucceeded(secondCellInfo, $"Reading cell info for script-placed wall cell ({secondWallCell.X}, {secondWallCell.Z})");
            AssertCellDoesNotContainBuildBlueprint(secondCellInfo.StructuredContent, "Wall");
            AssertCellContainsSolidThing(secondCellInfo.StructuredContent, "Wall");

            if (context.HumanVerificationEnabled)
            {
                await context.ExportHumanVerificationArtifactAsync(
                    "script_wall_sequence",
                    scriptScreenshotPath,
                    "The script-driven wall sequence should have produced two directly built walls and captured its own screenshot artifact.",
                    [
                        "Two freshly placed walls should be visible near the center region of the view.",
                        "This image was produced by the script itself through a rimworld/take_screenshot step rather than by a separate harness capture."
                    ],
                    cancellationToken);
            }

            context.SetSummaryValue("structureCategoryId", structureCategoryId);
            context.SetSummaryValue("wallDesignatorId", wallDesignatorId);
            context.SetSummaryValue("firstWallCell", $"{firstWallCell.X},{firstWallCell.Z}");
            context.SetSummaryValue("secondWallCell", $"{secondWallCell.X},{secondWallCell.Z}");
            context.SetSummaryValue("scriptScreenshotPath", scriptScreenshotPath);
            context.SetScenarioData("architectCategories", new JsonArray(categoryArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("structureDesignators", new JsonArray(designatorArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("runScript", runScript.StructuredContent);
            context.SetScenarioData("firstWallCellInfo", firstCellInfo.StructuredContent);
            context.SetScenarioData("secondWallCellInfo", secondCellInfo.StructuredContent);
        }
        catch (Exception ex)
        {
            scenarioError = ex;
            throw;
        }
        finally
        {
            try
            {
                await EnsureGodModeAsync(context, "script_wall.restore_god_mode", originalGodMode, cancellationToken);
            }
            catch (Exception restoreError)
            {
                if (scenarioError is null)
                    throw;

                context.Note($"Failed to restore god mode after the script wall sequence: {restoreError.Message}");
            }
        }

        var observation = await observationWindow.CaptureAsync(
            "script_wall.final_bridge_status",
            "script_wall.collect_operation_events",
            "script_wall.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunScriptColonistPrisonAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("script_prison.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("script_prison.snapshot_bridge_status", cancellationToken);
        var initialDesignatorState = await context.CallGameToolAsync("script_prison.get_designator_state_before", "rimworld/get_designator_state", new { }, cancellationToken);
        context.EnsureSucceeded(initialDesignatorState, "Reading initial architect state for the script colonist prison scenario");
        var originalGodMode = JsonNodeHelpers.ReadBoolean(initialDesignatorState.StructuredContent, "godMode") == true;

        Exception? scenarioError = null;

        try
        {
            var screenshotFileName = "rimbridge_script_colonist_prison_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var scriptPath = GetLuaFixturePath("script-colonist-prison.lua");
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["screenshotFileName"] = screenshotFileName
            };

            var compileLua = await context.CallGameToolAsync("script_prison.compile_lua_file", "rimbridge/compile_lua_file", new
            {
                scriptPath,
                parameters
            }, cancellationToken);
            context.EnsureSucceeded(compileLua, "Compiling the file-backed Lua colonist prison sequence");

            var resolvedCompilePath = JsonNodeHelpers.ReadString(compileLua.StructuredContent, "resolvedScriptPath");
            if (!string.Equals(resolvedCompilePath, scriptPath, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected compile_lua_file to resolve '{scriptPath}' but received '{resolvedCompilePath}'.");

            var compiledScript = JsonNodeHelpers.ReadObject(compileLua.StructuredContent, "script");
            var compiledSteps = JsonNodeHelpers.ReadArray(compiledScript, "Steps");
            if (compiledSteps.Count == 0)
                throw new InvalidOperationException("Expected compile_lua_file to produce at least one lowered script step.");

            var runLua = await context.CallGameToolAsync("script_prison.run_lua_file", "rimbridge/run_lua_file", new
            {
                scriptPath,
                parameters,
                includeStepResults = true
            }, cancellationToken);
            context.EnsureSucceeded(runLua, "Running the file-backed Lua colonist prison sequence");

            var resolvedRunPath = JsonNodeHelpers.ReadString(runLua.StructuredContent, "resolvedScriptPath");
            if (!string.Equals(resolvedRunPath, scriptPath, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected run_lua_file to resolve '{scriptPath}' but received '{resolvedRunPath}'.");

            if (JsonNodeHelpers.ReadBoolean(runLua.StructuredContent, "returned") != true)
                throw new InvalidOperationException("Expected the Lua colonist prison script to end with a return value.");

            var scriptResult = JsonNodeHelpers.ReadObject(runLua.StructuredContent, "result");
            var scriptReport = JsonNodeHelpers.ReadObject(runLua.StructuredContent, "script");
            if (JsonNodeHelpers.ReadBoolean(scriptReport, "success") != true)
                throw new InvalidOperationException("Expected the colonist prison Lua script report to succeed.");

            var scriptSteps = JsonNodeHelpers.ReadArray(scriptReport, "steps");
            if (!scriptSteps.Any(step => (JsonNodeHelpers.ReadString(step, "id") ?? string.Empty).Contains("#2", StringComparison.Ordinal)))
                throw new InvalidOperationException("Expected the Lua colonist prison script report to include repeated loop or poll step ids.");

            var scriptOutput = JsonNodeHelpers.ReadArray(runLua.StructuredContent, "output");
            if (scriptOutput.Count < 3)
                throw new InvalidOperationException("Expected the Lua colonist prison script to emit structured output rows for planning and execution.");

            var outputMessages = scriptOutput
                .Select(entry => JsonNodeHelpers.ReadString(entry, "message"))
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToHashSet(StringComparer.Ordinal);
            if (!outputMessages.Contains("planning_attempts")
                || !outputMessages.Contains("rally")
                || !outputMessages.Contains("grouped_attempts"))
            {
                throw new InvalidOperationException("Expected the Lua colonist prison script output to include 'planning_attempts', 'rally', and 'grouped_attempts' trace rows.");
            }

            if (!scriptSteps.Any(step => string.Equals(JsonNodeHelpers.ReadString(step, "call"), "rimworld/find_random_cell_near", StringComparison.Ordinal)))
                throw new InvalidOperationException("Expected the Lua colonist prison script to resolve the rally cell inside the script through rimworld/find_random_cell_near.");

            if (!scriptSteps.Any(step => string.Equals(JsonNodeHelpers.ReadString(step, "call"), "rimworld/list_architect_designators", StringComparison.Ordinal)))
                throw new InvalidOperationException("Expected the Lua colonist prison script to resolve the wall designator inside the script.");

            var captureStep = scriptSteps.FirstOrDefault(step =>
                string.Equals(JsonNodeHelpers.ReadString(step, "call"), "rimworld/take_screenshot", StringComparison.Ordinal));
            var scriptScreenshotPath = JsonNodeHelpers.ReadString(scriptResult, "screenshotPath");
            var captureScreenshotPath = JsonNodeHelpers.ReadString(captureStep, "result", "path");
            if (string.IsNullOrWhiteSpace(scriptScreenshotPath) || !File.Exists(scriptScreenshotPath))
                throw new InvalidOperationException("Expected the capture step to produce a valid screenshot artifact.");
            if (!string.Equals(scriptScreenshotPath, captureScreenshotPath, StringComparison.Ordinal))
                throw new InvalidOperationException("Expected the Lua script return value to surface the same screenshot path returned by the capture step.");
            var wallDesignatorId = JsonNodeHelpers.ReadString(scriptResult, "wallDesignatorId");
            var rallyX = JsonNodeHelpers.ReadInt32(scriptResult, "rallyX");
            var rallyZ = JsonNodeHelpers.ReadInt32(scriptResult, "rallyZ");
            var interiorMinX = JsonNodeHelpers.ReadInt32(scriptResult, "interiorMinX");
            var interiorMaxX = JsonNodeHelpers.ReadInt32(scriptResult, "interiorMaxX");
            var interiorMinZ = JsonNodeHelpers.ReadInt32(scriptResult, "interiorMinZ");
            var interiorMaxZ = JsonNodeHelpers.ReadInt32(scriptResult, "interiorMaxZ");
            if (string.IsNullOrWhiteSpace(wallDesignatorId))
                throw new InvalidOperationException("Expected the Lua script to return the resolved wall designator id.");
            if (!rallyX.HasValue || !rallyZ.HasValue || !interiorMinX.HasValue || !interiorMaxX.HasValue || !interiorMinZ.HasValue || !interiorMaxZ.HasValue)
                throw new InvalidOperationException("Expected the Lua script to return rally and interior bounds.");
            if (JsonNodeHelpers.ReadInt32(scriptResult, "planningAttempts").GetValueOrDefault() < 1)
                throw new InvalidOperationException("Expected the Lua script return value to include planningAttempts >= 1.");
            if (JsonNodeHelpers.ReadInt32(scriptResult, "groupedAttempts").GetValueOrDefault() < 1)
                throw new InvalidOperationException("Expected the Lua script return value to include groupedAttempts >= 1.");
            if (JsonNodeHelpers.ReadInt32(scriptResult, "capturedAttempts").GetValueOrDefault() < 1)
                throw new InvalidOperationException("Expected the Lua script return value to include capturedAttempts >= 1.");

            foreach (var perimeterCell in new[]
                     {
                         (rallyX.Value, interiorMinZ.Value - 1),
                         (rallyX.Value, interiorMaxZ.Value + 1),
                         (interiorMinX.Value - 1, rallyZ.Value),
                         (interiorMaxX.Value + 1, rallyZ.Value)
                     })
            {
                var cellInfo = await context.CallGameToolAsync($"script_prison.verify_wall_{perimeterCell.Item1}_{perimeterCell.Item2}", "rimworld/get_cell_info", new
                {
                    x = perimeterCell.Item1,
                    z = perimeterCell.Item2
                }, cancellationToken);
                context.EnsureSucceeded(cellInfo, $"Reading prison perimeter cell info for ({perimeterCell.Item1}, {perimeterCell.Item2})");
                AssertCellContainsSolidThing(cellInfo.StructuredContent, "Wall");
                AssertCellDoesNotContainBuildBlueprint(cellInfo.StructuredContent, "Wall");
            }

            var finalColonists = await context.CallGameToolAsync("script_prison.final_colonists", "rimworld/list_colonists", new
            {
                currentMapOnly = true
            }, cancellationToken);
            context.EnsureSucceeded(finalColonists, "Listing final colonists after the script colonist prison sequence");

            var finalColonistArray = JsonNodeHelpers.ReadArray(finalColonists.StructuredContent, "colonists");
            if (finalColonistArray.Count < 3)
                throw new InvalidOperationException("Expected at least three colonists after the script colonist prison sequence.");

            foreach (var colonist in finalColonistArray.Take(3))
            {
                if (JsonNodeHelpers.ReadBoolean(colonist, "drafted") == true)
                    throw new InvalidOperationException($"Colonist '{JsonNodeHelpers.ReadString(colonist, "name")}' was still drafted after the prison script.");

                var x = JsonNodeHelpers.ReadInt32(colonist, "position", "x");
                var z = JsonNodeHelpers.ReadInt32(colonist, "position", "z");
                if (!x.HasValue || !z.HasValue || x.Value < interiorMinX.Value || x.Value > interiorMaxX.Value || z.Value < interiorMinZ.Value || z.Value > interiorMaxZ.Value)
                    throw new InvalidOperationException($"Colonist '{JsonNodeHelpers.ReadString(colonist, "name")}' was not inside the prison interior after the script.");
            }

            var interiorFlood = await context.CallGameToolAsync("script_prison.verify_interior_flood", "rimworld/flood_fill_cells", new
            {
                x = rallyX.Value,
                z = rallyZ.Value,
                maxCellsToProcess = 16,
                minimumCellCount = 9,
                maxReturnedCells = 16,
                requireWalkable = true,
                requireStandable = true,
                requireNoImpassableThings = true
            }, cancellationToken);
            context.EnsureSucceeded(interiorFlood, "Flood-filling the enclosed prison interior");

            if (JsonNodeHelpers.ReadInt32(interiorFlood.StructuredContent, "visitedCellCount").GetValueOrDefault() != 9)
                throw new InvalidOperationException("Expected the final prison interior flood fill to contain exactly 9 reachable interior cells.");

            if (context.HumanVerificationEnabled)
            {
                await context.ExportHumanVerificationArtifactAsync(
                    "script_colonist_prison",
                    scriptScreenshotPath,
                    "The script-driven colonist prison scenario should show the starting colonists enclosed by a full wall perimeter after the script grouped, enclosed, and undrafted them.",
                    [
                        "A full wall ring should enclose the default starting colonists.",
                        "The colonists should be inside the prison rather than standing on the wall perimeter.",
                        "This image was produced by the script itself after the final unpause and capture steps."
                    ],
                    cancellationToken);
            }

            context.SetSummaryValue("wallDesignatorId", wallDesignatorId);
            context.SetSummaryValue("rallyCell", $"{rallyX.Value},{rallyZ.Value}");
            context.SetSummaryValue("interiorRect", $"{interiorMinX.Value},{interiorMinZ.Value}..{interiorMaxX.Value},{interiorMaxZ.Value}");
            context.SetSummaryValue("scriptScreenshotPath", scriptScreenshotPath);
            context.SetScenarioData("compileLua", compileLua.StructuredContent);
            context.SetScenarioData("runLua", runLua.StructuredContent);
            context.SetScenarioData("finalColonists", finalColonists.StructuredContent);
            context.SetScenarioData("interiorFlood", interiorFlood.StructuredContent);
        }
        catch (Exception ex)
        {
            scenarioError = ex;
            throw;
        }
        finally
        {
            try
            {
                await EnsureGodModeAsync(context, "script_prison.restore_god_mode", originalGodMode, cancellationToken);
            }
            catch (Exception restoreError)
            {
                if (scenarioError is null)
                    throw;

                context.Note($"Failed to restore god mode after the script colonist prison sequence: {restoreError.Message}");
            }
        }

        var observation = await observationWindow.CaptureAsync(
            "script_prison.final_bridge_status",
            "script_prison.collect_operation_events",
            "script_prison.collect_logs",
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

    private static async Task RunArchitectWallPlacementAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("architect.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("architect.snapshot_bridge_status", cancellationToken);
        var initialDesignatorState = await context.CallGameToolAsync("architect.get_designator_state", "rimworld/get_designator_state", new { }, cancellationToken);
        context.EnsureSucceeded(initialDesignatorState, "Reading initial architect state");
        var originalGodMode = JsonNodeHelpers.ReadBoolean(initialDesignatorState.StructuredContent, "godMode") == true;

        Exception? scenarioError = null;
        try
        {
            var categories = await context.CallGameToolAsync("architect.list_categories", "rimworld/list_architect_categories", new
            {
                includeHidden = false,
                includeEmpty = false
            }, cancellationToken);
            context.EnsureSucceeded(categories, "Listing architect categories");

            var categoryArray = JsonNodeHelpers.ReadArray(categories.StructuredContent, "categories");
            var structureCategoryId = ResolveArchitectCategoryId(categoryArray, "Structure");

            var designators = await context.CallGameToolAsync("architect.list_structure_designators", "rimworld/list_architect_designators", new
            {
                categoryId = structureCategoryId,
                includeHidden = false
            }, cancellationToken);
            context.EnsureSucceeded(designators, "Listing structure designators");

            var designatorArray = JsonNodeHelpers.ReadArray(designators.StructuredContent, "designators");
            var wallDesignatorId = ResolveArchitectBuildDesignatorId(designatorArray, "Wall");

            var colonists = await context.CallGameToolAsync("architect.list_colonists", "rimworld/list_colonists", new
            {
                currentMapOnly = true
            }, cancellationToken);
            context.EnsureSucceeded(colonists, "Listing current-map colonists for architect placement");

            await EnsureGodModeAsync(context, "architect.disable_god_mode", false, cancellationToken);

            var blockedCells = new HashSet<string>(StringComparer.Ordinal);
            var blueprintCell = await FindAcceptedArchitectPlacementCellAsync(
                context,
                "architect.find_blueprint_cell",
                wallDesignatorId,
                colonists.StructuredContent,
                blockedCells,
                cancellationToken);
            blockedCells.Add(CellKey(blueprintCell.X, blueprintCell.Z));

            var blueprintPlacement = await context.CallGameToolAsync("architect.apply_blueprint_wall", "rimworld/apply_architect_designator", new
            {
                designatorId = wallDesignatorId,
                x = blueprintCell.X,
                z = blueprintCell.Z,
                keepSelected = false
            }, cancellationToken);
            context.EnsureSucceeded(blueprintPlacement, "Applying wall designator with god mode disabled");

            var blueprintCellInfo = await context.CallGameToolAsync("architect.get_blueprint_cell_info", "rimworld/get_cell_info", new
            {
                x = blueprintCell.X,
                z = blueprintCell.Z
            }, cancellationToken);
            context.EnsureSucceeded(blueprintCellInfo, $"Reading cell info for blueprint wall cell ({blueprintCell.X}, {blueprintCell.Z})");

            AssertCellContainsBuildBlueprint(blueprintCellInfo.StructuredContent, "Wall");
            AssertCellDoesNotContainSolidThing(blueprintCellInfo.StructuredContent, "Wall");

            if (context.HumanVerificationEnabled)
            {
                var jumpBlueprintCell = await context.CallGameToolAsync("architect.jump_camera_to_blueprint_cell", "rimworld/jump_camera_to_cell", new
                {
                    x = blueprintCell.X,
                    z = blueprintCell.Z
                }, cancellationToken);
                context.EnsureSucceeded(jumpBlueprintCell, $"Jumping camera to architect blueprint cell ({blueprintCell.X}, {blueprintCell.Z})");

                await context.CaptureHumanVerificationScreenshotAsync(
                    "human_verify.architect_blueprint_wall",
                    "architect_blueprint_wall",
                    "A wall placed with god mode disabled should remain a blueprint while the game is paused.",
                    [
                        "A translucent wooden wall blueprint should be visible near the center of the view.",
                        "There should not be a fully built wooden wall at that exact target cell.",
                        "This confirms the non-god-mode Architect path creates blueprints instead of instant structures."
                    ],
                    cancellationToken);
            }

            await EnsureGodModeAsync(context, "architect.enable_god_mode", true, cancellationToken);

            var directCell = await FindAcceptedArchitectPlacementCellAsync(
                context,
                "architect.find_direct_cell",
                wallDesignatorId,
                colonists.StructuredContent,
                blockedCells,
                cancellationToken);
            blockedCells.Add(CellKey(directCell.X, directCell.Z));

            var directPlacement = await context.CallGameToolAsync("architect.apply_direct_wall", "rimworld/apply_architect_designator", new
            {
                designatorId = wallDesignatorId,
                x = directCell.X,
                z = directCell.Z,
                keepSelected = false
            }, cancellationToken);
            context.EnsureSucceeded(directPlacement, "Applying wall designator with god mode enabled");

            var directCellInfo = await context.CallGameToolAsync("architect.get_direct_cell_info", "rimworld/get_cell_info", new
            {
                x = directCell.X,
                z = directCell.Z
            }, cancellationToken);
            context.EnsureSucceeded(directCellInfo, $"Reading cell info for direct wall cell ({directCell.X}, {directCell.Z})");

            AssertCellContainsSolidThing(directCellInfo.StructuredContent, "Wall");
            AssertCellDoesNotContainBuildBlueprint(directCellInfo.StructuredContent, "Wall");

            if (context.HumanVerificationEnabled)
            {
                var jumpDirectCell = await context.CallGameToolAsync("architect.jump_camera_to_direct_cell", "rimworld/jump_camera_to_cell", new
                {
                    x = directCell.X,
                    z = directCell.Z
                }, cancellationToken);
                context.EnsureSucceeded(jumpDirectCell, $"Jumping camera to architect direct-build cell ({directCell.X}, {directCell.Z})");

                await context.CaptureHumanVerificationScreenshotAsync(
                    "human_verify.architect_direct_wall",
                    "architect_direct_wall",
                    "A wall placed with god mode enabled should appear immediately as a finished structure.",
                    [
                        "A solid wooden wall should be visible near the center of the view.",
                        "There should not be a translucent blueprint overlay at that exact target cell.",
                        "This confirms the god-mode Architect path creates the finished structure directly."
                    ],
                    cancellationToken);
            }

            context.SetSummaryValue("structureCategoryId", structureCategoryId);
            context.SetSummaryValue("wallDesignatorId", wallDesignatorId);
            context.SetSummaryValue("blueprintCell", $"{blueprintCell.X},{blueprintCell.Z}");
            context.SetSummaryValue("directCell", $"{directCell.X},{directCell.Z}");
            context.SetScenarioData("architectCategories", new JsonArray(categoryArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("structureDesignators", new JsonArray(designatorArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("blueprintPlacement", blueprintPlacement.StructuredContent);
            context.SetScenarioData("blueprintCellInfo", blueprintCellInfo.StructuredContent);
            context.SetScenarioData("directPlacement", directPlacement.StructuredContent);
            context.SetScenarioData("directCellInfo", directCellInfo.StructuredContent);
        }
        catch (Exception ex)
        {
            scenarioError = ex;
            throw;
        }
        finally
        {
            try
            {
                await EnsureGodModeAsync(context, "architect.restore_god_mode", originalGodMode, cancellationToken);
            }
            catch (Exception restoreError)
            {
                if (scenarioError is null)
                    throw;

                context.Note($"Failed to restore god mode after architect wall placement scenario: {restoreError.Message}");
            }
        }

        var observation = await observationWindow.CaptureAsync(
            "architect.final_bridge_status",
            "architect.collect_operation_events",
            "architect.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunArchitectFloorDropdownAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("architect_floor.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("architect_floor.snapshot_bridge_status", cancellationToken);
        var initialDesignatorState = await context.CallGameToolAsync("architect_floor.get_designator_state", "rimworld/get_designator_state", new { }, cancellationToken);
        context.EnsureSucceeded(initialDesignatorState, "Reading initial architect state for the floor dropdown scenario");
        var originalGodMode = JsonNodeHelpers.ReadBoolean(initialDesignatorState.StructuredContent, "godMode") == true;

        Exception? scenarioError = null;
        try
        {
            var categories = await context.CallGameToolAsync("architect_floor.list_categories", "rimworld/list_architect_categories", new
            {
                includeHidden = false,
                includeEmpty = false
            }, cancellationToken);
            context.EnsureSucceeded(categories, "Listing architect categories for the floor dropdown scenario");

            var categoryArray = JsonNodeHelpers.ReadArray(categories.StructuredContent, "categories");
            var floorsCategoryId = ResolveArchitectCategoryId(categoryArray, "Floors");

            var designators = await context.CallGameToolAsync("architect_floor.list_floor_designators", "rimworld/list_architect_designators", new
            {
                categoryId = floorsCategoryId,
                includeHidden = false
            }, cancellationToken);
            context.EnsureSucceeded(designators, "Listing floor designators");

            var designatorArray = JsonNodeHelpers.ReadArray(designators.StructuredContent, "designators");
            var dropdownFloorDesignatorId = ResolveArchitectBuildDesignatorId(designatorArray, "MetalTile", requireDropdownChild: true);
            var dropdownParentId = ResolveArchitectParentId(designatorArray, dropdownFloorDesignatorId);

            var selectFloor = await context.CallGameToolAsync("architect_floor.select_floor_designator", "rimworld/select_architect_designator", new
            {
                designatorId = dropdownFloorDesignatorId
            }, cancellationToken);
            context.EnsureSucceeded(selectFloor, "Selecting the dropdown child floor designator");

            var selectedFloorState = await context.CallGameToolAsync("architect_floor.get_selected_state", "rimworld/get_designator_state", new { }, cancellationToken);
            context.EnsureSucceeded(selectedFloorState, "Reading architect selection state after selecting the floor designator");

            var selectedDesignatorId = JsonNodeHelpers.ReadString(selectedFloorState.StructuredContent, "designatorState", "selectedDesignatorId");
            var selectedContainerId = JsonNodeHelpers.ReadString(selectedFloorState.StructuredContent, "designatorState", "selectedContainerDesignatorId");
            if (!string.Equals(selectedDesignatorId, dropdownFloorDesignatorId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected selected floor designator '{dropdownFloorDesignatorId}', but got '{selectedDesignatorId ?? "<null>"}'.");
            if (!string.Equals(selectedContainerId, dropdownParentId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected selected floor dropdown container '{dropdownParentId}', but got '{selectedContainerId ?? "<null>"}'.");

            var colonists = await context.CallGameToolAsync("architect_floor.list_colonists", "rimworld/list_colonists", new
            {
                currentMapOnly = true
            }, cancellationToken);
            context.EnsureSucceeded(colonists, "Listing current-map colonists for floor placement");

            await EnsureGodModeAsync(context, "architect_floor.enable_god_mode", true, cancellationToken);

            var blockedCells = new HashSet<string>(StringComparer.Ordinal);
            var floorRect = await FindAcceptedArchitectPlacementCellAsync(
                context,
                "architect_floor.find_rect",
                dropdownFloorDesignatorId,
                colonists.StructuredContent,
                blockedCells,
                cancellationToken,
                width: 2,
                height: 2,
                minRadius: 20,
                maxRadius: 34);

            var floorPlacement = await context.CallGameToolAsync("architect_floor.apply_direct_floor", "rimworld/apply_architect_designator", new
            {
                designatorId = dropdownFloorDesignatorId,
                x = floorRect.X,
                z = floorRect.Z,
                width = 2,
                height = 2,
                keepSelected = false
            }, cancellationToken);
            context.EnsureSucceeded(floorPlacement, "Applying the dropdown child floor designator");

            var floorCellInfos = await ReadRectangleCellInfosAsync(
                context,
                "architect_floor.get_floor_cell_info",
                floorRect.X,
                floorRect.Z,
                2,
                2,
                cancellationToken);

            foreach (var floorCellInfo in floorCellInfos)
            {
                var terrainDefName = JsonNodeHelpers.ReadString(floorCellInfo.StructuredContent, "cell", "terrainDefName");
                if (!string.Equals(terrainDefName, "MetalTile", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Expected MetalTile terrain after direct floor placement, but got '{terrainDefName ?? "<null>"}'.");

                AssertCellDoesNotContainBuildBlueprint(floorCellInfo.StructuredContent, "MetalTile");
            }

            if (context.HumanVerificationEnabled)
            {
                var jumpFloorCell = await context.CallGameToolAsync("architect_floor.jump_camera_to_floor", "rimworld/jump_camera_to_cell", new
                {
                    x = floorRect.X,
                    z = floorRect.Z
                }, cancellationToken);
                context.EnsureSucceeded(jumpFloorCell, $"Jumping camera to dropdown floor cell ({floorRect.X}, {floorRect.Z})");

                await context.CaptureHumanVerificationScreenshotAsync(
                    "human_verify.architect_dropdown_floor",
                    "architect_dropdown_floor",
                    "A dropdown-selected steel floor should be placed directly over a 2x2 rectangle when god mode is enabled.",
                    [
                        "A small 2x2 patch of steel tiles should be visible near the center of the view.",
                        "This floor came from a dropdown child designator, not from a top-level Architect button.",
                        "There should not be blueprint overlays on those four floor cells."
                    ],
                    cancellationToken);
            }

            context.SetSummaryValue("floorsCategoryId", floorsCategoryId);
            context.SetSummaryValue("dropdownParentId", dropdownParentId);
            context.SetSummaryValue("dropdownFloorDesignatorId", dropdownFloorDesignatorId);
            context.SetSummaryValue("floorRect", $"{floorRect.X},{floorRect.Z} 2x2");
            context.SetScenarioData("floorCategories", new JsonArray(categoryArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("floorDesignators", new JsonArray(designatorArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("floorPlacement", floorPlacement.StructuredContent);
            context.SetScenarioData("floorCellInfos", new JsonArray(floorCellInfos.Select(response => JsonNodeHelpers.CloneNode(response.StructuredContent)).ToArray()));
        }
        catch (Exception ex)
        {
            scenarioError = ex;
            throw;
        }
        finally
        {
            try
            {
                await EnsureGodModeAsync(context, "architect_floor.restore_god_mode", originalGodMode, cancellationToken);
            }
            catch (Exception restoreError)
            {
                if (scenarioError is null)
                    throw;

                context.Note($"Failed to restore god mode after the floor dropdown scenario: {restoreError.Message}");
            }
        }

        var observation = await observationWindow.CaptureAsync(
            "architect_floor.final_bridge_status",
            "architect_floor.collect_operation_events",
            "architect_floor.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunArchitectZoneAreaDragAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("architect_zone.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("architect_zone.snapshot_bridge_status", cancellationToken);

        var categories = await context.CallGameToolAsync("architect_zone.list_categories", "rimworld/list_architect_categories", new
        {
            includeHidden = false,
            includeEmpty = false
        }, cancellationToken);
        context.EnsureSucceeded(categories, "Listing architect categories for the zone/area scenario");

        var categoryArray = JsonNodeHelpers.ReadArray(categories.StructuredContent, "categories");
        var zoneCategoryId = ResolveArchitectCategoryId(categoryArray, "Zone");

        var designators = await context.CallGameToolAsync("architect_zone.list_zone_designators", "rimworld/list_architect_designators", new
        {
            categoryId = zoneCategoryId,
            includeHidden = false
        }, cancellationToken);
        context.EnsureSucceeded(designators, "Listing zone designators");

        var designatorArray = JsonNodeHelpers.ReadArray(designators.StructuredContent, "designators");
        var stockpileDesignatorId = ResolveArchitectDesignatorIdByClassName(designatorArray, "RimWorld.Designator_ZoneAddStockpile_Resources");
        var homeAreaDesignatorId = ResolveArchitectDesignatorIdByClassName(designatorArray, "RimWorld.Designator_AreaHomeExpand");

        var colonists = await context.CallGameToolAsync("architect_zone.list_colonists", "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        context.EnsureSucceeded(colonists, "Listing current-map colonists for zone placement");

        var zonesBefore = await context.CallGameToolAsync("architect_zone.list_zones_before", "rimworld/list_zones", new
        {
            includeHidden = false,
            includeEmpty = false
        }, cancellationToken);
        context.EnsureSucceeded(zonesBefore, "Listing zones before stockpile placement");
        var zoneCountBefore = JsonNodeHelpers.ReadInt32(zonesBefore.StructuredContent, "count").GetValueOrDefault();
        var zoneArrayBefore = JsonNodeHelpers.ReadArray(zonesBefore.StructuredContent, "zones");

        var areasBefore = await context.CallGameToolAsync("architect_zone.list_areas_before", "rimworld/list_areas", new
        {
            includeEmpty = true,
            includeAssignableOnly = false
        }, cancellationToken);
        context.EnsureSucceeded(areasBefore, "Listing areas before home-area placement");
        var areaArrayBefore = JsonNodeHelpers.ReadArray(areasBefore.StructuredContent, "areas");
        var homeAreaBefore = ResolveAreaByClassName(areaArrayBefore, "RimWorld.Area_Home");
        var homeAreaCellCountBefore = JsonNodeHelpers.ReadInt32(homeAreaBefore, "cellCount").GetValueOrDefault();

        var blockedCells = new HashSet<string>(StringComparer.Ordinal);
        var stockpileRect = await FindAcceptedArchitectPlacementCellAsync(
            context,
            "architect_zone.find_stockpile_rect",
            stockpileDesignatorId,
            colonists.StructuredContent,
            blockedCells,
            cancellationToken,
            width: 3,
            height: 2,
            minRadius: 20,
            maxRadius: 34,
            preflightAsync: RectangleHasNoZoneAsync);

        var stockpilePlacement = await context.CallGameToolAsync("architect_zone.apply_stockpile_zone", "rimworld/apply_architect_designator", new
        {
            designatorId = stockpileDesignatorId,
            x = stockpileRect.X,
            z = stockpileRect.Z,
            width = 3,
            height = 2,
            keepSelected = false
        }, cancellationToken);
        context.EnsureSucceeded(stockpilePlacement, "Applying the stockpile zone designator");

        var stockpileCellInfo = await context.CallGameToolAsync("architect_zone.get_stockpile_cell_info", "rimworld/get_cell_info", new
        {
            x = stockpileRect.X,
            z = stockpileRect.Z
        }, cancellationToken);
        context.EnsureSucceeded(stockpileCellInfo, $"Reading cell info for stockpile zone cell ({stockpileRect.X}, {stockpileRect.Z})");

        var stockpileZone = JsonNodeHelpers.ReadObject(stockpileCellInfo.StructuredContent, "cell", "zone");
        var stockpileZoneId = JsonNodeHelpers.ReadString(stockpileZone, "id");
        if (string.IsNullOrWhiteSpace(stockpileZoneId))
            throw new InvalidOperationException("Expected the stockpile zone cell to report a zone id after placement.");
        if (!string.Equals(JsonNodeHelpers.ReadString(stockpileZone, "className"), "RimWorld.Zone_Stockpile", StringComparison.Ordinal))
            throw new InvalidOperationException("Expected the stockpile zone cell to report a RimWorld.Zone_Stockpile.");

        var zonesAfter = await context.CallGameToolAsync("architect_zone.list_zones_after", "rimworld/list_zones", new
        {
            includeHidden = false,
            includeEmpty = false
        }, cancellationToken);
        context.EnsureSucceeded(zonesAfter, "Listing zones after stockpile placement");
        var zoneCountAfter = JsonNodeHelpers.ReadInt32(zonesAfter.StructuredContent, "count").GetValueOrDefault();
        if (zoneCountAfter <= zoneCountBefore)
            throw new InvalidOperationException("Expected stockpile zone placement to increase the visible zone count.");

        var zoneArrayAfter = JsonNodeHelpers.ReadArray(zonesAfter.StructuredContent, "zones");
        var stockpileZoneDescriptor = ResolveZoneById(zoneArrayAfter, stockpileZoneId);
        if (JsonNodeHelpers.ReadInt32(stockpileZoneDescriptor, "cellCount").GetValueOrDefault() < 6)
            throw new InvalidOperationException("Expected the created stockpile zone to cover the full 3x2 rectangle.");
        if (zoneArrayBefore.Any(zone => string.Equals(JsonNodeHelpers.ReadString(zone, "id"), stockpileZoneId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Expected the stockpile zone to be newly created rather than reusing a pre-existing zone.");

        var homeAreaRect = await FindAcceptedArchitectPlacementCellAsync(
            context,
            "architect_zone.find_home_area_rect",
            homeAreaDesignatorId,
            colonists.StructuredContent,
            blockedCells,
            cancellationToken,
            width: 2,
            height: 2,
            minRadius: 24,
            maxRadius: 40,
            preflightAsync: RectangleHasNoHomeAreaAsync);

        var homeAreaPlacement = await context.CallGameToolAsync("architect_zone.apply_home_area", "rimworld/apply_architect_designator", new
        {
            designatorId = homeAreaDesignatorId,
            x = homeAreaRect.X,
            z = homeAreaRect.Z,
            width = 2,
            height = 2,
            keepSelected = false
        }, cancellationToken);
        context.EnsureSucceeded(homeAreaPlacement, "Applying the home-area designator");

        var homeAreaCellInfo = await context.CallGameToolAsync("architect_zone.get_home_area_cell_info", "rimworld/get_cell_info", new
        {
            x = homeAreaRect.X,
            z = homeAreaRect.Z
        }, cancellationToken);
        context.EnsureSucceeded(homeAreaCellInfo, $"Reading cell info for home-area cell ({homeAreaRect.X}, {homeAreaRect.Z})");
        AssertCellContainsArea(homeAreaCellInfo.StructuredContent, "RimWorld.Area_Home");

        var areasAfter = await context.CallGameToolAsync("architect_zone.list_areas_after", "rimworld/list_areas", new
        {
            includeEmpty = true,
            includeAssignableOnly = false
        }, cancellationToken);
        context.EnsureSucceeded(areasAfter, "Listing areas after home-area placement");
        var areaArrayAfter = JsonNodeHelpers.ReadArray(areasAfter.StructuredContent, "areas");
        var homeAreaAfter = ResolveAreaByClassName(areaArrayAfter, "RimWorld.Area_Home");
        var homeAreaCellCountAfter = JsonNodeHelpers.ReadInt32(homeAreaAfter, "cellCount").GetValueOrDefault();
        if (homeAreaCellCountAfter < homeAreaCellCountBefore + 4)
            throw new InvalidOperationException("Expected the home area to grow by at least the full 2x2 drag rectangle.");

        if (context.HumanVerificationEnabled)
        {
            var jumpStockpileCell = await context.CallGameToolAsync("architect_zone.jump_camera_to_stockpile", "rimworld/jump_camera_to_cell", new
            {
                x = stockpileRect.X,
                z = stockpileRect.Z
            }, cancellationToken);
            context.EnsureSucceeded(jumpStockpileCell, $"Jumping camera to stockpile zone cell ({stockpileRect.X}, {stockpileRect.Z})");

            await context.CaptureHumanVerificationScreenshotAsync(
                "human_verify.architect_stockpile_zone",
                "architect_stockpile_zone",
                "A 3x2 stockpile zone should be visible near the center of the map after rectangle drag placement.",
                [
                    "A tinted stockpile zone overlay should cover a small rectangular patch of cells near the center of the view.",
                    "This confirms rectangle drag placement created a new stockpile zone rather than a single-cell designation."
                ],
                cancellationToken);

            var jumpHomeAreaCell = await context.CallGameToolAsync("architect_zone.jump_camera_to_home_area", "rimworld/jump_camera_to_cell", new
            {
                x = homeAreaRect.X,
                z = homeAreaRect.Z
            }, cancellationToken);
            context.EnsureSucceeded(jumpHomeAreaCell, $"Jumping camera to home-area cell ({homeAreaRect.X}, {homeAreaRect.Z})");

            await context.CaptureHumanVerificationScreenshotAsync(
                "human_verify.architect_home_area",
                "architect_home_area",
                "A 2x2 home area should be visible after rectangle drag placement.",
                [
                    "A small home-area overlay should be visible near the center of the view.",
                    "This confirms area designators are reachable through the same Architect automation seam as build and zone tools."
                ],
                cancellationToken);
        }

        context.SetSummaryValue("zoneCategoryId", zoneCategoryId);
        context.SetSummaryValue("stockpileDesignatorId", stockpileDesignatorId);
        context.SetSummaryValue("homeAreaDesignatorId", homeAreaDesignatorId);
        context.SetSummaryValue("stockpileRect", $"{stockpileRect.X},{stockpileRect.Z} 3x2");
        context.SetSummaryValue("homeAreaRect", $"{homeAreaRect.X},{homeAreaRect.Z} 2x2");
        context.SetScenarioData("zoneCategories", new JsonArray(categoryArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
        context.SetScenarioData("zoneDesignators", new JsonArray(designatorArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
        context.SetScenarioData("zonesBefore", zonesBefore.StructuredContent);
        context.SetScenarioData("zonesAfter", zonesAfter.StructuredContent);
        context.SetScenarioData("areasBefore", areasBefore.StructuredContent);
        context.SetScenarioData("areasAfter", areasAfter.StructuredContent);
        context.SetScenarioData("stockpilePlacement", stockpilePlacement.StructuredContent);
        context.SetScenarioData("stockpileCellInfo", stockpileCellInfo.StructuredContent);
        context.SetScenarioData("homeAreaPlacement", homeAreaPlacement.StructuredContent);
        context.SetScenarioData("homeAreaCellInfo", homeAreaCellInfo.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "architect_zone.final_bridge_status",
            "architect_zone.collect_operation_events",
            "architect_zone.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static async Task RunArchitectStatefulTargetingAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("architect_state.wait_for_long_event_idle", cancellationToken);

        var observationWindow = await context.BeginObservationWindowAsync("architect_state.snapshot_bridge_status", cancellationToken);

        string? createdAllowedAreaId = null;
        string? createdZoneId = null;
        string? stockpileDesignatorId = null;
        (int X, int Z)? allowedAreaRect = null;
        ((int X, int Z) First, (int X, int Z) Second)? zoneRects = null;
        Exception? scenarioError = null;

        try
        {
            var categories = await context.CallGameToolAsync("architect_state.list_categories", "rimworld/list_architect_categories", new
            {
                includeHidden = false,
                includeEmpty = false
            }, cancellationToken);
            context.EnsureSucceeded(categories, "Listing architect categories for the stateful targeting scenario");

            var categoryArray = JsonNodeHelpers.ReadArray(categories.StructuredContent, "categories");
            var zoneCategoryId = ResolveArchitectCategoryId(categoryArray, "Zone");

            var designators = await context.CallGameToolAsync("architect_state.list_zone_designators", "rimworld/list_architect_designators", new
            {
                categoryId = zoneCategoryId,
                includeHidden = false
            }, cancellationToken);
            context.EnsureSucceeded(designators, "Listing zone designators for the stateful targeting scenario");

            var designatorArray = JsonNodeHelpers.ReadArray(designators.StructuredContent, "designators");
            stockpileDesignatorId = ResolveArchitectDesignatorIdByClassName(designatorArray, "RimWorld.Designator_ZoneAddStockpile_Resources");
            var allowedAreaExpandDesignatorId = ResolveArchitectDesignatorIdByClassName(designatorArray, "RimWorld.Designator_AreaAllowedExpand");

            var colonists = await context.CallGameToolAsync("architect_state.list_colonists", "rimworld/list_colonists", new
            {
                currentMapOnly = true
            }, cancellationToken);
            context.EnsureSucceeded(colonists, "Listing current-map colonists for the stateful targeting scenario");

            var zonesBefore = await context.CallGameToolAsync("architect_state.list_zones_before", "rimworld/list_zones", new
            {
                includeHidden = false,
                includeEmpty = false
            }, cancellationToken);
            context.EnsureSucceeded(zonesBefore, "Listing zones before the stateful targeting scenario");
            var zoneCountBefore = JsonNodeHelpers.ReadInt32(zonesBefore.StructuredContent, "count").GetValueOrDefault();

            var areasBefore = await context.CallGameToolAsync("architect_state.list_areas_before", "rimworld/list_areas", new
            {
                includeEmpty = true,
                includeAssignableOnly = false
            }, cancellationToken);
            context.EnsureSucceeded(areasBefore, "Listing areas before the stateful targeting scenario");
            var areaArrayBefore = JsonNodeHelpers.ReadArray(areasBefore.StructuredContent, "areas");
            var areaCountBefore = JsonNodeHelpers.ReadInt32(areasBefore.StructuredContent, "count").GetValueOrDefault();

            var allowedAreaLabel = "RimBridge Live Allowed " + DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var createAllowedArea = await context.CallGameToolAsync("architect_state.create_allowed_area", "rimworld/create_allowed_area", new
            {
                label = allowedAreaLabel,
                select = true
            }, cancellationToken);
            context.EnsureSucceeded(createAllowedArea, $"Creating allowed area '{allowedAreaLabel}'");

            createdAllowedAreaId = JsonNodeHelpers.ReadString(createAllowedArea.StructuredContent, "area", "id");
            if (string.IsNullOrWhiteSpace(createdAllowedAreaId))
                throw new InvalidOperationException("Expected create_allowed_area to return the new area id.");

            var selectAllowedArea = await context.CallGameToolAsync("architect_state.select_allowed_area", "rimworld/select_allowed_area", new
            {
                areaId = createdAllowedAreaId
            }, cancellationToken);
            context.EnsureSucceeded(selectAllowedArea, $"Selecting allowed area '{createdAllowedAreaId}'");
            if (!string.Equals(JsonNodeHelpers.ReadString(selectAllowedArea.StructuredContent, "selectedAllowedArea", "id"), createdAllowedAreaId, StringComparison.Ordinal))
                throw new InvalidOperationException("select_allowed_area did not report the requested allowed area as selected.");

            var designatorState = await context.CallGameToolAsync("architect_state.get_designator_state_after_select_area", "rimworld/get_designator_state", new { }, cancellationToken);
            context.EnsureSucceeded(designatorState, "Reading architect state after selecting the allowed area");
            if (!string.Equals(JsonNodeHelpers.ReadString(designatorState.StructuredContent, "designatorState", "selectedAllowedArea", "id"), createdAllowedAreaId, StringComparison.Ordinal))
                throw new InvalidOperationException("get_designator_state did not surface the selected allowed area.");

            var allowedAreaCandidateSet = new HashSet<string>(StringComparer.Ordinal);
            allowedAreaRect = await FindAcceptedArchitectPlacementCellAsync(
                context,
                "architect_state.find_allowed_area_rect",
                allowedAreaExpandDesignatorId,
                colonists.StructuredContent,
                allowedAreaCandidateSet,
                cancellationToken,
                width: 2,
                height: 2,
                minRadius: 28,
                maxRadius: 44,
                preflightAsync: (innerContext, x, z, width, height, token) => RectangleHasNoAreaAsync(innerContext, x, z, width, height, createdAllowedAreaId, token));

            var allowedAreaPlacement = await context.CallGameToolAsync("architect_state.apply_allowed_area", "rimworld/apply_architect_designator", new
            {
                designatorId = allowedAreaExpandDesignatorId,
                x = allowedAreaRect.Value.X,
                z = allowedAreaRect.Value.Z,
                width = 2,
                height = 2,
                keepSelected = false
            }, cancellationToken);
            context.EnsureSucceeded(allowedAreaPlacement, "Applying the selected allowed-area designator");

            var allowedAreaCellInfo = await context.CallGameToolAsync("architect_state.get_allowed_area_cell_info", "rimworld/get_cell_info", new
            {
                x = allowedAreaRect.Value.X,
                z = allowedAreaRect.Value.Z
            }, cancellationToken);
            context.EnsureSucceeded(allowedAreaCellInfo, $"Reading cell info for allowed-area cell ({allowedAreaRect.Value.X}, {allowedAreaRect.Value.Z})");
            AssertCellContainsAreaId(allowedAreaCellInfo.StructuredContent, createdAllowedAreaId);

            var areasAfterPlacement = await context.CallGameToolAsync("architect_state.list_areas_after_place", "rimworld/list_areas", new
            {
                includeEmpty = true,
                includeAssignableOnly = false
            }, cancellationToken);
            context.EnsureSucceeded(areasAfterPlacement, "Listing areas after allowed-area placement");
            var areaCountAfterCreate = JsonNodeHelpers.ReadInt32(areasAfterPlacement.StructuredContent, "count").GetValueOrDefault();
            if (areaCountAfterCreate <= areaCountBefore)
                throw new InvalidOperationException("Expected creating a custom allowed area to increase the visible area count.");

            var areaArrayAfterPlacement = JsonNodeHelpers.ReadArray(areasAfterPlacement.StructuredContent, "areas");
            var createdAreaAfterPlacement = ResolveAreaById(areaArrayAfterPlacement, createdAllowedAreaId);
            if (JsonNodeHelpers.ReadInt32(createdAreaAfterPlacement, "cellCount").GetValueOrDefault() < 4)
                throw new InvalidOperationException("Expected the created allowed area to cover the full 2x2 placement rectangle.");

            var zoneCandidateSet = new HashSet<string>(StringComparer.Ordinal);
            var firstZoneRect = await FindAcceptedArchitectPlacementCellAsync(
                context,
                "architect_state.find_first_zone_rect",
                stockpileDesignatorId,
                colonists.StructuredContent,
                zoneCandidateSet,
                cancellationToken,
                width: 3,
                height: 2,
                minRadius: 20,
                maxRadius: 34,
                preflightAsync: RectangleHasNoZoneAsync);

            var firstZonePlacement = await context.CallGameToolAsync("architect_state.apply_first_zone", "rimworld/apply_architect_designator", new
            {
                designatorId = stockpileDesignatorId,
                x = firstZoneRect.X,
                z = firstZoneRect.Z,
                width = 3,
                height = 2,
                keepSelected = false
            }, cancellationToken);
            context.EnsureSucceeded(firstZonePlacement, "Creating the initial stockpile zone");

            var firstZoneCellInfo = await context.CallGameToolAsync("architect_state.get_first_zone_cell_info", "rimworld/get_cell_info", new
            {
                x = firstZoneRect.X,
                z = firstZoneRect.Z
            }, cancellationToken);
            context.EnsureSucceeded(firstZoneCellInfo, $"Reading cell info for initial stockpile zone cell ({firstZoneRect.X}, {firstZoneRect.Z})");

            createdZoneId = JsonNodeHelpers.ReadString(firstZoneCellInfo.StructuredContent, "cell", "zone", "id");
            if (string.IsNullOrWhiteSpace(createdZoneId))
                throw new InvalidOperationException("Expected the initial stockpile zone cell to return a zone id.");

            var zonesAfterCreate = await context.CallGameToolAsync("architect_state.list_zones_after_create", "rimworld/list_zones", new
            {
                includeHidden = false,
                includeEmpty = false
            }, cancellationToken);
            context.EnsureSucceeded(zonesAfterCreate, "Listing zones after creating the first stockpile zone");
            var zoneCountAfterCreate = JsonNodeHelpers.ReadInt32(zonesAfterCreate.StructuredContent, "count").GetValueOrDefault();
            if (zoneCountAfterCreate <= zoneCountBefore)
                throw new InvalidOperationException("Expected the first stockpile zone to increase the zone count.");

            var zonesAfterCreateArray = JsonNodeHelpers.ReadArray(zonesAfterCreate.StructuredContent, "zones");
            var createdZoneAfterCreate = ResolveZoneById(zonesAfterCreateArray, createdZoneId);
            var createdZoneCellCountBeforeExpand = JsonNodeHelpers.ReadInt32(createdZoneAfterCreate, "cellCount").GetValueOrDefault();
            if (createdZoneCellCountBeforeExpand < 6)
                throw new InvalidOperationException("Expected the initial stockpile zone to cover the full 3x2 rectangle.");

            var setZoneTarget = await context.CallGameToolAsync("architect_state.set_zone_target", "rimworld/set_zone_target", new
            {
                designatorId = stockpileDesignatorId,
                zoneId = createdZoneId
            }, cancellationToken);
            context.EnsureSucceeded(setZoneTarget, $"Selecting existing zone '{createdZoneId}' as the stockpile target");
            if (!string.Equals(JsonNodeHelpers.ReadString(setZoneTarget.StructuredContent, "zone", "id"), createdZoneId, StringComparison.Ordinal))
                throw new InvalidOperationException("set_zone_target did not report the expected zone id.");

            var secondZoneRect = await FindAcceptedArchitectPlacementCellAsync(
                context,
                "architect_state.find_second_zone_rect",
                stockpileDesignatorId,
                colonists.StructuredContent,
                zoneCandidateSet,
                cancellationToken,
                width: 2,
                height: 2,
                minRadius: 36,
                maxRadius: 52,
                preflightAsync: RectangleHasNoZoneAsync);
            zoneRects = (firstZoneRect, secondZoneRect);

            var expandZonePlacement = await context.CallGameToolAsync("architect_state.expand_existing_zone", "rimworld/apply_architect_designator", new
            {
                designatorId = stockpileDesignatorId,
                x = secondZoneRect.X,
                z = secondZoneRect.Z,
                width = 2,
                height = 2,
                keepSelected = false
            }, cancellationToken);
            context.EnsureSucceeded(expandZonePlacement, "Expanding the existing stockpile zone through an explicit zone target");

            var secondZoneCellInfo = await context.CallGameToolAsync("architect_state.get_second_zone_cell_info", "rimworld/get_cell_info", new
            {
                x = secondZoneRect.X,
                z = secondZoneRect.Z
            }, cancellationToken);
            context.EnsureSucceeded(secondZoneCellInfo, $"Reading cell info for expanded stockpile zone cell ({secondZoneRect.X}, {secondZoneRect.Z})");

            var reusedZoneId = JsonNodeHelpers.ReadString(secondZoneCellInfo.StructuredContent, "cell", "zone", "id");
            if (!string.Equals(reusedZoneId, createdZoneId, StringComparison.Ordinal))
                throw new InvalidOperationException("Expected explicit zone targeting to reuse the existing stockpile zone instead of creating a new zone.");

            var zonesAfterExpand = await context.CallGameToolAsync("architect_state.list_zones_after_expand", "rimworld/list_zones", new
            {
                includeHidden = false,
                includeEmpty = false
            }, cancellationToken);
            context.EnsureSucceeded(zonesAfterExpand, "Listing zones after expanding the targeted stockpile zone");
            var zoneCountAfterExpand = JsonNodeHelpers.ReadInt32(zonesAfterExpand.StructuredContent, "count").GetValueOrDefault();
            if (zoneCountAfterExpand != zoneCountAfterCreate)
                throw new InvalidOperationException("Expected expanding an explicitly targeted zone to keep the visible zone count unchanged.");

            var zonesAfterExpandArray = JsonNodeHelpers.ReadArray(zonesAfterExpand.StructuredContent, "zones");
            var createdZoneAfterExpand = ResolveZoneById(zonesAfterExpandArray, createdZoneId);
            var createdZoneCellCountAfterExpand = JsonNodeHelpers.ReadInt32(createdZoneAfterExpand, "cellCount").GetValueOrDefault();
            if (createdZoneCellCountAfterExpand < createdZoneCellCountBeforeExpand + 4)
                throw new InvalidOperationException("Expected the targeted stockpile zone to grow by at least the 2x2 expansion rectangle.");

            if (context.HumanVerificationEnabled)
            {
                var jumpAllowedArea = await context.CallGameToolAsync("architect_state.jump_camera_to_allowed_area", "rimworld/jump_camera_to_cell", new
                {
                    x = allowedAreaRect.Value.X,
                    z = allowedAreaRect.Value.Z
                }, cancellationToken);
                context.EnsureSucceeded(jumpAllowedArea, $"Jumping camera to allowed-area cell ({allowedAreaRect.Value.X}, {allowedAreaRect.Value.Z})");

                await context.CaptureHumanVerificationScreenshotAsync(
                    "human_verify.architect_allowed_area",
                    "architect_allowed_area",
                    "A custom allowed area should be visible after selecting it explicitly and applying the allowed-area expand designator.",
                    [
                        "A small colored allowed-area overlay should be visible near the center of the map.",
                        "This area should come from a newly created custom allowed area, not the built-in Home area."
                    ],
                    cancellationToken);

                var jumpExpandedZone = await context.CallGameToolAsync("architect_state.jump_camera_to_expanded_zone", "rimworld/jump_camera_to_cell", new
                {
                    x = secondZoneRect.X,
                    z = secondZoneRect.Z
                }, cancellationToken);
                context.EnsureSucceeded(jumpExpandedZone, $"Jumping camera to expanded stockpile zone cell ({secondZoneRect.X}, {secondZoneRect.Z})");

                await context.CaptureHumanVerificationScreenshotAsync(
                    "human_verify.architect_existing_zone_expand",
                    "architect_existing_zone_expand",
                    "A stockpile zone overlay should be visible after explicitly targeting an existing zone and expanding it into a second rectangle.",
                    [
                        "A stockpile overlay should be visible near the center of the map at the second rectangle.",
                        "This second patch should belong to the same stockpile zone id created earlier rather than creating a new stockpile."
                    ],
                    cancellationToken);
            }

            context.SetSummaryValue("zoneCategoryId", zoneCategoryId);
            context.SetSummaryValue("allowedAreaExpandDesignatorId", allowedAreaExpandDesignatorId);
            context.SetSummaryValue("stockpileDesignatorId", stockpileDesignatorId);
            context.SetSummaryValue("createdAllowedAreaId", createdAllowedAreaId);
            context.SetSummaryValue("createdZoneId", createdZoneId);
            context.SetSummaryValue("allowedAreaRect", $"{allowedAreaRect.Value.X},{allowedAreaRect.Value.Z} 2x2");
            context.SetSummaryValue("firstZoneRect", $"{firstZoneRect.X},{firstZoneRect.Z} 3x2");
            context.SetSummaryValue("secondZoneRect", $"{secondZoneRect.X},{secondZoneRect.Z} 2x2");
            context.SetScenarioData("architectCategories", new JsonArray(categoryArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("zoneDesignators", new JsonArray(designatorArray.Select(JsonNodeHelpers.CloneNode).ToArray()));
            context.SetScenarioData("areasBefore", areasBefore.StructuredContent);
            context.SetScenarioData("createAllowedArea", createAllowedArea.StructuredContent);
            context.SetScenarioData("selectAllowedArea", selectAllowedArea.StructuredContent);
            context.SetScenarioData("allowedAreaPlacement", allowedAreaPlacement.StructuredContent);
            context.SetScenarioData("allowedAreaCellInfo", allowedAreaCellInfo.StructuredContent);
            context.SetScenarioData("areasAfterPlacement", areasAfterPlacement.StructuredContent);
            context.SetScenarioData("zonesBefore", zonesBefore.StructuredContent);
            context.SetScenarioData("firstZonePlacement", firstZonePlacement.StructuredContent);
            context.SetScenarioData("setZoneTarget", setZoneTarget.StructuredContent);
            context.SetScenarioData("expandZonePlacement", expandZonePlacement.StructuredContent);
            context.SetScenarioData("zonesAfterExpand", zonesAfterExpand.StructuredContent);
            context.SetScenarioData("secondZoneCellInfo", secondZoneCellInfo.StructuredContent);
        }
        catch (Exception ex)
        {
            scenarioError = ex;
            throw;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(createdZoneId) && !string.IsNullOrWhiteSpace(stockpileDesignatorId))
            {
                try
                {
                    var clearZoneTarget = await context.CallGameToolAsync("architect_state.clear_zone_target", "rimworld/set_zone_target", new
                    {
                        designatorId = stockpileDesignatorId
                    }, cancellationToken);
                    context.EnsureSucceeded(clearZoneTarget, "Clearing the explicit stockpile zone target during cleanup");
                }
                catch (Exception cleanupError)
                {
                    if (scenarioError is null)
                        throw;

                    context.Note($"Failed to clear the explicit zone target during cleanup: {cleanupError.Message}");
                }

                try
                {
                    var deleteZone = await context.CallGameToolAsync("architect_state.delete_zone", "rimworld/delete_zone", new
                    {
                        zoneId = createdZoneId
                    }, cancellationToken);
                    context.EnsureSucceeded(deleteZone, $"Deleting stockpile zone '{createdZoneId}' during cleanup");
                    context.SetScenarioData("deleteZone", deleteZone.StructuredContent);
                }
                catch (Exception cleanupError)
                {
                    if (scenarioError is null)
                        throw;

                    context.Note($"Failed to delete stockpile zone '{createdZoneId}' during cleanup: {cleanupError.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(createdAllowedAreaId))
            {
                try
                {
                    var clearArea = await context.CallGameToolAsync("architect_state.clear_area", "rimworld/clear_area", new
                    {
                        areaId = createdAllowedAreaId
                    }, cancellationToken);
                    context.EnsureSucceeded(clearArea, $"Clearing allowed area '{createdAllowedAreaId}' during cleanup");
                    context.SetScenarioData("clearArea", clearArea.StructuredContent);
                }
                catch (Exception cleanupError)
                {
                    if (scenarioError is null)
                        throw;

                    context.Note($"Failed to clear allowed area '{createdAllowedAreaId}' during cleanup: {cleanupError.Message}");
                }

                try
                {
                    var deleteArea = await context.CallGameToolAsync("architect_state.delete_area", "rimworld/delete_area", new
                    {
                        areaId = createdAllowedAreaId
                    }, cancellationToken);
                    context.EnsureSucceeded(deleteArea, $"Deleting allowed area '{createdAllowedAreaId}' during cleanup");
                    context.SetScenarioData("deleteArea", deleteArea.StructuredContent);
                }
                catch (Exception cleanupError)
                {
                    if (scenarioError is null)
                        throw;

                    context.Note($"Failed to delete allowed area '{createdAllowedAreaId}' during cleanup: {cleanupError.Message}");
                }
            }
        }

        var areasAfterCleanup = await context.CallGameToolAsync("architect_state.list_areas_after_cleanup", "rimworld/list_areas", new
        {
            includeEmpty = true,
            includeAssignableOnly = false
        }, cancellationToken);
        context.EnsureSucceeded(areasAfterCleanup, "Listing areas after cleanup");
        if (!string.IsNullOrWhiteSpace(createdAllowedAreaId))
        {
            var cleanedAreaArray = JsonNodeHelpers.ReadArray(areasAfterCleanup.StructuredContent, "areas");
            if (cleanedAreaArray.Any(area => string.Equals(JsonNodeHelpers.ReadString(area, "id"), createdAllowedAreaId, StringComparison.Ordinal)))
                throw new InvalidOperationException("Expected the created allowed area to be deleted during cleanup.");
        }

        var zonesAfterCleanup = await context.CallGameToolAsync("architect_state.list_zones_after_cleanup", "rimworld/list_zones", new
        {
            includeHidden = false,
            includeEmpty = false
        }, cancellationToken);
        context.EnsureSucceeded(zonesAfterCleanup, "Listing zones after cleanup");
        if (!string.IsNullOrWhiteSpace(createdZoneId))
        {
            var cleanedZoneArray = JsonNodeHelpers.ReadArray(zonesAfterCleanup.StructuredContent, "zones");
            if (cleanedZoneArray.Any(zone => string.Equals(JsonNodeHelpers.ReadString(zone, "id"), createdZoneId, StringComparison.Ordinal)))
                throw new InvalidOperationException("Expected the targeted stockpile zone to be deleted during cleanup.");
        }

        if (zoneRects.HasValue)
        {
            var postCleanupCellInfo = await context.CallGameToolAsync("architect_state.get_cleanup_zone_cell_info", "rimworld/get_cell_info", new
            {
                x = zoneRects.Value.Second.X,
                z = zoneRects.Value.Second.Z
            }, cancellationToken);
            context.EnsureSucceeded(postCleanupCellInfo, $"Reading post-cleanup zone cell info for ({zoneRects.Value.Second.X}, {zoneRects.Value.Second.Z})");
            if (JsonNodeHelpers.ReadObject(postCleanupCellInfo.StructuredContent, "cell", "zone") != null)
                throw new InvalidOperationException("Expected the expanded zone cell to report no zone after cleanup.");
            context.SetScenarioData("postCleanupZoneCellInfo", postCleanupCellInfo.StructuredContent);
        }

        if (allowedAreaRect.HasValue)
        {
            var postCleanupAllowedAreaCellInfo = await context.CallGameToolAsync("architect_state.get_cleanup_area_cell_info", "rimworld/get_cell_info", new
            {
                x = allowedAreaRect.Value.X,
                z = allowedAreaRect.Value.Z
            }, cancellationToken);
            context.EnsureSucceeded(postCleanupAllowedAreaCellInfo, $"Reading post-cleanup allowed-area cell info for ({allowedAreaRect.Value.X}, {allowedAreaRect.Value.Z})");
            if (!string.IsNullOrWhiteSpace(createdAllowedAreaId) && CellContainsAreaId(postCleanupAllowedAreaCellInfo.StructuredContent, createdAllowedAreaId))
                throw new InvalidOperationException("Expected the created allowed area to no longer cover the cleanup cell.");
            context.SetScenarioData("postCleanupAllowedAreaCellInfo", postCleanupAllowedAreaCellInfo.StructuredContent);
        }

        context.SetScenarioData("areasAfterCleanup", areasAfterCleanup.StructuredContent);
        context.SetScenarioData("zonesAfterCleanup", zonesAfterCleanup.StructuredContent);

        var observation = await observationWindow.CaptureAsync(
            "architect_state.final_bridge_status",
            "architect_state.collect_operation_events",
            "architect_state.collect_logs",
            cancellationToken);
        context.ApplyObservationWindow(observation);
    }

    private static string ResolveFirstColonistName(JsonNode? structuredContent)
    {
        return ReadRequiredString(ResolveFirstColonist(structuredContent), "name");
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

    private static JsonNode? ResolveFirstColonist(JsonNode? structuredContent)
    {
        var colonists = JsonNodeHelpers.ReadArray(structuredContent, "colonists");
        foreach (var colonist in colonists)
        {
            if (!string.IsNullOrWhiteSpace(JsonNodeHelpers.ReadString(colonist, "pawnId")))
                return colonist;
        }

        throw new InvalidOperationException("The current map did not expose any colonists that could be used for the smoke scenario.");
    }

    private static bool ResolveColonistDrafted(JsonNode? structuredContent, string pawnId)
    {
        var colonists = JsonNodeHelpers.ReadArray(structuredContent, "colonists");
        foreach (var colonist in colonists)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(colonist, "pawnId"), pawnId, StringComparison.Ordinal))
                return JsonNodeHelpers.ReadBoolean(colonist, "drafted") == true;
        }

        throw new InvalidOperationException($"Could not find colonist '{pawnId}' in the colonist list response.");
    }

    private static bool ReadRequiredSemanticBooleanField(JsonNode? structuredContent, string fieldName)
    {
        var field = FindSemanticField(structuredContent, fieldName);
        var value = JsonNodeHelpers.ReadBoolean(field, "value");
        if (!value.HasValue)
            throw new InvalidOperationException($"Semantic field '{fieldName}' did not expose a boolean value.");

        return value.Value;
    }

    private static int ReadRequiredSemanticIntField(JsonNode? structuredContent, string fieldName)
    {
        var field = FindSemanticField(structuredContent, fieldName);
        var value = JsonNodeHelpers.ReadInt32(field, "value");
        if (!value.HasValue)
            throw new InvalidOperationException($"Semantic field '{fieldName}' did not expose an integer value.");

        return value.Value;
    }

    private static JsonNode? FindSemanticField(JsonNode? structuredContent, string fieldName)
    {
        var fields = JsonNodeHelpers.ReadArray(structuredContent, "root", "children");
        foreach (var field in fields)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(field, "name"), fieldName, StringComparison.Ordinal))
                return field;
        }

        throw new InvalidOperationException($"Could not find semantic field '{fieldName}' in the mod settings payload.");
    }

    private static JsonNode? ResolveDraftGizmo(IReadOnlyList<JsonNode?> gizmos)
    {
        foreach (var gizmo in gizmos)
        {
            if (JsonNodeHelpers.ReadBoolean(gizmo, "disabled") == true)
                continue;

            var label = JsonNodeHelpers.ReadString(gizmo, "label");
            var description = JsonNodeHelpers.ReadString(gizmo, "description");
            if (label.IndexOf("draft", StringComparison.OrdinalIgnoreCase) >= 0
                || description.IndexOf("draft", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return gizmo;
            }
        }

        throw new InvalidOperationException("Could not find an enabled draft gizmo for the selected colonist.");
    }

    private static JsonNode? ResolvePreferredActiveNonCoreMod(IReadOnlyList<JsonNode?> mods, string preferredPackageId)
    {
        foreach (var mod in mods)
        {
            if (JsonNodeHelpers.ReadBoolean(mod, "enabled") != true)
                continue;
            if (JsonNodeHelpers.ReadBoolean(mod, "isCoreMod") == true)
                continue;
            if (string.Equals(JsonNodeHelpers.ReadString(mod, "packageId"), preferredPackageId, StringComparison.OrdinalIgnoreCase))
                return mod;
        }

        foreach (var mod in mods)
        {
            if (JsonNodeHelpers.ReadBoolean(mod, "enabled") != true)
                continue;
            if (JsonNodeHelpers.ReadBoolean(mod, "isCoreMod") == true)
                continue;

            return mod;
        }

        throw new InvalidOperationException("Could not find an active non-core mod to use for the mod-configuration roundtrip.");
    }

    private static JsonNode? ResolveModById(IReadOnlyList<JsonNode?> mods, string modId)
    {
        foreach (var mod in mods)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(mod, "modId"), modId, StringComparison.Ordinal))
                return mod;
        }

        throw new InvalidOperationException($"Could not find mod '{modId}' in the returned mod list response.");
    }

    private static HashSet<string> ReadIdSet(ToolInvocationResult result, string arrayPropertyName)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in JsonNodeHelpers.ReadArray(result.StructuredContent, arrayPropertyName))
        {
            var id = JsonNodeHelpers.ReadString(node, "id");
            if (!string.IsNullOrWhiteSpace(id))
                set.Add(id);
        }

        return set;
    }

    private static JsonNode? ResolveNewNodeById(
        JsonNode? structuredContent,
        string arrayPropertyName,
        ISet<string> baselineIds,
        Func<JsonNode?, bool> predicate)
    {
        foreach (var node in JsonNodeHelpers.ReadArray(structuredContent, arrayPropertyName))
        {
            var id = JsonNodeHelpers.ReadString(node, "id");
            if (string.IsNullOrWhiteSpace(id) || baselineIds.Contains(id))
                continue;
            if (predicate(node))
                return node;
        }

        throw new InvalidOperationException($"Could not find a new '{arrayPropertyName}' entry that matched the smoke-test predicate.");
    }

    private static string ReadRequiredString(JsonNode? node, params string[] path)
    {
        var value = JsonNodeHelpers.ReadString(node, path);
        if (string.IsNullOrWhiteSpace(value))
        {
            var location = path.Length == 0 ? "<root>" : string.Join(".", path);
            throw new InvalidOperationException($"Expected a non-empty string at '{location}' in the smoke scenario response payload.");
        }

        return value;
    }

    private static bool HasCompletedOperationStatus(JsonNode? node, params string[] path)
    {
        return JsonNodeHelpers.ReadInt32(node, path) == 2
            || string.Equals(JsonNodeHelpers.ReadString(node, path), "Completed", StringComparison.Ordinal);
    }

    private static (int X, int Z) ResolveColonistSearchOrigin(JsonNode? structuredContent)
    {
        var colonists = JsonNodeHelpers.ReadArray(structuredContent, "colonists");
        var positions = colonists
            .Select(colonist =>
            {
                var x = JsonNodeHelpers.ReadInt32(colonist, "position", "x");
                var z = JsonNodeHelpers.ReadInt32(colonist, "position", "z");
                return x.HasValue && z.HasValue ? (x.Value, z.Value) : ((int X, int Z)?)null;
            })
            .Where(position => position.HasValue)
            .Select(position => position!.Value)
            .ToList();

        if (positions.Count == 0)
            throw new InvalidOperationException("Could not resolve any colonist positions for architect placement probing.");

        return (
            (int)Math.Round(positions.Average(position => position.X)),
            (int)Math.Round(positions.Average(position => position.Z)));
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

    private static IEnumerable<(int X, int Z)> BuildArchitectProbeCells((int X, int Z) origin, int minRadius = 6, int maxRadius = 18)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var radius = minRadius; radius <= maxRadius; radius += 2)
        {
            for (var x = origin.X - radius; x <= origin.X + radius; x++)
            {
                if (seen.Add(CellKey(x, origin.Z - radius)))
                    yield return (x, origin.Z - radius);
                if (seen.Add(CellKey(x, origin.Z + radius)))
                    yield return (x, origin.Z + radius);
            }

            for (var z = origin.Z - radius + 1; z <= origin.Z + radius - 1; z++)
            {
                if (seen.Add(CellKey(origin.X - radius, z)))
                    yield return (origin.X - radius, z);
                if (seen.Add(CellKey(origin.X + radius, z)))
                    yield return (origin.X + radius, z);
            }
        }
    }

    private static bool SaveListContains(JsonNode? structuredContent, string saveName)
    {
        var saves = JsonNodeHelpers.ReadArray(structuredContent, "saves");
        return saves.Any(save => string.Equals(JsonNodeHelpers.ReadString(save, "name"), saveName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveArchitectCategoryId(IReadOnlyList<JsonNode?> categories, string preferredDefName)
    {
        foreach (var category in categories)
        {
            var defName = JsonNodeHelpers.ReadString(category, "categoryDefName");
            if (string.Equals(defName, preferredDefName, StringComparison.Ordinal))
                return JsonNodeHelpers.ReadString(category, "id");
        }

        throw new InvalidOperationException($"Could not find Architect category '{preferredDefName}'.");
    }

    private static JsonNode? ResolveMainTabNode(IReadOnlyList<JsonNode?> tabs, string preferredDefName, string preferredWindowType)
    {
        foreach (var tab in tabs)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(tab, "defName"), preferredDefName, StringComparison.Ordinal)
                && string.Equals(JsonNodeHelpers.ReadString(tab, "type"), preferredWindowType, StringComparison.Ordinal))
            {
                return tab;
            }
        }

        foreach (var tab in tabs)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(tab, "defName"), preferredDefName, StringComparison.Ordinal))
                return tab;
        }

        foreach (var tab in tabs)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(tab, "type"), preferredWindowType, StringComparison.Ordinal))
                return tab;
        }

        throw new InvalidOperationException($"Could not find the expected main tab '{preferredDefName}' / '{preferredWindowType}'.");
    }

    private static JsonNode? ResolveUiLayoutSurface(JsonNode? structuredContent, string surfaceTargetId)
    {
        var surfaces = JsonNodeHelpers.ReadArray(structuredContent, "surfaces");
        foreach (var surface in surfaces)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(surface, "surfaceTargetId"), surfaceTargetId, StringComparison.Ordinal))
                return surface;
        }

        throw new InvalidOperationException($"Could not find UI layout surface '{surfaceTargetId}'.");
    }

    private static JsonNode? ResolveUiLayoutElement(JsonNode? surface, string kind)
    {
        var elements = JsonNodeHelpers.ReadArray(surface, "elements");
        foreach (var element in elements)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(element, "kind"), kind, StringComparison.Ordinal))
                return element;
        }

        throw new InvalidOperationException($"Could not find a UI layout element of kind '{kind}'.");
    }

    private static string ResolveArchitectBuildDesignatorId(IReadOnlyList<JsonNode?> designators, string buildableDefName, bool requireDropdownChild = false)
    {
        foreach (var designator in designators)
        {
            var defName = JsonNodeHelpers.ReadString(designator, "buildableDefName");
            var parentId = JsonNodeHelpers.ReadString(designator, "parentId");
            if (string.Equals(defName, buildableDefName, StringComparison.Ordinal))
            {
                if (requireDropdownChild && string.IsNullOrWhiteSpace(parentId))
                    continue;

                return JsonNodeHelpers.ReadString(designator, "id");
            }
        }

        throw new InvalidOperationException($"Could not find Architect build designator '{buildableDefName}'.");
    }

    private static string ResolveArchitectParentId(IReadOnlyList<JsonNode?> designators, string designatorId)
    {
        foreach (var designator in designators)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(designator, "id"), designatorId, StringComparison.Ordinal))
                return JsonNodeHelpers.ReadString(designator, "parentId");
        }

        throw new InvalidOperationException($"Could not find Architect parent id for designator '{designatorId}'.");
    }

    private static string ResolveArchitectDesignatorIdByClassName(IReadOnlyList<JsonNode?> designators, string className)
    {
        foreach (var designator in designators)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(designator, "className"), className, StringComparison.Ordinal))
                return JsonNodeHelpers.ReadString(designator, "id");
        }

        throw new InvalidOperationException($"Could not find Architect designator with class name '{className}'.");
    }

    private static async Task EnsureGodModeAsync(SmokeScenarioContext context, string stepName, bool enabled, CancellationToken cancellationToken)
    {
        var setGodMode = await context.CallGameToolAsync(stepName, "rimworld/set_god_mode", new
        {
            enabled
        }, cancellationToken);
        context.EnsureSucceeded(setGodMode, $"Setting god mode to '{enabled}'");

        if (JsonNodeHelpers.ReadBoolean(setGodMode.StructuredContent, "godMode") != enabled)
            throw new InvalidOperationException($"God mode did not reach the requested value '{enabled}'.");
    }

    private static JsonObject BuildArchitectApplyArguments(string designatorId, int x, int z, int width, int height)
    {
        return new JsonObject
        {
            ["designatorId"] = designatorId,
            ["x"] = x,
            ["z"] = z,
            ["width"] = width,
            ["height"] = height,
            ["dryRun"] = false,
            ["keepSelected"] = true
        };
    }

    private static string GetLuaFixturePath(string fileName)
    {
        var fullPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Lua", fileName));
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"Lua fixture '{fullPath}' could not be found.");

        return fullPath;
    }

    private static async Task<(int X, int Z)> FindAcceptedArchitectPlacementCellAsync(
        SmokeScenarioContext context,
        string stepPrefix,
        string designatorId,
        JsonNode? colonistsStructuredContent,
        HashSet<string> blockedCells,
        CancellationToken cancellationToken,
        int width = 1,
        int height = 1,
        int minRadius = 6,
        int maxRadius = 18,
        Func<SmokeScenarioContext, int, int, int, int, CancellationToken, Task<bool>>? preflightAsync = null)
    {
        var origin = ResolveColonistSearchOrigin(colonistsStructuredContent);
        var attempt = 0;
        var fastCandidate = await context.CallGameToolAsync($"{stepPrefix}.fast_candidate", "rimworld/find_random_cell_near", new
        {
            x = origin.X,
            z = origin.Z,
            startingSearchRadius = minRadius,
            maxSearchRadius = maxRadius,
            width,
            height,
            footprintAnchor = "top_left",
            designatorId
        }, cancellationToken);
        if (fastCandidate.Success)
        {
            var candidateX = JsonNodeHelpers.ReadInt32(fastCandidate.StructuredContent, "cell", "x");
            var candidateZ = JsonNodeHelpers.ReadInt32(fastCandidate.StructuredContent, "cell", "z");
            if (candidateX.HasValue && candidateZ.HasValue)
            {
                var fastCellKey = CellKey(candidateX.Value, candidateZ.Value);
                if (blockedCells.Add(fastCellKey)
                    && (preflightAsync == null || await preflightAsync(context, candidateX.Value, candidateZ.Value, width, height, cancellationToken)))
                {
                    return (candidateX.Value, candidateZ.Value);
                }
            }
        }

        foreach (var candidateCell in BuildArchitectProbeCells(origin, minRadius, maxRadius))
        {
            if (!blockedCells.Add(CellKey(candidateCell.X, candidateCell.Z)))
                continue;

            if (preflightAsync != null && !await preflightAsync(context, candidateCell.X, candidateCell.Z, width, height, cancellationToken))
                continue;

            attempt++;
            var dryRun = await context.CallGameToolAsync($"{stepPrefix}.dry_run_{attempt}", "rimworld/apply_architect_designator", new
            {
                designatorId,
                x = candidateCell.X,
                z = candidateCell.Z,
                width,
                height,
                dryRun = true,
                keepSelected = false
            }, cancellationToken);
            if (JsonNodeHelpers.ReadBoolean(dryRun.StructuredContent, "operation", "Success") != true)
                throw new InvalidOperationException($"Architect placement probe failed for cell ({candidateCell.X}, {candidateCell.Z}). {dryRun.Message}".Trim());

            if (JsonNodeHelpers.ReadInt32(dryRun.StructuredContent, "acceptedCellCount").GetValueOrDefault() < width * height)
                continue;

            return candidateCell;
        }

        throw new InvalidOperationException("Could not find an accepted architect placement cell for the wall designator.");
    }

    private static async Task<IReadOnlyList<ToolInvocationResult>> ReadRectangleCellInfosAsync(
        SmokeScenarioContext context,
        string stepPrefix,
        int x,
        int z,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var responses = new List<ToolInvocationResult>(width * height);
        foreach (var cell in EnumerateRectangleCells(x, z, width, height))
        {
            var response = await context.CallGameToolAsync($"{stepPrefix}_{cell.X}_{cell.Z}", "rimworld/get_cell_info", new
            {
                x = cell.X,
                z = cell.Z
            }, cancellationToken);
            context.EnsureSucceeded(response, $"Reading cell info for rectangle cell ({cell.X}, {cell.Z})");
            responses.Add(response);
        }

        return responses;
    }

    private static async Task<bool> RectangleHasNoZoneAsync(
        SmokeScenarioContext context,
        int x,
        int z,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        foreach (var cell in EnumerateRectangleCells(x, z, width, height))
        {
            var response = await context.CallGameToolAsync($"architect_zone.preflight_zone_{cell.X}_{cell.Z}", "rimworld/get_cell_info", new
            {
                x = cell.X,
                z = cell.Z
            }, cancellationToken);
            context.EnsureSucceeded(response, $"Reading preflight zone info for cell ({cell.X}, {cell.Z})");

            if (JsonNodeHelpers.ReadObject(response.StructuredContent, "cell", "zone") != null)
                return false;
        }

        return true;
    }

    private static async Task<bool> RectangleHasNoHomeAreaAsync(
        SmokeScenarioContext context,
        int x,
        int z,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        foreach (var cell in EnumerateRectangleCells(x, z, width, height))
        {
            var response = await context.CallGameToolAsync($"architect_zone.preflight_home_{cell.X}_{cell.Z}", "rimworld/get_cell_info", new
            {
                x = cell.X,
                z = cell.Z
            }, cancellationToken);
            context.EnsureSucceeded(response, $"Reading preflight home-area info for cell ({cell.X}, {cell.Z})");

            if (CellContainsArea(response.StructuredContent, "RimWorld.Area_Home"))
                return false;
        }

        return true;
    }

    private static async Task<bool> RectangleHasNoAreaAsync(
        SmokeScenarioContext context,
        int x,
        int z,
        int width,
        int height,
        string areaId,
        CancellationToken cancellationToken)
    {
        foreach (var cell in EnumerateRectangleCells(x, z, width, height))
        {
            var response = await context.CallGameToolAsync($"architect_state.preflight_area_{cell.X}_{cell.Z}", "rimworld/get_cell_info", new
            {
                x = cell.X,
                z = cell.Z
            }, cancellationToken);
            context.EnsureSucceeded(response, $"Reading preflight allowed-area info for cell ({cell.X}, {cell.Z})");

            if (CellContainsAreaId(response.StructuredContent, areaId))
                return false;
        }

        return true;
    }

    private static void AssertCellContainsBuildBlueprint(JsonNode? cellInfo, string buildDefName)
    {
        var blueprints = JsonNodeHelpers.ReadArray(cellInfo, "cell", "blueprintBuildDefs");
        if (!blueprints.Any(item => string.Equals(JsonNodeHelpers.ReadString(item), buildDefName, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Expected blueprint build def '{buildDefName}' was not present in the cell info response.");
    }

    private static void AssertCellDoesNotContainBuildBlueprint(JsonNode? cellInfo, string buildDefName)
    {
        var blueprints = JsonNodeHelpers.ReadArray(cellInfo, "cell", "blueprintBuildDefs");
        if (blueprints.Any(item => string.Equals(JsonNodeHelpers.ReadString(item), buildDefName, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Blueprint build def '{buildDefName}' was still present when the scenario expected a direct build.");
    }

    private static void AssertCellContainsSolidThing(JsonNode? cellInfo, string thingDefName)
    {
        var solidThings = JsonNodeHelpers.ReadArray(cellInfo, "cell", "solidThingDefs");
        if (!solidThings.Any(item => string.Equals(JsonNodeHelpers.ReadString(item), thingDefName, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Expected solid thing def '{thingDefName}' was not present in the cell info response.");
    }

    private static void AssertCellDoesNotContainSolidThing(JsonNode? cellInfo, string thingDefName)
    {
        var solidThings = JsonNodeHelpers.ReadArray(cellInfo, "cell", "solidThingDefs");
        if (solidThings.Any(item => string.Equals(JsonNodeHelpers.ReadString(item), thingDefName, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Solid thing def '{thingDefName}' was present when the scenario expected only a blueprint.");
    }

    private static void AssertCellContainsArea(JsonNode? cellInfo, string className)
    {
        if (!CellContainsArea(cellInfo, className))
            throw new InvalidOperationException($"Expected area '{className}' was not present in the cell info response.");
    }

    private static void AssertCellContainsAreaId(JsonNode? cellInfo, string areaId)
    {
        if (!CellContainsAreaId(cellInfo, areaId))
            throw new InvalidOperationException($"Expected area id '{areaId}' was not present in the cell info response.");
    }

    private static bool CellContainsArea(JsonNode? cellInfo, string className)
    {
        var areas = JsonNodeHelpers.ReadArray(cellInfo, "cell", "areas");
        return areas.Any(area => string.Equals(JsonNodeHelpers.ReadString(area, "className"), className, StringComparison.Ordinal));
    }

    private static bool CellContainsAreaId(JsonNode? cellInfo, string areaId)
    {
        var areas = JsonNodeHelpers.ReadArray(cellInfo, "cell", "areas");
        return areas.Any(area => string.Equals(JsonNodeHelpers.ReadString(area, "id"), areaId, StringComparison.Ordinal));
    }

    private static JsonNode? ResolveZoneById(IReadOnlyList<JsonNode?> zones, string zoneId)
    {
        foreach (var zone in zones)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(zone, "id"), zoneId, StringComparison.Ordinal))
                return zone;
        }

        throw new InvalidOperationException($"Could not find zone '{zoneId}' in the zone list response.");
    }

    private static JsonNode? ResolveAreaByClassName(IReadOnlyList<JsonNode?> areas, string className)
    {
        foreach (var area in areas)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(area, "className"), className, StringComparison.Ordinal))
                return area;
        }

        throw new InvalidOperationException($"Could not find area '{className}' in the area list response.");
    }

    private static JsonNode? ResolveAreaById(IReadOnlyList<JsonNode?> areas, string areaId)
    {
        foreach (var area in areas)
        {
            if (string.Equals(JsonNodeHelpers.ReadString(area, "id"), areaId, StringComparison.Ordinal))
                return area;
        }

        throw new InvalidOperationException($"Could not find area '{areaId}' in the area list response.");
    }

    private static string CellKey(int x, int z)
    {
        return x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + z.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IEnumerable<(int X, int Z)> EnumerateRectangleCells(int x, int z, int width, int height)
    {
        for (var offsetZ = 0; offsetZ < height; offsetZ++)
        {
            for (var offsetX = 0; offsetX < width; offsetX++)
                yield return (x + offsetX, z + offsetZ);
        }
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

    private static string ResolveDebugActionPath(IReadOnlyList<JsonNode?> children, string preferredPath)
    {
        foreach (var child in children)
        {
            var path = JsonNodeHelpers.ReadString(child, "path");
            if (string.Equals(path, preferredPath, StringComparison.Ordinal))
                return path;
        }

        var preferredLabel = preferredPath[(preferredPath.LastIndexOf('\\') + 1)..];
        foreach (var child in children)
        {
            var label = JsonNodeHelpers.ReadString(child, "label");
            if (string.Equals(label, preferredLabel, StringComparison.Ordinal))
                return JsonNodeHelpers.ReadString(child, "path");
        }

        throw new InvalidOperationException($"Could not resolve debug action '{preferredPath}' from the returned child nodes.");
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
