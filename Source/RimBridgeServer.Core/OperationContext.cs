using System;
using System.Threading;

namespace RimBridgeServer.Core;

public sealed class OperationContextSnapshot
{
    public string OperationId { get; set; } = string.Empty;

    public string CapabilityId { get; set; } = string.Empty;

    public string ParentOperationId { get; set; } = string.Empty;

    public string RootOperationId { get; set; } = string.Empty;

    public string ScriptStatementId { get; set; } = string.Empty;

    public string ScriptStepId { get; set; } = string.Empty;

    public string ScriptCall { get; set; } = string.Empty;
}

public static class OperationContext
{
    private sealed class RestoreScope : IDisposable
    {
        private readonly OperationContextSnapshot _previous;
        private bool _disposed;

        public RestoreScope(OperationContextSnapshot previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Current = Clone(_previous);
            _disposed = true;
        }
    }

    private static readonly AsyncLocal<OperationContextSnapshot> CurrentSlot = new();

    public static OperationContextSnapshot Current
    {
        get => Clone(CurrentSlot.Value);
        private set => CurrentSlot.Value = value == null ? null : Clone(value);
    }

    public static OperationContextSnapshot Capture()
    {
        return Current;
    }

    public static IDisposable PushOperation(string operationId, string capabilityId)
    {
        var previous = CurrentSlot.Value;
        var rootOperationId = string.IsNullOrWhiteSpace(previous?.RootOperationId)
            ? (string.IsNullOrWhiteSpace(previous?.OperationId) ? operationId ?? string.Empty : previous.OperationId)
            : previous.RootOperationId;

        Current = new OperationContextSnapshot
        {
            OperationId = operationId ?? string.Empty,
            CapabilityId = capabilityId ?? string.Empty,
            ParentOperationId = previous?.OperationId ?? string.Empty,
            RootOperationId = rootOperationId ?? string.Empty,
            ScriptStatementId = previous?.ScriptStatementId ?? string.Empty,
            ScriptStepId = previous?.ScriptStepId ?? string.Empty,
            ScriptCall = previous?.ScriptCall ?? string.Empty
        };

        return new RestoreScope(previous);
    }

    public static IDisposable PushMetadata(string scriptStatementId = null, string scriptStepId = null, string scriptCall = null)
    {
        var previous = CurrentSlot.Value;
        Current = new OperationContextSnapshot
        {
            OperationId = previous?.OperationId ?? string.Empty,
            CapabilityId = previous?.CapabilityId ?? string.Empty,
            ParentOperationId = previous?.ParentOperationId ?? string.Empty,
            RootOperationId = previous?.RootOperationId ?? string.Empty,
            ScriptStatementId = scriptStatementId ?? previous?.ScriptStatementId ?? string.Empty,
            ScriptStepId = scriptStepId ?? previous?.ScriptStepId ?? string.Empty,
            ScriptCall = scriptCall ?? previous?.ScriptCall ?? string.Empty
        };

        return new RestoreScope(previous);
    }

    public static IDisposable Restore(OperationContextSnapshot snapshot)
    {
        var previous = CurrentSlot.Value;
        Current = snapshot;
        return new RestoreScope(previous);
    }

    private static OperationContextSnapshot Clone(OperationContextSnapshot snapshot)
    {
        if (snapshot == null)
            return null;

        return new OperationContextSnapshot
        {
            OperationId = snapshot.OperationId,
            CapabilityId = snapshot.CapabilityId,
            ParentOperationId = snapshot.ParentOperationId,
            RootOperationId = snapshot.RootOperationId,
            ScriptStatementId = snapshot.ScriptStatementId,
            ScriptStepId = snapshot.ScriptStepId,
            ScriptCall = snapshot.ScriptCall
        };
    }
}
