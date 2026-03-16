using System;

namespace RimBridgeServer.Core;

public enum DebugActionExecutionKind
{
    BrowseOnly,
    Direct,
    MapTarget,
    PawnTarget,
    WorldTarget
}

public readonly struct DebugActionExecutionAssessment
{
    public DebugActionExecutionAssessment(DebugActionExecutionKind kind, bool supported, string reason, string requiredTargetKind = null)
    {
        Kind = kind;
        Supported = supported;
        Reason = reason ?? string.Empty;
        RequiredTargetKind = requiredTargetKind ?? string.Empty;
    }

    public DebugActionExecutionKind Kind { get; }

    public bool Supported { get; }

    public string Reason { get; }

    public string RequiredTargetKind { get; }
}

public static class DebugActionExecutionPolicy
{
    public static DebugActionExecutionAssessment Evaluate(bool hasChildren, string actionType, bool hasAction, bool hasPawnAction)
    {
        if (hasChildren)
        {
            return new DebugActionExecutionAssessment(
                DebugActionExecutionKind.BrowseOnly,
                supported: false,
                reason: "This debug node is a submenu. Browse its children instead of executing it directly.");
        }

        switch (actionType?.Trim())
        {
            case "Action":
                if (hasAction)
                {
                    return new DebugActionExecutionAssessment(
                        DebugActionExecutionKind.Direct,
                        supported: true,
                        reason: string.Empty);
                }

                if (hasPawnAction)
                {
                    return new DebugActionExecutionAssessment(
                        DebugActionExecutionKind.PawnTarget,
                        supported: true,
                        reason: "This debug action requires a pawn target input.",
                        requiredTargetKind: "pawn");
                }

                return new DebugActionExecutionAssessment(
                    DebugActionExecutionKind.BrowseOnly,
                    supported: false,
                    reason: "No direct action delegate is exposed for this debug node.");

            case "ToolMap":
                return new DebugActionExecutionAssessment(
                    DebugActionExecutionKind.MapTarget,
                    supported: false,
                    reason: "This debug action requires a map target. Targeted map execution is not implemented yet.",
                    requiredTargetKind: "map");

            case "ToolMapForPawns":
                return new DebugActionExecutionAssessment(
                    DebugActionExecutionKind.PawnTarget,
                    supported: true,
                    reason: "This debug action requires a pawn target input.",
                    requiredTargetKind: "pawn");

            case "ToolWorld":
                return new DebugActionExecutionAssessment(
                    DebugActionExecutionKind.WorldTarget,
                    supported: false,
                    reason: "This debug action requires a world target. World-target execution is not implemented yet.",
                    requiredTargetKind: "world");

            default:
                return new DebugActionExecutionAssessment(
                    DebugActionExecutionKind.BrowseOnly,
                    supported: false,
                    reason: string.IsNullOrWhiteSpace(actionType)
                        ? "This debug node does not expose a usable action type."
                        : $"Unsupported debug action type '{actionType}'.");
        }
    }
}
