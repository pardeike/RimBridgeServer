namespace RimBridgeServer;

internal sealed class DebugActionsCapabilityModule
{
    public object ListDebugActionRoots(bool includeHidden = false)
    {
        return RimWorldDebugActions.ListDebugActionRootsResponse(includeHidden);
    }

    public object ListDebugActionChildren(string path, bool includeHidden = false)
    {
        return RimWorldDebugActions.ListDebugActionChildrenResponse(path, includeHidden);
    }

    public object GetDebugAction(string path, bool includeChildren = true, bool includeHiddenChildren = false)
    {
        return RimWorldDebugActions.GetDebugActionResponse(path, includeChildren, includeHiddenChildren);
    }

    public object ExecuteDebugAction(string path)
    {
        return RimWorldDebugActions.ExecuteDebugActionResponse(path);
    }

    public object SetDebugSetting(string path, bool enabled)
    {
        return RimWorldDebugActions.SetDebugSettingResponse(path, enabled);
    }
}
