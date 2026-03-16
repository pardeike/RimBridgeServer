using System.Text.Json.Nodes;

namespace RimBridgeServer.LiveSmoke;

internal sealed class SmokeObservationWindowOptions
{
    public int EventLimit { get; init; } = 200;

    public int LogLimit { get; init; } = 200;

    public string MinimumLogLevel { get; init; } = "info";

    public bool IncludeDiagnosticEvents { get; init; }
}

internal sealed class SmokeObservationWindowResult
{
    public JsonNode? InitialState { get; init; }

    public JsonNode? FinalState { get; init; }

    public List<JsonNode?> OperationEvents { get; init; } = [];

    public List<JsonNode?> LogEntries { get; init; } = [];

    public long InitialOperationEventSequence { get; init; }

    public long InitialLogSequence { get; init; }

    public long? FinalOperationEventSequence { get; init; }

    public long? FinalLogSequence { get; init; }
}

internal sealed class SmokeObservationWindow
{
    private readonly SmokeScenarioContext _context;
    private readonly SmokeObservationWindowOptions _options;
    private readonly long _initialOperationEventSequence;
    private readonly long _initialLogSequence;
    private readonly JsonNode? _initialState;

    public SmokeObservationWindow(
        SmokeScenarioContext context,
        JsonNode? initialState,
        long initialOperationEventSequence,
        long initialLogSequence,
        SmokeObservationWindowOptions options)
    {
        _context = context;
        _initialState = JsonNodeHelpers.CloneNode(initialState);
        _initialOperationEventSequence = initialOperationEventSequence;
        _initialLogSequence = initialLogSequence;
        _options = options;
    }

    public async Task<SmokeObservationWindowResult> CaptureAsync(
        string finalStatusStepName,
        string eventStepName,
        string logStepName,
        CancellationToken cancellationToken)
    {
        var finalStatus = await _context.CallGameToolAsync(finalStatusStepName, "rimbridge/get_bridge_status", new { }, cancellationToken);
        _context.EnsureSucceeded(finalStatus, "Reading final bridge status");

        var eventWindow = await _context.CallGameToolAsync(eventStepName, "rimbridge/list_operation_events", new
        {
            afterSequence = _initialOperationEventSequence,
            limit = _options.EventLimit,
            includeDiagnostics = _options.IncludeDiagnosticEvents
        }, cancellationToken);
        _context.EnsureSucceeded(eventWindow, "Collecting operation events");

        var logWindow = await _context.CallGameToolAsync(logStepName, "rimbridge/list_logs", new
        {
            afterSequence = _initialLogSequence,
            minimumLevel = _options.MinimumLogLevel,
            limit = _options.LogLimit
        }, cancellationToken);
        _context.EnsureSucceeded(logWindow, "Collecting logs");

        return new SmokeObservationWindowResult
        {
            InitialState = JsonNodeHelpers.CloneNode(_initialState),
            FinalState = JsonNodeHelpers.CloneNode(JsonNodeHelpers.GetPath(finalStatus.StructuredContent, "state")),
            OperationEvents = JsonNodeHelpers.ReadArray(eventWindow.StructuredContent, "events"),
            LogEntries = JsonNodeHelpers.ReadArray(logWindow.StructuredContent, "logs"),
            InitialOperationEventSequence = _initialOperationEventSequence,
            InitialLogSequence = _initialLogSequence,
            FinalOperationEventSequence = JsonNodeHelpers.ReadInt64(finalStatus.StructuredContent, "latestOperationEventSequence"),
            FinalLogSequence = JsonNodeHelpers.ReadInt64(finalStatus.StructuredContent, "latestLogSequence")
        };
    }
}
