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
    public const string SelectionRoundTripScenarioName = "selection-roundtrip";

    private static readonly IReadOnlyDictionary<string, SmokeScenarioDefinition> Definitions =
        new Dictionary<string, SmokeScenarioDefinition>(StringComparer.Ordinal)
        {
            [DebugGameLoadScenarioName] = new SmokeScenarioDefinition
            {
                Name = DebugGameLoadScenarioName,
                Description = "Start RimWorld's debug colony from the main menu and capture the resulting operation/log window.",
                RunAsync = RunDebugGameLoadAsync
            },
            [SelectionRoundTripScenarioName] = new SmokeScenarioDefinition
            {
                Name = SelectionRoundTripScenarioName,
                Description = "Ensure a playable game exists, then exercise selection and camera tools around a real colonist.",
                RunAsync = RunSelectionRoundTripAsync
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
}
