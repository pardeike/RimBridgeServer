using System;
using System.Collections.Generic;

namespace RimBridgeServer.Contracts;

public sealed class CapabilityScriptDefinition
{
    public string Name { get; set; } = string.Empty;

    public bool ContinueOnError { get; set; }

    public List<CapabilityScriptStep> Steps { get; set; } = [];
}

public sealed class CapabilityScriptStep
{
    public string Id { get; set; } = string.Empty;

    public string Call { get; set; } = string.Empty;

    public Dictionary<string, object> Arguments { get; set; } = [];
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

    public string HaltReason { get; set; } = string.Empty;

    public int StepCount { get; set; }

    public int ExecutedStepCount { get; set; }

    public int SucceededStepCount { get; set; }

    public int FailedStepCount { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public long DurationMs { get; set; }

    public List<CapabilityScriptStepReport> Steps { get; set; } = [];
}
