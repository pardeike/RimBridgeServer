using System.Text.Json.Nodes;

namespace RimBridgeServer.LiveSmoke;

internal sealed class SmokeScenarioContext
{
    private readonly CliOptions _options;
    private readonly McpStdioClient _client;
    private readonly SmokeRunReport _report;

    public SmokeScenarioContext(CliOptions options, McpStdioClient client, SmokeRunReport report)
    {
        _options = options;
        _client = client;
        _report = report;
    }

    public SmokeRunReport Report => _report;

    public bool StartedGameByHarness { get; set; }

    public async Task<ToolInvocationResult> CallServerToolAsync(string stepName, string toolName, object arguments, CancellationToken cancellationToken)
    {
        var result = await _client.CallToolAsync(toolName, arguments, cancellationToken);
        _report.Steps.Add(new SmokeStepReport
        {
            Name = stepName,
            ToolName = toolName,
            Success = result.Success,
            DurationMs = result.DurationMs,
            Message = result.Message,
            OperationId = JsonNodeHelpers.ReadString(result.StructuredContent, "operation", "OperationId"),
            Response = JsonNodeHelpers.CloneNode(result.StructuredContent)
        });

        return result;
    }

    public Task<ToolInvocationResult> CallGameToolAsync(string stepName, string toolName, object arguments, CancellationToken cancellationToken)
    {
        return CallServerToolAsync(
            stepName,
            "games.call_tool",
            new
            {
                gameId = _options.GameId,
                tool = toolName,
                arguments,
                timeout = _options.GameToolTimeoutSeconds
            },
            cancellationToken);
    }

    public void EnsureSucceeded(ToolInvocationResult result, string actionDescription)
    {
        if (!result.Success)
            throw new InvalidOperationException($"{actionDescription} failed. {result.Message}".Trim());
    }

    public async Task WaitForLongEventIdleAsync(string stepName, CancellationToken cancellationToken)
    {
        var wait = await CallGameToolAsync(stepName, "rimbridge/wait_for_long_event_idle", new
        {
            timeoutMs = _options.WaitTimeoutMs,
            pollIntervalMs = 100
        }, cancellationToken);
        EnsureSucceeded(wait, "Waiting for RimWorld to become idle");
    }

    public async Task WaitForGameLoadedAsync(string stepName, CancellationToken cancellationToken)
    {
        var wait = await CallGameToolAsync(stepName, "rimbridge/wait_for_game_loaded", new
        {
            timeoutMs = _options.WaitTimeoutMs,
            pollIntervalMs = 100
        }, cancellationToken);
        EnsureSucceeded(wait, "Waiting for RimWorld to load a playable game");
    }

    public async Task WaitForOperationAsync(string stepName, string operationId, CancellationToken cancellationToken)
    {
        var wait = await CallGameToolAsync(stepName, "rimbridge/wait_for_operation", new
        {
            operationId,
            timeoutMs = _options.WaitTimeoutMs,
            pollIntervalMs = 50
        }, cancellationToken);
        EnsureSucceeded(wait, $"Waiting for operation '{operationId}'");
    }

    public async Task EnsurePlayableGameAsync(CancellationToken cancellationToken)
    {
        var initialStatus = await CallGameToolAsync("ensure_playable.bridge_status", "rimbridge/get_bridge_status", new { }, cancellationToken);
        EnsureSucceeded(initialStatus, "Checking bridge status before playable-game precondition");
        if (IsPlayable(initialStatus.StructuredContent))
        {
            Note("Reused an already loaded playable game.");
            return;
        }

        await WaitForLongEventIdleAsync("ensure_playable.wait_for_long_event_idle", cancellationToken);

        var idleStatus = await CallGameToolAsync("ensure_playable.bridge_status_after_idle", "rimbridge/get_bridge_status", new { }, cancellationToken);
        EnsureSucceeded(idleStatus, "Checking bridge status after waiting for idle");
        if (IsPlayable(idleStatus.StructuredContent))
        {
            Note("The game reached a playable state while satisfying the precondition.");
            return;
        }

        if (!IsEntryState(idleStatus.StructuredContent))
            throw new InvalidOperationException("The live smoke precondition requires either a loaded game or the RimWorld main menu entry scene so a debug game can be started safely.");

        var startDebugGame = await CallGameToolAsync("ensure_playable.start_debug_game", "rimworld/start_debug_game", new { }, cancellationToken);
        EnsureSucceeded(startDebugGame, "Starting RimWorld debug game for the playable-game precondition");

        var operationId = RequireOperationId(startDebugGame, "Starting RimWorld debug game for the playable-game precondition");
        await WaitForOperationAsync("ensure_playable.wait_for_operation", operationId, cancellationToken);
        await WaitForGameLoadedAsync("ensure_playable.wait_for_game_loaded", cancellationToken);

        Note("Created a RimWorld debug quick-test colony to satisfy the playable-game precondition.");
    }

