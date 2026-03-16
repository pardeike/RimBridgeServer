using System;
using System.Collections.Generic;

namespace RimBridgeServer.Contracts;

[Flags]
public enum CapabilityExecutionMode
{
    None = 0,
    Immediate = 1,
    Wait = 2,
    Queue = 4
}

public enum CapabilityExecutionKind
{
    Immediate,
    MainThread,
    FrameBound,
    LongEventBound,
    BackgroundObserved
}

public enum CapabilitySourceKind
{
    Core,
    Optional,
    Extension
}

public sealed class CapabilityParameterDescriptor
{
    public string Name { get; set; } = string.Empty;

    public string ParameterType { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Required { get; set; }

    public object DefaultValue { get; set; }
}

public sealed class CapabilityDescriptor
{
    public string Id { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public CapabilitySourceKind Source { get; set; }

    public CapabilityExecutionKind ExecutionKind { get; set; }

    public CapabilityExecutionMode SupportedModes { get; set; } = CapabilityExecutionMode.Wait;

    public bool EmitsEvents { get; set; }

    public string ResultType { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    public List<CapabilityParameterDescriptor> Parameters { get; set; } = [];

    public bool SupportsMode(CapabilityExecutionMode mode)
    {
        return (SupportedModes & mode) == mode;
    }
}

public sealed class CapabilityInvocation
{
    public string CapabilityId { get; set; } = string.Empty;

    public string OperationId { get; set; } = string.Empty;

    public CapabilityExecutionMode RequestedMode { get; set; } = CapabilityExecutionMode.Wait;

    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Dictionary<string, object> Arguments { get; set; } = [];
}
