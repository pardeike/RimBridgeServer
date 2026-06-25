using System;
using System.Collections.Generic;

namespace RimBridgeServer.Sdk;

public sealed class RimBridgeToolDescriptor
{
    public string Id { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string ExecutionKind { get; set; } = string.Empty;

    public IReadOnlyList<string> SupportedModes { get; set; } = [];

    public bool EmitsEvents { get; set; }

    public string ResultType { get; set; } = string.Empty;

    public IReadOnlyList<string> Aliases { get; set; } = [];

    public IReadOnlyList<RimBridgeToolParameter> Parameters { get; set; } = [];
}

public sealed class RimBridgeToolParameter
{
    public string Name { get; set; } = string.Empty;

    public string ParameterType { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Required { get; set; }

    public object DefaultValue { get; set; }
}

public sealed class RimBridgeToolCallResult<T>
{
    public bool Success { get; set; }

    public string OperationId { get; set; } = string.Empty;

    public string CapabilityId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public T Result { get; set; }

    public RimBridgeOperationError Error { get; set; }
}

public sealed class RimBridgeOperationInfo
{
    public string OperationId { get; set; } = string.Empty;

    public string CapabilityId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool Success { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public long? DurationMs { get; set; }

    public bool HasResult { get; set; }

    public RimBridgeOperationError Error { get; set; }
}

public sealed class RimBridgeOperationError
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string ExceptionType { get; set; } = string.Empty;

    public object Details { get; set; }
}

public sealed class RimBridgeTickResult
{
    public bool Success { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int RequestedTicks { get; set; }

    public int CompletedTicks { get; set; }

    public int StartTicksGame { get; set; }

    public int EndTicksGame { get; set; }

    public int AdvancedTicks { get; set; }

    public int AdvancedFrames { get; set; }
}

public sealed class RimBridgeWaitResult
{
    public bool Success { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int ElapsedFrames { get; set; }

    public int StartTicksGame { get; set; }

    public int EndTicksGame { get; set; }

    public int AdvancedTicks { get; set; }
}