    public async Task<SmokeObservationWindow> BeginObservationWindowAsync(
        string stepName,
        CancellationToken cancellationToken,
        SmokeObservationWindowOptions? options = null)
    {
        var baselineStatus = await CallGameToolAsync(stepName, "rimbridge/get_bridge_status", new { }, cancellationToken);
        EnsureSucceeded(baselineStatus, "Reading initial bridge status");

        var baselineLogSequence = JsonNodeHelpers.ReadInt64(baselineStatus.StructuredContent, "latestLogSequence");
        var baselineEventSequence = JsonNodeHelpers.ReadInt64(baselineStatus.StructuredContent, "latestOperationEventSequence");
        if (baselineLogSequence is null || baselineEventSequence is null)
            throw new InvalidOperationException("Bridge status did not include the expected log and operation event cursors.");

        return new SmokeObservationWindow(
            this,
            JsonNodeHelpers.GetPath(baselineStatus.StructuredContent, "state"),
            baselineEventSequence.Value,
            baselineLogSequence.Value,
            options ?? new SmokeObservationWindowOptions());
    }

    public void ApplyObservationWindow(SmokeObservationWindowResult result)
    {
        _report.InitialBridgeState = JsonNodeHelpers.CloneNode(result.InitialState);
        _report.FinalBridgeState = JsonNodeHelpers.CloneNode(result.FinalState);
        _report.OperationEvents = result.OperationEvents.Select(JsonNodeHelpers.CloneNode).ToList();
        _report.LogEntries = result.LogEntries.Select(JsonNodeHelpers.CloneNode).ToList();
    }

    public string RequireOperationId(ToolInvocationResult result, string actionDescription)
    {
        var operationId = JsonNodeHelpers.ReadString(result.StructuredContent, "operation", "OperationId");
        if (string.IsNullOrWhiteSpace(operationId))
            throw new InvalidOperationException($"{actionDescription} did not return operation metadata.");

        return operationId;
    }

    public void Note(string message)
    {
        _report.Notes.Add(message);
    }

    public void SetSummaryValue(string key, string value)
    {
        _report.SummaryValues[key] = value;
    }

    public void SetScenarioData(string key, JsonNode? value)
    {
        _report.ScenarioData[key] = JsonNodeHelpers.CloneNode(value);
    }

    public IReadOnlyList<string> GetGabsStderrTail()
    {
        return _client.GetStderrTail();
    }

    private static bool IsPlayable(JsonNode? structuredContent)
    {
        return JsonNodeHelpers.ReadBoolean(structuredContent, "state", "hasCurrentGame") == true
            && string.Equals(JsonNodeHelpers.ReadString(structuredContent, "state", "programState"), "Playing", StringComparison.OrdinalIgnoreCase)
            && JsonNodeHelpers.ReadBoolean(structuredContent, "state", "longEventPending") == false;
    }

    private static bool IsEntryState(JsonNode? structuredContent)
    {
        return JsonNodeHelpers.ReadBoolean(structuredContent, "state", "hasCurrentGame") == false
            && string.Equals(JsonNodeHelpers.ReadString(structuredContent, "state", "programState"), "Entry", StringComparison.OrdinalIgnoreCase)
            && JsonNodeHelpers.ReadBoolean(structuredContent, "state", "inEntryScene") == true;
    }
}
