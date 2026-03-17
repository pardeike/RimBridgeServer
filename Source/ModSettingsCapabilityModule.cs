namespace RimBridgeServer;

internal sealed class ModSettingsCapabilityModule
{
    public object ListModSettingsSurfaces(bool includeWithoutSettings = false)
    {
        return RimWorldModSettings.ListModSettingsSurfacesResponse(includeWithoutSettings);
    }

    public object GetModSettings(string modId, int maxDepth = 4, int maxCollectionEntries = 32)
    {
        return RimWorldModSettings.GetModSettingsResponse(modId, maxDepth, maxCollectionEntries);
    }
}
