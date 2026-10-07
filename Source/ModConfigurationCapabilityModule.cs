namespace RimBridgeServer;

internal sealed class ModConfigurationCapabilityModule
{
    public object ListMods(bool includeInactive = true, bool includeDetails = false, int offset = 0)
    {
        return RimWorldModConfiguration.ListModsResponse(includeInactive, includeDetails, offset);
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
