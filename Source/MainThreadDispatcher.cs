using System;
using RimBridgeServer.Core;

namespace RimBridgeServer;

internal sealed class MainThreadDispatcher : IGameThreadDispatcher
{
    public bool IsMainThread => RimBridgeMainThread.IsMainThread;

    public T Invoke<T>(Func<T> func, int timeoutMs)
    {
        return RimBridgeMainThread.Invoke(func, timeoutMs);
    }

    public void Invoke(Action action, int timeoutMs)
    {
        RimBridgeMainThread.Invoke(action, timeoutMs);
    }
}
