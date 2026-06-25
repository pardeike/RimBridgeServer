using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimBridgeServer.Sdk;

public interface IRimBridgeHost
{
    IRimBridgeContext Current { get; }
}

public interface IRimBridgeContext
{
    string OperationId { get; }

    string CapabilityId { get; }

    IRimBridgeToolClient Tools { get; }

    IRimBridgeGameClock Game { get; }

    IRimBridgeMainThread MainThread { get; }
}

public interface IRimBridgeToolClient
{
    IReadOnlyList<RimBridgeToolDescriptor> List(RimBridgeToolQuery query = null);

    RimBridgeToolDescriptor Get(string idOrAlias);

    bool Exists(string idOrAlias);

    Task<RimBridgeToolCallResult<object>> CallAsync(string idOrAlias, object args = null, RimBridgeToolCallOptions options = null, CancellationToken cancellationToken = default);

    Task<RimBridgeToolCallResult<T>> CallAsync<T>(string idOrAlias, object args = null, RimBridgeToolCallOptions options = null, CancellationToken cancellationToken = default);

    Task<RimBridgeOperationInfo> QueueAsync(string idOrAlias, object args = null, RimBridgeToolCallOptions options = null, CancellationToken cancellationToken = default);
}

public interface IRimBridgeGameClock
{
    Task NextFrameAsync(CancellationToken cancellationToken = default);

    Task FramesAsync(int count, CancellationToken cancellationToken = default);

    Task<RimBridgeTickResult> StepTicksAsync(int ticks, RimBridgeTickOptions options = null, CancellationToken cancellationToken = default);

    Task<RimBridgeTickResult> RunForTicksAsync(int ticks, RimBridgeRunTicksOptions options = null, CancellationToken cancellationToken = default);

    Task<RimBridgeWaitResult> RunUntilAsync(Func<bool> predicate, RimBridgeWaitOptions options = null, CancellationToken cancellationToken = default);
}

public interface IRimBridgeMainThread
{
    bool IsMainThread { get; }

    Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default);

    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}

public sealed class RimBridgeToolQuery
{
    public string Text { get; set; }

    public string Category { get; set; }

    public string ProviderId { get; set; }

    public string Source { get; set; }
}

public sealed class RimBridgeToolCallOptions
{
    public int TimeoutMs { get; set; }

    public bool Queue { get; set; }
}

public sealed class RimBridgeTickOptions
{
    public int TimeoutMs { get; set; } = 10000;

    public bool PauseFirst { get; set; } = true;

    public bool PlaySound { get; set; }

    public bool FailIfBusy { get; set; }
}

public sealed class RimBridgeRunTicksOptions
{
    public int TimeoutMs { get; set; } = 10000;

    public bool ForceNormalSpeed { get; set; } = true;

    public bool PauseWhenDone { get; set; } = true;

    public bool FailIfBusy { get; set; }
}

public sealed class RimBridgeWaitOptions
{
    public int TimeoutMs { get; set; } = 10000;

    public bool FailIfBusy { get; set; }
}
