namespace RimBridgeServer;

internal sealed class SelectionSemanticsCapabilityModule
{
    public object GetSelectionSemantics()
    {
        return RimWorldSelectionSemantics.GetSelectionSemanticsResponse();
    }

    public object ListSelectedGizmos()
    {
        return RimWorldSelectionSemantics.ListSelectedGizmosResponse();
    }

    public object ExecuteGizmo(string gizmoId)
    {
        return RimWorldSelectionSemantics.ExecuteGizmoResponse(gizmoId);
    }
}
