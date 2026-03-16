using System;
using System.Collections.Generic;

namespace RimBridgeServer.Contracts;

public sealed class CapabilityScriptDefinition
{
    public string Name { get; set; } = string.Empty;

    public bool ContinueOnError { get; set; }

    public int MaxDurationMs { get; set; } = 60000;

    public int MaxExecutedStatements { get; set; } = 1000;

    public int MaxControlDepth { get; set; } = 32;

    public List<CapabilityScriptStep> Steps { get; set; } = [];
}

public sealed class CapabilityScriptStep
{
    public string Type { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Call { get; set; } = string.Empty;

    public Dictionary<string, object> Arguments { get; set; } = [];

    public CapabilityScriptContinuePolicy ContinueUntil { get; set; }

    public string Name { get; set; } = string.Empty;

    public object Value { get; set; }

    public Dictionary<string, object> Condition { get; set; } = [];

    public List<CapabilityScriptStep> Body { get; set; } = [];

    public List<CapabilityScriptStep> ElseBody { get; set; } = [];

    public object Collection { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string IndexName { get; set; } = string.Empty;

    public int MaxIterations { get; set; } = 100;
}

public sealed class CapabilityScriptOutputEntry
{
    public int Index { get; set; }

    public string StatementId { get; set; } = string.Empty;

    public string Level { get; set; } = "info";

    public string Message { get; set; } = string.Empty;

    public object Value { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }
}

public sealed class CapabilityScriptContinuePolicy
{
    public int TimeoutMs { get; set; } = 10000;

    public int PollIntervalMs { get; set; } = 100;

    public string TimeoutMessage { get; set; } = string.Empty;

    public Dictionary<string, object> Condition { get; set; } = [];
}

public sealed class CapabilityScriptStepReport
{
    public int Index { get; set; }

    public string Id { get; set; } = string.Empty;

    public string Call { get; set; } = string.Empty;

    public string CapabilityId { get; set; } = string.Empty;

    public string OperationId { get; set; } = string.Empty;

    public OperationStatus Status { get; set; }

    public bool Success { get; set; }

    public int Attempts { get; set; } = 1;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public long? DurationMs { get; set; }

    public object Result { get; set; }

    public OperationError Error { get; set; }

    public List<OperationWarning> Warnings { get; set; } = [];
}

public sealed class CapabilityScriptReport
{
    public string Name { get; set; } = string.Empty;

    public bool ContinueOnError { get; set; }

    public bool Success { get; set; }

    public bool Halted { get; set; }

    public bool Returned { get; set; }

    public string HaltReason { get; set; } = string.Empty;

    public object Result { get; set; }

    public OperationError Error { get; set; }

    public int StepCount { get; set; }

    public int ExecutedStepCount { get; set; }

    public int SucceededStepCount { get; set; }

    public int FailedStepCount { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public long DurationMs { get; set; }

    public List<CapabilityScriptOutputEntry> Output { get; set; } = [];

    public List<CapabilityScriptStepReport> Steps { get; set; } = [];
}
