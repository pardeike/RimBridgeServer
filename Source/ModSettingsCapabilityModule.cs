using System.Collections.Generic;

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

    public object UpdateModSettings(string modId, Dictionary<string, object> values, bool write = true, int maxDepth = 4, int maxCollectionEntries = 32)
    {
        return RimWorldModSettings.UpdateModSettingsResponse(modId, values, write, maxDepth, maxCollectionEntries);
    }

    public object ReloadModSettings(string modId, int maxDepth = 4, int maxCollectionEntries = 32)
    {
        return RimWorldModSettings.ReloadModSettingsResponse(modId, maxDepth, maxCollectionEntries);
    }

    public object OpenModSettings(string modId, bool replaceExisting = true)
    {
        return RimWorldModSettings.OpenModSettingsResponse(modId, replaceExisting);
    }
}
