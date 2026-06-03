using System.Collections.Generic;

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

    public object GetUiLayout(string surfaceId = null, int timeoutMs = 2000)
    {
        return RimBridgeUiWorkbench.GetUiLayoutResponse(surfaceId, timeoutMs);
    }

    public object PointerMove(
        Dictionary<string, object> target,
        Dictionary<string, object> offset = null,
        int durationMs = 0,
        int steps = 0,
        bool persist = true,
        bool waitForTooltip = false,
        int timeoutMs = 2000)
    {
        return RimBridgePointer.PointerMoveResponse(target, offset, durationMs, steps, persist, waitForTooltip, timeoutMs);
    }

    public object PointerGesture(
        Dictionary<string, object> from,
        Dictionary<string, object> to = null,
        string button = "left",
        List<object> modifiers = null,
        int durationMs = 250,
        int steps = 8,
        int holdStartMs = 0,
        int holdEndMs = 0,
        int timeoutMs = 3000,
        bool leavePointerAtEnd = false)
    {
        return RimBridgePointer.PointerGestureResponse(from, to, button, modifiers, durationMs, steps, holdStartMs, holdEndMs, timeoutMs, leavePointerAtEnd);
    }

    public object PointerClear()
    {
        return RimBridgePointer.PointerClearResponse();
    }

    public object ClickUiTarget(string targetId, int timeoutMs = 2000)
    {
        return RimBridgeUiWorkbench.ClickUiTargetResponse(targetId, timeoutMs);
    }

    public object ScrollUiTarget(string targetId, float deltaY = 0f, float deltaX = 0f, float? targetY = null, float? targetX = null, int timeoutMs = 2000)
    {
        return RimBridgeUiWorkbench.ScrollUiTargetResponse(targetId, deltaY, deltaX, targetY, targetX, timeoutMs);
    }

    public object SetHoverTarget(string targetId = null, int? x = null, int? z = null, string thingId = null, string pawnName = null, string pawnId = null)
    {
        return RimWorldHover.SetHoverTargetResponse(targetId, x, z, thingId, pawnName, pawnId);
    }

    public object ClearHoverTarget()
    {
        return RimWorldHover.ClearHoverTargetResponse();
    }
}
