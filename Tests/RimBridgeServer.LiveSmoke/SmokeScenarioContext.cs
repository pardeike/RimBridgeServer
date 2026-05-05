using System.Linq;
using System.Text.Json.Nodes;
using System.Text;

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

    public bool HumanVerificationEnabled => _options.HumanVerify;

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
            pollIntervalMs = 50,
            readiness = "visual",
            pauseIfNeeded = true
        }, cancellationToken);
        EnsureSucceeded(wait, "Waiting for RimWorld to load an automation-ready game");
    }

    public async Task LoadGameReadyAsync(string stepName, string saveName, CancellationToken cancellationToken)
    {
        var load = await CallGameToolAsync(stepName, "rimworld/load_game_ready", new
        {
            saveName,
            timeoutMs = _options.WaitTimeoutMs,
            pollIntervalMs = 50,
            readiness = "visual",
            pauseIfNeeded = true
        }, cancellationToken);
        EnsureSucceeded(load, $"Loading RimWorld save '{saveName}' and waiting for automation readiness");
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
        if (IsAutomationReady(initialStatus.StructuredContent))
        {
            Note("Reused an already loaded automation-ready game.");
            return;
        }

        if (IsPlayable(initialStatus.StructuredContent))
        {
            await WaitForGameLoadedAsync("ensure_playable.wait_for_game_loaded_existing", cancellationToken);
            Note("Reused a loaded game after waiting for screen fade to finish and pausing it for automation.");
            return;
        }

        await WaitForLongEventIdleAsync("ensure_playable.wait_for_long_event_idle", cancellationToken);

        var idleStatus = await CallGameToolAsync("ensure_playable.bridge_status_after_idle", "rimbridge/get_bridge_status", new { }, cancellationToken);
        EnsureSucceeded(idleStatus, "Checking bridge status after waiting for idle");
        if (IsAutomationReady(idleStatus.StructuredContent))
        {
            Note("The game was already automation-ready while satisfying the precondition.");
            return;
        }

        if (IsPlayable(idleStatus.StructuredContent))
        {
            await WaitForGameLoadedAsync("ensure_playable.wait_for_game_loaded_after_idle", cancellationToken);
            Note("The game reached a playable state while satisfying the precondition, then was paused after screen fade completed.");
            return;
        }

        if (!IsEntryState(idleStatus.StructuredContent))
            throw new InvalidOperationException("The live smoke precondition requires either a loaded game or the RimWorld main menu entry scene so a debug game can be started safely.");

        ToolInvocationResult? startDebugGame = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            startDebugGame = await CallGameToolAsync($"ensure_playable.start_debug_game_{attempt}", "rimworld/start_debug_game", new { }, cancellationToken);
            if (startDebugGame.Success)
                break;

            if (startDebugGame.Message.IndexOf("busy with another long event", StringComparison.OrdinalIgnoreCase) < 0)
                break;

            Note($"Retrying debug-game precondition after a transient long-event race (attempt {attempt}).");
            await WaitForLongEventIdleAsync($"ensure_playable.wait_for_long_event_idle_retry_{attempt}", cancellationToken);
        }

        EnsureSucceeded(startDebugGame ?? throw new InvalidOperationException("The playable-game precondition did not return a start-debug-game result."), "Starting RimWorld debug game for the playable-game precondition");

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

    public async Task CaptureHumanVerificationScreenshotAsync(
        string stepName,
        string label,
        string description,
        IEnumerable<string> expectationLines,
        CancellationToken cancellationToken)
    {
        if (!HumanVerificationEnabled)
            return;

        var fileStem = BuildHumanVerificationFileStem(label);
        var screenshot = await CallGameToolAsync(stepName, "rimworld/take_screenshot", new
        {
            fileName = fileStem,
            includeTargets = true
        }, cancellationToken);
        EnsureSucceeded(screenshot, $"Capturing human verification screenshot '{label}'");

        var sourceImagePath = JsonNodeHelpers.ReadString(screenshot.StructuredContent, "path");
        await ExportHumanVerificationArtifactAsync(label, sourceImagePath, description, expectationLines, cancellationToken);
    }

    public async Task ExportHumanVerificationArtifactAsync(
        string label,
        string sourceImagePath,
        string description,
        IEnumerable<string> expectationLines,
        CancellationToken cancellationToken)
    {
        if (!HumanVerificationEnabled)
            return;

        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
            throw new InvalidOperationException($"Human verification artifact '{label}' did not produce a valid image file.");

        var fileStem = BuildHumanVerificationFileStem(label);
        var extension = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".png";

        var destinationImagePath = Path.Combine(_options.HumanVerifyDirectory, fileStem + extension);
        File.Copy(sourceImagePath, destinationImagePath, overwrite: true);

        var destinationDescriptionPath = Path.Combine(_options.HumanVerifyDirectory, fileStem + ".txt");
        var descriptionText = BuildHumanVerificationDescription(label, description, expectationLines, sourceImagePath);
        await File.WriteAllTextAsync(destinationDescriptionPath, descriptionText, cancellationToken);

        _report.HumanVerificationArtifacts.Add(new HumanVerificationArtifact
        {
            Label = label,
            ImagePath = destinationImagePath,
            DescriptionPath = destinationDescriptionPath,
            Description = description
        });

        Note($"Wrote human verification artifact '{label}' to '{destinationImagePath}'.");
    }

    public IReadOnlyList<string> GetGabsStderrTail()
    {
        return _client.GetStderrTail();
    }

    private string BuildHumanVerificationFileStem(string label)
    {
        var safeLabel = SanitizeFileStem(label);
        return $"rimbridge_verify_{_report.StartedAtUtc:yyyyMMdd_HHmmss}_{_report.Scenario}_{safeLabel}";
    }

    private string BuildHumanVerificationDescription(
        string label,
        string description,
        IEnumerable<string> expectationLines,
        string sourceImagePath)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Scenario: {_report.Scenario}");
        builder.AppendLine($"Artifact: {label}");
        builder.AppendLine($"Captured: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine();
        builder.AppendLine("What this image is for:");
        builder.AppendLine(description);
        builder.AppendLine();
        builder.AppendLine("What you should see and expect:");
        foreach (var line in expectationLines.Where(line => string.IsNullOrWhiteSpace(line) == false))
            builder.AppendLine("- " + line);
        builder.AppendLine("- RimWorld's upper-left screenshot-taken toast should not be visible because the harness suppresses it during automated captures.");
        builder.AppendLine();
        builder.AppendLine("Technical context:");
        builder.AppendLine("- Source image: " + sourceImagePath);
        builder.AppendLine("- JSON report: " + (string.IsNullOrWhiteSpace(_report.ReportPath) ? "saved to the live-smoke report directory after the run completes" : _report.ReportPath));
        return builder.ToString();
    }

    private static string SanitizeFileStem(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "artifact";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        }

        return builder.ToString().Trim('_');
    }

    private static bool IsPlayable(JsonNode? structuredContent)
    {
        return JsonNodeHelpers.ReadBoolean(structuredContent, "state", "playable") == true
            || (
                JsonNodeHelpers.ReadBoolean(structuredContent, "state", "hasCurrentGame") == true
                && string.Equals(JsonNodeHelpers.ReadString(structuredContent, "state", "programState"), "Playing", StringComparison.OrdinalIgnoreCase)
                && JsonNodeHelpers.ReadBoolean(structuredContent, "state", "longEventPending") == false
            );
    }

    private static bool IsAutomationReady(JsonNode? structuredContent)
    {
        return JsonNodeHelpers.ReadBoolean(structuredContent, "state", "automationReady") == true;
    }

    private static bool IsEntryState(JsonNode? structuredContent)
    {
        return JsonNodeHelpers.ReadBoolean(structuredContent, "state", "hasCurrentGame") == false
            && string.Equals(JsonNodeHelpers.ReadString(structuredContent, "state", "programState"), "Entry", StringComparison.OrdinalIgnoreCase)
            && JsonNodeHelpers.ReadBoolean(structuredContent, "state", "inEntryScene") == true;
    }
}
