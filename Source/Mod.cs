using System;
using System.Reflection;
using Lib.GAB.Server;
using Verse;

namespace RimBridgeServer;

public class RimBridgeServerMod : Mod
{
    private readonly GabpServer _server;

    public RimBridgeServerMod(ModContentPack content)
        : base(content)
    {
        try
        {
            RimBridgeMainThread.Initialize();
            RimBridgePatches.Apply();

            var tools = new RimBridgeTools();
            var version = typeof(RimBridgeServerMod).Assembly.GetName().Version?.ToString() ?? "0.1.0.0";
            _server = Lib.GAB.Gabp.CreateGabsAwareServerWithInstance("RimBridgeServer", version, tools, fallbackPort: 5174);

            _server.StartAsync().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Log.Error($"[RimBridge] Failed to start server: {task.Exception}");
                    return;
                }

                if (Lib.GAB.Gabp.IsRunningUnderGabs())
                {
                    Log.Message($"[RimBridge] GABP server connected to GABS on port {_server.Port}");
                }
                else
                {
                    Log.Message($"[RimBridge] GABP server running standalone on port {_server.Port}");
                    Log.Message($"[RimBridge] Bridge token: {_server.Token}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[RimBridge] Failed to initialize server: {ex}");
        }
    }
}
