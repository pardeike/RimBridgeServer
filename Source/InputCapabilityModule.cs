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

    public object OpenWindowByType(string windowType, bool replaceExisting = true)
    {
        return RimWorldInput.OpenWindowByTypeResponse(windowType, replaceExisting);
    }

    public object ClickScreenTarget(string targetId)
    {
        return RimWorldInput.ClickScreenTargetResponse(targetId);
    }

    public object ListLanguages()
    {
        return RimWorldLanguages.ListLanguagesResponse();
    }

    public object SwitchLanguage(string language)
    {
        return RimWorldLanguages.SwitchLanguageResponse(language);
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

    public object ListInspectTabs(bool includeHidden = false)
    {
        return RimWorldInspectTabs.ListInspectTabsResponse(includeHidden);
    }

    public object OpenInspectTab(string inspectTabId)
    {
        return RimWorldInspectTabs.OpenInspectTabResponse(inspectTabId);
    }

    public object GetUiLayout(string surfaceId = null, int timeoutMs = 2000)
    {
        return RimBridgeUiWorkbench.GetUiLayoutResponse(surfaceId, timeoutMs);
    }

    public object ClickUiTarget(string targetId, int timeoutMs = 2000)
    {
        return RimBridgeUiWorkbench.ClickUiTargetResponse(targetId, timeoutMs);
    }

    public object ScrollUiTarget(string targetId, float deltaY = 0f, float deltaX = 0f, float? targetY = null, float? targetX = null, int timeoutMs = 2000)
    {
        return RimBridgeUiWorkbench.ScrollUiTargetResponse(targetId, deltaY, deltaX, targetY, targetX, timeoutMs);
    }

    public object SetHoverTarget(
        string targetId = null,
        int? x = null,
        int? z = null,
        string thingId = null,
        string pawnName = null,
        string pawnId = null,
        string anchor = "center",
        float offsetX = 0f,
        float offsetY = 0f,
        float? screenX = null,
        float? screenY = null,
        int? durationMs = null,
        int settleMs = RimWorldHover.DefaultHoverSettleMs)
    {
        return RimWorldHover.SetHoverTargetResponse(targetId, x, z, thingId, pawnName, pawnId, anchor, offsetX, offsetY, screenX, screenY, durationMs, settleMs);
    }

    public object ClearHoverTarget()
    {
        return RimWorldHover.ClearHoverTargetResponse();
    }
}
