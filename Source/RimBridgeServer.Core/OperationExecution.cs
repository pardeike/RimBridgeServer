using System;
using RimBridgeServer.Contracts;

namespace RimBridgeServer.Core;

public interface IGameThreadDispatcher
{
    bool IsMainThread { get; }

    T Invoke<T>(Func<T> func, int timeoutMs);

    void Invoke(Action action, int timeoutMs);
}

public sealed class OperationExecutionOptions
{
    public string OperationId { get; set; } = string.Empty;

    public string CapabilityId { get; set; } = string.Empty;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public bool MarshalToMainThread { get; set; } = true;

    public int TimeoutMs { get; set; } = 10000;

    public string FailureCode { get; set; } = "tool.failed";

    public string TimeoutCode { get; set; } = "tool.timed_out";

    public string CancellationCode { get; set; } = "tool.cancelled";
}

public sealed class OperationRunner
{
    private readonly IGameThreadDispatcher _dispatcher;

    public OperationRunner(IGameThreadDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public OperationEnvelope Run(Func<object> func, OperationExecutionOptions options)
    {
        if (func == null)
            throw new ArgumentNullException(nameof(func));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var operationId = string.IsNullOrWhiteSpace(options.OperationId)
            ? "op_" + Guid.NewGuid().ToString("N")
            : options.OperationId;
        var startedAtUtc = options.StartedAtUtc ?? DateTimeOffset.UtcNow;

        try
        {
            var result = options.MarshalToMainThread
                ? _dispatcher.Invoke(func, options.TimeoutMs)
                : func();

            return OperationEnvelope.Completed(operationId, options.CapabilityId, startedAtUtc, result);
        }
        catch (OperationCanceledException ex)
        {
            return OperationEnvelope.Cancelled(operationId, options.CapabilityId, startedAtUtc, CreateError(ex, options.CancellationCode));
        }
        catch (TimeoutException ex)
        {
            return OperationEnvelope.TimedOut(operationId, options.CapabilityId, startedAtUtc, CreateError(ex, options.TimeoutCode));
        }
        catch (Exception ex)
        {
            return OperationEnvelope.Failed(operationId, options.CapabilityId, startedAtUtc, CreateError(ex, options.FailureCode));
        }
    }

    private static OperationError CreateError(Exception exception, string code)
    {
        var root = exception.InnerException ?? exception;
        return new OperationError
        {
            Code = code,
            Message = root.Message,
            ExceptionType = root.GetType().FullName ?? root.GetType().Name,
            Details = exception.ToString()
        };
    }
}
