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
    public string CapabilityId { get; set; } = string.Empty;

    public bool MarshalToMainThread { get; set; } = true;

    public int TimeoutMs { get; set; } = 10000;

    public string FailureCode { get; set; } = "tool.failed";
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

        var operationId = "op_" + Guid.NewGuid().ToString("N");
        var startedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            var result = options.MarshalToMainThread
                ? _dispatcher.Invoke(func, options.TimeoutMs)
                : func();

            return OperationEnvelope.Completed(operationId, options.CapabilityId, startedAtUtc, result);
        }
        catch (Exception ex)
        {
            var root = ex.InnerException ?? ex;
            return OperationEnvelope.Failed(operationId, options.CapabilityId, startedAtUtc, new OperationError
            {
                Code = options.FailureCode,
                Message = root.Message,
                ExceptionType = root.GetType().FullName ?? root.GetType().Name,
                Details = ex.ToString()
            });
        }
    }
}
