using System;

namespace RimBridgeServer.Sdk;

public static class RimBridge
{
    private static readonly object Sync = new();
    private static IRimBridgeHost _host;

    public static bool IsReady
    {
        get
        {
            lock (Sync)
            {
                return _host != null;
            }
        }
    }

    public static IRimBridgeContext Current => Host.Current;

    public static IRimBridgeToolClient Tools => Current.Tools;

    public static IRimBridgeGameClock Game => Current.Game;

    public static IRimBridgeMainThread MainThread => Current.MainThread;

    public static void SetHost(IRimBridgeHost host)
    {
        lock (Sync)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }
    }

    public static void ClearHost()
    {
        lock (Sync)
        {
            _host = null;
        }
    }

    private static IRimBridgeHost Host
    {
        get
        {
            lock (Sync)
            {
                return _host ?? throw new RimBridgeNotReadyException();
            }
        }
    }
}

public sealed class RimBridgeNotReadyException : InvalidOperationException
{
    public RimBridgeNotReadyException()
        : base("RimBridgeServer SDK host is not ready. Use the SDK from inside a RimBridge companion tool invocation, or wait until RimBridgeServer has completed companion registration.")
    {
    }
}
