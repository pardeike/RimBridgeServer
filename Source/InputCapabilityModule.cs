namespace RimBridgeServer;

internal sealed class InputCapabilityModule
{
    public object GetUiState()
    {
        return RimWorldInput.GetUiStateResponse();
    }

    public object PressAccept()
    {
        return RimWorldInput.PressAcceptResponse();
    }

    public object PressCancel()
    {
        return RimWorldInput.PressCancelResponse();
    }

    public object CloseWindow(string windowType = null)
    {
        return RimWorldInput.CloseWindowResponse(windowType);
    }

    public object ClickScreenTarget(string targetId)
    {
        return RimWorldInput.ClickScreenTargetResponse(targetId);
    }

    public object ListMainTabs(bool includeHidden = false)
    {
        return RimWorldMainTabs.ListMainTabsResponse(includeHidden);
    }

    public object OpenMainTab(string mainTabId)
    {
        return RimWorldMainTabs.OpenMainTabResponse(mainTabId);
    }

    public object CloseMainTab(string mainTabId = null)
    {
        return RimWorldMainTabs.CloseMainTabResponse(mainTabId);
    }

    public object GetUiLayout(string surfaceId = null, int timeoutMs = 2000)
    {
        return RimBridgeUiWorkbench.GetUiLayoutResponse(surfaceId, timeoutMs);
    }

    public object ClickUiTarget(string targetId, int timeoutMs = 2000)
    {
        return RimBridgeUiWorkbench.ClickUiTargetResponse(targetId, timeoutMs);
    }
}
