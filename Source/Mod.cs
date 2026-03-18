using System;
using Verse;

namespace RimBridgeServer;

public class RimBridgeServerMod : Mod
{
    public RimBridgeServerMod(ModContentPack content)
        : base(content)
    {
        try
        {
            RimBridgeMainThread.Initialize();
            RimBridgePatches.Apply();
            RimBridgeStartup.OnModConstructed();
        }
        catch (Exception ex)
        {
            Log.Error($"[RimBridge] STARTUP_INIT_FAILURE: {ex}");
            Log.Error($"[RimBridge] Failed to initialize server: {ex}");
        }
    }
}
