namespace RimBridgeServer;

internal sealed class DebugActionsCapabilityModule
{
    public object ListDebugActionRoots(bool includeHidden = false)
    {
        return RimWorldDebugActions.ListDebugActionRootsResponse(includeHidden);
    }

    public object ListDebugActionChildren(string path, bool includeHidden = false, int offset = 0)
    {
        return RimWorldDebugActions.ListDebugActionChildrenResponse(path, includeHidden, offset);
    }

    public object SearchDebugActions(string query, int limit = 50, bool includeHidden = false, bool supportedOnly = false, string requiredTargetKind = null, int offset = 0)
    {
        return RimWorldDebugActions.SearchDebugActionsResponse(query, limit, includeHidden, supportedOnly, requiredTargetKind, offset);
    }

    public object GetDebugAction(string path, bool includeChildren = true, bool includeHiddenChildren = false, int offset = 0)
    {
        return RimWorldDebugActions.GetDebugActionResponse(path, includeChildren, includeHiddenChildren, offset);
    }

    public object ExecuteDebugAction(
        string path,
        string pawnName = null,
        string pawnId = null,
        int? x = null,
        int? z = null,
        string thingId = null)
    {
        return RimWorldDebugActions.ExecuteDebugActionResponse(path, pawnName, pawnId, x, z, thingId);
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
