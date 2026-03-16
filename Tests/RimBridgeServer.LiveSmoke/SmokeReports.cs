using System.Text.Json.Nodes;

namespace RimBridgeServer.LiveSmoke;

internal sealed class ToolInvocationResult
{
    public required string ToolName { get; init; }

    public required bool Success { get; set; }

    public required bool IsError { get; set; }

    public required long DurationMs { get; init; }

    public required string Text { get; init; }

    public required string Message { get; set; }

    public JsonNode? StructuredContent { get; init; }
}

internal sealed class SmokeStepReport
{
    public required string Name { get; init; }

    public required string ToolName { get; init; }

    public required bool Success { get; init; }

    public required long DurationMs { get; init; }

    public required string Message { get; init; }

    public string OperationId { get; init; } = string.Empty;

    public JsonNode? Response { get; init; }
}

internal sealed class SmokeRunReport
{
    public required string Scenario { get; init; }

    public string ScenarioDescription { get; set; } = string.Empty;

    public required string GameId { get; init; }

    public required string GabsBinaryPath { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public bool Success { get; set; }

    public long DurationMs { get; set; }

    public string FailureMessage { get; set; } = string.Empty;

    public string Exception { get; set; } = string.Empty;

    public string ReportPath { get; set; } = string.Empty;

    public List<SmokeStepReport> Steps { get; set; } = [];

    public List<string> Notes { get; set; } = [];

    public List<string> GabsStderrTail { get; set; } = [];

    public JsonNode? InitialBridgeState { get; set; }

    public JsonNode? FinalBridgeState { get; set; }

    public int? ColonistCount { get; set; }

    public Dictionary<string, string> SummaryValues { get; set; } = new(StringComparer.Ordinal);

    public List<HumanVerificationArtifact> HumanVerificationArtifacts { get; set; } = [];

    public Dictionary<string, JsonNode?> ScenarioData { get; set; } = new(StringComparer.Ordinal);

    public List<JsonNode?> OperationEvents { get; set; } = [];

    public List<JsonNode?> LogEntries { get; set; } = [];
}

internal sealed class HumanVerificationArtifact
{
    public string Label { get; set; } = string.Empty;

    public string ImagePath { get; set; } = string.Empty;

    public string DescriptionPath { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
