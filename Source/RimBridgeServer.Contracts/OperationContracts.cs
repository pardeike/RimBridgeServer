using System;
using System.Collections.Generic;

namespace RimBridgeServer.Contracts;

public enum OperationStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    TimedOut,
    Cancelled
}

public sealed class OperationWarning
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public object Details { get; set; }
}

public sealed class OperationError
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string ExceptionType { get; set; } = string.Empty;

    public object Details { get; set; }
}

public sealed class OperationEnvelope
{
    public string OperationId { get; set; } = string.Empty;

    public string CapabilityId { get; set; } = string.Empty;

    public OperationStatus Status { get; set; }

    public bool Success { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public long? DurationMs { get; set; }

    public object Result { get; set; }

    public List<OperationWarning> Warnings { get; set; } = [];

    public OperationError Error { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = [];

    public OperationEnvelope WithoutResult()
    {
        return new OperationEnvelope
        {
            OperationId = OperationId,
            CapabilityId = CapabilityId,
            Status = Status,
            Success = Success,
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            DurationMs = DurationMs,
            Result = null,
            Error = Error,
            Warnings = new List<OperationWarning>(Warnings),
            Metadata = new Dictionary<string, object>(Metadata)
        };
    }

    public static OperationEnvelope Completed(string operationId, string capabilityId, DateTimeOffset startedAtUtc, object result)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        return new OperationEnvelope
        {
            OperationId = operationId,
            CapabilityId = capabilityId,
            Status = OperationStatus.Completed,
            Success = true,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMs = (long)(completedAtUtc - startedAtUtc).TotalMilliseconds,
            Result = result
        };
    }

    public static OperationEnvelope Failed(string operationId, string capabilityId, DateTimeOffset startedAtUtc, OperationError error, object result = null)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        return new OperationEnvelope
        {
            OperationId = operationId,
            CapabilityId = capabilityId,
            Status = OperationStatus.Failed,
            Success = false,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMs = (long)(completedAtUtc - startedAtUtc).TotalMilliseconds,
            Result = result,
            Error = error
        };
    }
}
