namespace RimBridgeServer;

internal sealed class ArchitectCapabilityModule
{
    public object GetDesignatorState()
    {
        return RimWorldArchitect.GetDesignatorStateResponse();
    }

    public object SetGodMode(bool enabled = true)
    {
        return RimWorldArchitect.SetGodModeResponse(enabled);
    }

    public object ListArchitectCategories(bool includeHidden = false, bool includeEmpty = false)
    {
        return RimWorldArchitect.ListArchitectCategoriesResponse(includeHidden, includeEmpty);
    }

    public object ListArchitectDesignators(string categoryId, bool includeHidden = false)
    {
        return RimWorldArchitect.ListArchitectDesignatorsResponse(categoryId, includeHidden);
    }

    public object SelectArchitectDesignator(string designatorId)
    {
        return RimWorldArchitect.SelectArchitectDesignatorResponse(designatorId);
    }

    public object ApplyArchitectDesignator(string designatorId, int x, int z, int width = 1, int height = 1, bool dryRun = false, bool keepSelected = true)
    {
        return RimWorldArchitect.ApplyArchitectDesignatorResponse(designatorId, x, z, width, height, dryRun, keepSelected);
    }

    public object GetCellInfo(int x, int z)
    {
        return RimWorldArchitect.GetCellInfoResponse(x, z);
    }
}
