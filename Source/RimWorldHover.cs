using System;
using System.Threading;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldHover
{
    internal const int DefaultHoverSettleMs = 600;
    private const int MaximumHoverSettleMs = 3000;

    public static object SetHoverTargetResponse(
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
        int settleMs = DefaultHoverSettleMs)
    {
        var hasUiTarget = !string.IsNullOrWhiteSpace(targetId);
        var hasCellTarget = x.HasValue || z.HasValue;
        var hasThingTarget = !string.IsNullOrWhiteSpace(thingId);
        var hasPawnTarget = !string.IsNullOrWhiteSpace(pawnName) || !string.IsNullOrWhiteSpace(pawnId);
        var hasScreenTarget = screenX.HasValue || screenY.HasValue;
        var targetKinds = (hasUiTarget ? 1 : 0)
            + (hasScreenTarget ? 1 : 0)
            + ((hasCellTarget || hasThingTarget || hasPawnTarget) ? 1 : 0);
        if (targetKinds == 0)
        {
            return new
            {
                success = false,
                command = "set_hover_target",
                message = "Provide a UI target id, explicit screen coordinates, or a map target (cell, pawn, or thing)."
            };
        }

        if (targetKinds > 1)
        {
            return new
            {
                success = false,
                command = "set_hover_target",
                message = "UI target ids, explicit screen coordinates, and map hover targets are mutually exclusive. Provide only one target."
            };
        }

        if (hasUiTarget)
        {
            if (targetId.Trim().StartsWith("ui-", StringComparison.OrdinalIgnoreCase))
                return RimBridgeUiWorkbench.SetHoverTargetResponse(targetId, anchor, offsetX, offsetY, durationMs, settleMs);

            return SetScreenTargetHoverTargetResponse(targetId, anchor, offsetX, offsetY, durationMs, settleMs);
        }

        if (hasScreenTarget)
        {
            if (!screenX.HasValue || !screenY.HasValue)
            {
                return new
                {
                    success = false,
                    command = "set_hover_target",
                    message = "Both screenX and screenY are required when hovering an explicit screen coordinate."
                };
            }

            try
            {
                var hoverTarget = RimBridgeMainThread.Invoke(
                    () => SetScreenHoverTarget(screenX.Value, screenY.Value, durationMs),
                    timeoutMs: 5000);
                SettleHoverIfRequested(settleMs);
                return new
                {
                    success = true,
                    command = "set_hover_target",
                    message = "Hover target is active.",
                    hoverTarget,
                    settleMs = NormalizeHoverSettleMs(settleMs)
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
            var hoverTarget = RimBridgeMainThread.Invoke(
                () => SetMapHoverTarget(x, z, thingId, pawnName, pawnId, offsetX, offsetY, durationMs),
                timeoutMs: 5000);
            SettleHoverIfRequested(settleMs);
            return new
            {
                success = true,
                command = "set_hover_target",
                message = "Hover target is active.",
                hoverTarget,
                settleMs = NormalizeHoverSettleMs(settleMs)
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

    public static void ClearHoverTargetForRealInput(Event currentEvent)
    {
        if (!RimBridgeVirtualPointer.ClearPersistentPointerForRealInput(currentEvent))
            return;

        RimBridgeUiWorkbench.ClearHoveredElement();
    }

    public static void ClearHoverTargetForRealInputState()
    {
        if (!RimBridgeVirtualPointer.ClearPersistentPointerForRealInputState())
            return;

        RimBridgeUiWorkbench.ClearHoveredElement();
    }

    public static void ClearExpiredHoverTarget()
    {
        if (RimBridgeVirtualPointer.ClearExpiredPersistentPointer() || !RimBridgeVirtualPointer.HasActivePersistentPointer())
            RimBridgeUiWorkbench.ClearHoveredElement();
    }

    internal static void ClearHoverTargetDirect()
    {
        RimBridgeUiWorkbench.ClearHoveredElement();
        RimBridgeVirtualPointer.ClearPersistentPointer();
    }

    internal static Vector2 ResolveRectPoint(UiRectSnapshot rect, string anchor, float offsetX, float offsetY)
    {
        if (rect == null)
            throw new InvalidOperationException("Cannot resolve a hover point from a missing screen rectangle.");

        var normalizedAnchor = string.IsNullOrWhiteSpace(anchor)
            ? "center"
            : anchor.Trim().Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();

        var x = normalizedAnchor switch
        {
            "topleft" or "left" or "bottomleft" => rect.X,
            "topright" or "right" or "bottomright" => rect.X + rect.Width,
            _ => rect.X + rect.Width / 2f
        };
        var y = normalizedAnchor switch
        {
            "topleft" or "top" or "topright" => rect.Y,
            "bottomleft" or "bottom" or "bottomright" => rect.Y + rect.Height,
            _ => rect.Y + rect.Height / 2f
        };

        return new Vector2(x + offsetX, y + offsetY);
    }

    internal static int NormalizeHoverSettleMs(int settleMs)
    {
        return Mathf.Clamp(settleMs, 0, MaximumHoverSettleMs);
    }

    internal static void SettleHoverIfRequested(int settleMs)
    {
        var normalizedSettleMs = NormalizeHoverSettleMs(settleMs);
        if (normalizedSettleMs > 0)
            Thread.Sleep(normalizedSettleMs);
    }

    private static object SetScreenTargetHoverTargetResponse(string targetId, string anchor, float offsetX, float offsetY, int? durationMs, int settleMs)
    {
        try
        {
            var hoverTarget = RimBridgeMainThread.Invoke(
                () => SetScreenTargetHoverTarget(targetId, anchor, offsetX, offsetY, durationMs),
                timeoutMs: 5000);
            SettleHoverIfRequested(settleMs);
            return new
            {
                success = true,
                command = "set_hover_target",
                message = $"Hover target '{targetId}' is active.",
                hoverTarget,
                settleMs = NormalizeHoverSettleMs(settleMs)
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

    private static object SetScreenTargetHoverTarget(string targetId, string anchor, float offsetX, float offsetY, int? durationMs)
    {
        if (!RimWorldTargeting.TryResolveClipArea(targetId, out var clipArea, out var error))
            throw new InvalidOperationException(error);

        var targetPoint = ResolveRectPoint(clipArea.Rect, anchor, offsetX, offsetY);
        RimBridgeUiWorkbench.ClearHoveredElement();
        RimBridgeVirtualPointer.SetPersistentPointer(
            kind: clipArea.TargetKind,
            targetId: clipArea.TargetId,
            label: clipArea.Label,
            screenPositionInverted: targetPoint,
            details: new
            {
                source = "screen_target",
                anchor = string.IsNullOrWhiteSpace(anchor) ? "center" : anchor,
                offsetX,
                offsetY,
                rect = new
                {
                    x = clipArea.Rect.X,
                    y = clipArea.Rect.Y,
                    width = clipArea.Rect.Width,
                    height = clipArea.Rect.Height
                }
            },
            durationMs: durationMs);
        return RimBridgeVirtualPointer.DescribePersistentPointer();
    }

    private static object SetScreenHoverTarget(float screenX, float screenY, int? durationMs)
    {
        RimBridgeUiWorkbench.ClearHoveredElement();
        RimBridgeVirtualPointer.SetPersistentPointer(
            kind: "screen",
            targetId: $"screen:{screenX:0.##}:{screenY:0.##}",
            label: $"Screen ({screenX:0.##}, {screenY:0.##})",
            screenPositionInverted: new Vector2(screenX, screenY),
            details: new
            {
                source = "screen_coordinate",
                screenPosition = new
                {
                    x = screenX,
                    y = screenY
                }
            },
            durationMs: durationMs);
        return RimBridgeVirtualPointer.DescribePersistentPointer();
    }

    private static object SetMapHoverTarget(int? x, int? z, string thingId, string pawnName, string pawnId, float offsetX, float offsetY, int? durationMs)
    {
        RimBridgeUiWorkbench.ClearHoveredElement();

        if (!string.IsNullOrWhiteSpace(thingId))
        {
            var thing = RimWorldState.ResolveCurrentMapThing(thingId);
            var targetPoint = ApplyOffset(thing.DrawPos.MapToUIPosition(), offsetX, offsetY);
            RimBridgeVirtualPointer.SetPersistentPointer(
                kind: thing is Pawn ? "pawn" : "thing",
                targetId: RimWorldState.GetThingId(thing),
                label: thing.LabelCap.ToString(),
                screenPositionInverted: targetPoint,
                details: thing is Pawn pawn ? RimWorldState.DescribePawn(pawn) : RimWorldState.DescribeThing(thing),
                durationMs: durationMs);
            return RimBridgeVirtualPointer.DescribePersistentPointer();
        }

        if (!string.IsNullOrWhiteSpace(pawnName) || !string.IsNullOrWhiteSpace(pawnId))
        {
            var pawn = RimWorldState.ResolveCurrentMapPawn(pawnName, pawnId);
            var targetPoint = ApplyOffset(pawn.DrawPos.MapToUIPosition(), offsetX, offsetY);
            RimBridgeVirtualPointer.SetPersistentPointer(
                kind: "pawn",
                targetId: RimWorldState.GetThingId(pawn),
                label: pawn.Name?.ToStringShort ?? pawn.LabelShort,
                screenPositionInverted: targetPoint,
                details: RimWorldState.DescribePawn(pawn),
                durationMs: durationMs);
            return RimBridgeVirtualPointer.DescribePersistentPointer();
        }

        var cell = new IntVec3(x.GetValueOrDefault(), 0, z.GetValueOrDefault());
        var map = RimWorldState.CurrentMapOrThrow();
        if (!cell.InBounds(map))
            throw new InvalidOperationException($"Cell ({cell.x}, {cell.z}) is out of bounds for the current map.");

        var cellCenter = ApplyOffset(RimWorldState.CellCenter(cell).MapToUIPosition(), offsetX, offsetY);
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
            },
            durationMs: durationMs);
        return RimBridgeVirtualPointer.DescribePersistentPointer();
    }

    private static Vector2 ApplyOffset(Vector2 point, float offsetX, float offsetY)
    {
        if (Math.Abs(offsetX) < 0.001f && Math.Abs(offsetY) < 0.001f)
            return point;

        return new Vector2(point.x + offsetX, point.y + offsetY);
    }

    private static void ClearHoverTarget()
    {
        ClearHoverTargetDirect();
    }
}
