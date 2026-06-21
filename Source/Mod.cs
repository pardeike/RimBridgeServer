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
            using (RimBridgeStartupTiming.Phase("mod-constructor.total"))
            {
                using (RimBridgeStartupTiming.Phase("main-thread.initialize"))
                    RimBridgeMainThread.Initialize();
                using (RimBridgeStartupTiming.Phase("harmony.startup-patches"))
                    RimBridgePatches.Apply();
                using (RimBridgeStartupTiming.Phase("startup.on-mod-constructed"))
                    RimBridgeStartup.OnModConstructed();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[RimBridge] STARTUP_INIT_FAILURE: {ex}");
            Log.Error($"[RimBridge] Failed to initialize server: {ex}");
        }
    }
}
