using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimBridgeServer;

internal sealed class ContextMenuCapabilityModule
{
    private const int DefaultTimeoutMs = 2000;

    private sealed class MapClickTarget
    {
        public IntVec3 Cell { get; set; } = IntVec3.Invalid;

        public string Label { get; set; } = string.Empty;
    }

    public object OpenContextMenu(string targetPawnName = null, string targetPawnId = null, int x = 0, int z = 0, string mode = "vanilla")
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded." };

        var selectedPawns = Find.Selector.SelectedPawns.ToList();
        if (selectedPawns.Count == 0)
            return new { success = false, message = "No pawns are currently selected." };

        var map = RimWorldState.CurrentMapOrThrow();
        if (!TryResolveMapClickTarget(map, targetPawnName, targetPawnId, x, z, out var target, out var failure))
            return failure;

        var normalizedMode = (mode ?? "vanilla").Trim().ToLowerInvariant();
        if (normalizedMode == "auto")
            normalizedMode = "live";

        if (normalizedMode != "vanilla" && normalizedMode != "live")
        {
            return new
            {
                success = false,
                message = $"Unsupported context menu mode '{mode}'. Supported values are 'vanilla', 'auto', and 'live'."
            };
        }

        var dispatch = RimBridgeMapClickInjector.DispatchRightClick(target.Cell, target.Label, DefaultTimeoutMs);
        if (!dispatch.Success)
            return new { success = false, message = dispatch.Message };

        var snapshot = dispatch.Snapshot;
        if (snapshot == null)
        {
            return new
            {
                success = true,
                menuId = 0,
                provider = "ui_event",
                clickCell = new { x = target.Cell.x, z = target.Cell.z },
                target = target.Label,
                selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
                optionCount = 0,
                options = new List<object>(),
                message = dispatch.Message
            };
        }

        return new
        {
            success = true,
            menuId = snapshot.Id,
            provider = snapshot.Provider,
            clickCell = new { x = target.Cell.x, z = target.Cell.z },
            target = target.Label,
            selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
            optionCount = snapshot.Options.Count,
            options = DescribeOptions(snapshot.Options),
            message = dispatch.Message
        };
    }

    public object RightClickCell(string targetPawnName = null, string targetPawnId = null, int x = 0, int z = 0)
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded." };

        var selectedPawns = Find.Selector.SelectedPawns.ToList();
        if (selectedPawns.Count == 0)
            return new { success = false, message = "No pawns are currently selected." };

        var map = RimWorldState.CurrentMapOrThrow();
        if (!TryResolveMapClickTarget(map, targetPawnName, targetPawnId, x, z, out var target, out var failure))
            return failure;

        var dispatch = RimBridgeMapClickInjector.DispatchRightClick(target.Cell, target.Label, DefaultTimeoutMs);
        if (!dispatch.Success)
            return new { success = false, message = dispatch.Message };

        if (dispatch.Snapshot != null)
        {
            var snapshot = dispatch.Snapshot;
            return new
            {
                success = true,
                actionKind = "menu_opened",
                menuId = snapshot.Id,
                provider = snapshot.Provider,
                clickCell = new { x = target.Cell.x, z = target.Cell.z },
                target = target.Label,
                selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
                optionCount = snapshot.Options.Count,
                options = DescribeOptions(snapshot.Options),
                message = dispatch.Message
            };
        }

        return new
        {
            success = true,
            actionKind = "click_dispatched",
            clickCell = new { x = target.Cell.x, z = target.Cell.z },
            target = target.Label,
            selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
            message = dispatch.Message
        };
    }

    public object GetContextMenuOptions()
    {
        var snapshot = RimBridgeContextMenus.Current;
        if (snapshot == null || snapshot.Menu == null)
            return new { success = false, message = "No debug context menu has been opened yet." };
        if (Find.WindowStack.FloatMenu != snapshot.Menu)
        {
            RimBridgeContextMenus.Clear();
            return new { success = false, message = "No debug context menu has been opened yet." };
        }

        return new
        {
            success = true,
            menuId = snapshot.Id,
            provider = snapshot.Provider,
            target = snapshot.TargetLabel,
            clickCell = new { x = snapshot.ClickCell.x, z = snapshot.ClickCell.z },
            optionCount = snapshot.Options.Count,
            options = DescribeOptions(snapshot.Options)
        };
    }

    public object ExecuteContextMenuOption(int optionIndex = -1, string label = null)
    {
        var execution = RimWorldContextMenuActions.ExecuteOption(optionIndex, label);
        if (!execution.Success)
            return new { success = false, message = execution.Message, label = execution.Label };

        return new
        {
            success = true,
            executedIndex = execution.ResolvedIndex,
            label = execution.Label
        };
    }

    public object CloseContextMenu()
    {
        if (Find.WindowStack.FloatMenu != null)
            Find.WindowStack.TryRemove(Find.WindowStack.FloatMenu, doCloseSound: false);

        RimBridgeContextMenus.Clear();
        return new { success = true };
    }

    private static List<object> DescribeOptions(IEnumerable<FloatMenuOption> options)
    {
        return options.Select((option, index) => (object)new
        {
            index = index + 1,
            label = option.Label,
            disabled = option.Disabled,
            priority = option.Priority.ToString(),
            orderInPriority = option.orderInPriority,
            autoTakeable = option.autoTakeable,
            hasAction = option.action != null
        }).ToList();
    }

    private static bool TryResolveMapClickTarget(Map map, string targetPawnName, string targetPawnId, int x, int z, out MapClickTarget target, out object failure)
    {
        target = null;
        failure = null;

        if (!string.IsNullOrWhiteSpace(targetPawnName) || !string.IsNullOrWhiteSpace(targetPawnId))
        {
            var targetPawn = RimWorldState.ResolveCurrentMapPawn(targetPawnName, targetPawnId);
            if (!targetPawn.Spawned || targetPawn.Map != map)
            {
                var identifier = string.IsNullOrWhiteSpace(targetPawnId) ? targetPawnName : targetPawnId;
                failure = new { success = false, message = $"Pawn '{identifier}' is not spawned on the current map." };
                return false;
            }

            target = new MapClickTarget
            {
                Cell = targetPawn.Position,
                Label = targetPawn.Name?.ToStringShort ?? targetPawn.LabelShort
            };
            return true;
        }

        var clickCell = new IntVec3(x, 0, z);
        if (!clickCell.InBounds(map))
        {
            failure = new { success = false, message = $"Cell ({x}, {z}) is out of bounds for the current map." };
            return false;
        }

        target = new MapClickTarget
        {
            Cell = clickCell,
            Label = $"cell {x},{z}"
        };
        return true;
    }
}
