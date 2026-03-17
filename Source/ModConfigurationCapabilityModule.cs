namespace RimBridgeServer;

internal sealed class ModConfigurationCapabilityModule
{
    public object ListMods(bool includeInactive = true)
    {
        return RimWorldModConfiguration.ListModsResponse(includeInactive);
    }

    public object GetModConfigurationStatus()
    {
        return RimWorldModConfiguration.GetModConfigurationStatusResponse();
    }

    public object SetModEnabled(string modId, bool enabled, bool save = true, bool allowDisableCore = false)
    {
        return RimWorldModConfiguration.SetModEnabledResponse(modId, enabled, save, allowDisableCore);
    }

    public object ReorderMod(string modId, int targetIndex, bool save = true)
    {
        return RimWorldModConfiguration.ReorderModResponse(modId, targetIndex, save);
    }
}
