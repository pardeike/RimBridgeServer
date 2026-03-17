using System;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldHover
{
    public static object SetHoverTargetResponse(string targetId = null, int? x = null, int? z = null, string thingId = null, string pawnName = null, string pawnId = null)
    {
        var hasUiTarget = !string.IsNullOrWhiteSpace(targetId);
        var hasCellTarget = x.HasValue || z.HasValue;
        var hasThingTarget = !string.IsNullOrWhiteSpace(thingId);
        var hasPawnTarget = !string.IsNullOrWhiteSpace(pawnName) || !string.IsNullOrWhiteSpace(pawnId);
        var targetKinds = (hasUiTarget ? 1 : 0) + ((hasCellTarget || hasThingTarget || hasPawnTarget) ? 1 : 0);
        if (targetKinds == 0)
        {
            return new
            {
                success = false,
                command = "set_hover_target",
                message = "Provide either a UI target id or a map target (cell, pawn, or thing)."
            };
        }

        if (targetKinds > 1)
        {
            return new
            {
                success = false,
                command = "set_hover_target",
                message = "UI hover targets and map hover targets are mutually exclusive. Provide only one target."
            };
        }

        if (hasUiTarget)
            return RimBridgeUiWorkbench.SetHoverTargetResponse(targetId);

        if (hasCellTarget && (!x.HasValue || !z.HasValue))
        {
            return new
            {
                success = false,
                command = "set_hover_target",
                message = "Both x and z are required when hovering a map cell."
            };
        }

        try
        {
            var hoverTarget = RimBridgeMainThread.Invoke(() => SetMapHoverTarget(x, z, thingId, pawnName, pawnId), timeoutMs: 5000);
            return new
            {
                success = true,
                command = "set_hover_target",
                message = "Hover target is active.",
                hoverTarget
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                command = "set_hover_target",
                message = ex.Message
            };
        }
    }

    public static object ClearHoverTargetResponse()
    {
        try
        {
            RimBridgeMainThread.Invoke(ClearHoverTarget, timeoutMs: 5000);
            return new
            {
                success = true,
                command = "clear_hover_target",
                message = "Hover target cleared.",
                hoverTarget = (object)null
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                command = "clear_hover_target",
                message = ex.Message
            };
        }
    }

    public static object DescribeHoverTarget()
    {
        return RimBridgeVirtualPointer.DescribePersistentPointer();
    }

    private static object SetMapHoverTarget(int? x, int? z, string thingId, string pawnName, string pawnId)
    {
        RimBridgeUiWorkbench.ClearHoveredElement();

        if (!string.IsNullOrWhiteSpace(thingId))
        {
            var thing = RimWorldState.ResolveCurrentMapThing(thingId);
            var targetPoint = thing.DrawPos.MapToUIPosition();
            RimBridgeVirtualPointer.SetPersistentPointer(
                kind: thing is Pawn ? "pawn" : "thing",
                targetId: RimWorldState.GetThingId(thing),
                label: thing.LabelCap.ToString(),
                screenPositionInverted: targetPoint,
                details: thing is Pawn pawn ? RimWorldState.DescribePawn(pawn) : RimWorldState.DescribeThing(thing));
            return RimBridgeVirtualPointer.DescribePersistentPointer();
        }

        if (!string.IsNullOrWhiteSpace(pawnName) || !string.IsNullOrWhiteSpace(pawnId))
        {
            var pawn = RimWorldState.ResolveCurrentMapPawn(pawnName, pawnId);
            var targetPoint = pawn.DrawPos.MapToUIPosition();
            RimBridgeVirtualPointer.SetPersistentPointer(
                kind: "pawn",
                targetId: RimWorldState.GetThingId(pawn),
                label: pawn.Name?.ToStringShort ?? pawn.LabelShort,
                screenPositionInverted: targetPoint,
                details: RimWorldState.DescribePawn(pawn));
            return RimBridgeVirtualPointer.DescribePersistentPointer();
        }

        var cell = new IntVec3(x.GetValueOrDefault(), 0, z.GetValueOrDefault());
        var map = RimWorldState.CurrentMapOrThrow();
        if (!cell.InBounds(map))
            throw new InvalidOperationException($"Cell ({cell.x}, {cell.z}) is out of bounds for the current map.");

        var cellCenter = RimWorldState.CellCenter(cell).MapToUIPosition();
        RimBridgeVirtualPointer.SetPersistentPointer(
            kind: "cell",
            targetId: $"cell:{cell.x}:{cell.z}",
            label: $"Cell ({cell.x}, {cell.z})",
            screenPositionInverted: cellCenter,
            details: new
            {
                cell = new
                {
                    x = cell.x,
                    z = cell.z
                },
                mapId = RimWorldState.GetMapId(map),
                mapIndex = map.Index
            });
        return RimBridgeVirtualPointer.DescribePersistentPointer();
    }

    private static void ClearHoverTarget()
    {
        RimBridgeUiWorkbench.ClearHoveredElement();
        RimBridgeVirtualPointer.ClearPersistentPointer();
    }
}
