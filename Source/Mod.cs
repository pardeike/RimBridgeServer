using System;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

public class RimBridgeServerMod : Mod
{
    private readonly RimBridgeServerSettings _settings;

    public RimBridgeServerMod(ModContentPack content)
        : base(content)
    {
        try
        {
            _settings = GetSettings<RimBridgeServerSettings>();
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

    public override string SettingsCategory()
    {
        return "RimBridgeServer";
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        RimBridgeServerSettingsDrawer.Draw(inRect, _settings);
    }
}
