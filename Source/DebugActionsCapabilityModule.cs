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

    public object SearchDebugActions(string query, int limit = 50, bool includeHidden = false, bool supportedOnly = false, string requiredTargetKind = null)
    {
        return RimWorldDebugActions.SearchDebugActionsResponse(query, limit, includeHidden, supportedOnly, requiredTargetKind);
    }

    public object GetDebugAction(string path, bool includeChildren = true, bool includeHiddenChildren = false)
    {
        return RimWorldDebugActions.GetDebugActionResponse(path, includeChildren, includeHiddenChildren);
    }

    public object ExecuteDebugAction(string path, string pawnName = null, string pawnId = null)
    {
        return RimWorldDebugActions.ExecuteDebugActionResponse(path, pawnName, pawnId);
    }

    public object SetDebugSetting(string path, bool enabled)
    {
        return RimWorldDebugActions.SetDebugSettingResponse(path, enabled);
    }

    public object SetColonistJobLogging(string pawnName = null, string pawnId = null, bool enabled = true)
    {
        return RimWorldDebugActions.SetColonistJobLoggingResponse(pawnName, pawnId, enabled);
    }
}
