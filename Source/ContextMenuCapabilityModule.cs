using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class ContextMenuCapabilityModule
{
    public object OpenContextMenu(string targetPawnName = null, int x = 0, int z = 0, string mode = "vanilla")
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded." };

        var selectedPawns = Find.Selector.SelectedPawns.ToList();
        if (selectedPawns.Count == 0)
            return new { success = false, message = "No pawns are currently selected." };

        var map = RimWorldState.CurrentMapOrThrow();
        Pawn targetPawn = null;
        IntVec3 clickCell;
        string targetLabel;

        if (!string.IsNullOrWhiteSpace(targetPawnName))
        {
            targetPawn = RimWorldState.ResolveCurrentMapPawn(targetPawnName);
            if (!targetPawn.Spawned || targetPawn.Map != map)
                return new { success = false, message = $"Pawn '{targetPawnName}' is not spawned on the current map." };

            clickCell = targetPawn.Position;
            targetLabel = targetPawn.Name?.ToStringShort ?? targetPawn.LabelShort;
        }
        else
        {
            clickCell = new IntVec3(x, 0, z);
            if (!clickCell.InBounds(map))
                return new { success = false, message = $"Cell ({x}, {z}) is out of bounds for the current map." };

            targetLabel = $"cell {x},{z}";
        }

        if (Find.WindowStack.FloatMenu != null)
            Find.WindowStack.TryRemove(Find.WindowStack.FloatMenu, doCloseSound: false);

        var clickPos = RimWorldState.CellCenter(clickCell);
        var normalizedMode = (mode ?? "vanilla").Trim().ToLowerInvariant();
        if (normalizedMode == "auto")
            normalizedMode = "vanilla";

        if (normalizedMode != "vanilla")
        {
            return new
            {
                success = false,
                message = $"Unsupported context menu mode '{mode}'. Only vanilla is supported."
            };
        }

        FloatMenu menu = null;
        var options = FloatMenuMakerMap.GetOptions(selectedPawns, clickPos, out _).ToList();
        const string provider = "vanilla";

        if (options.Count == 0)
        {
            RimBridgeContextMenus.Clear();
            return new
            {
                success = true,
                menuId = 0,
                provider,
                clickCell = new { x = clickCell.x, z = clickCell.z },
                target = targetLabel,
                selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
                optionCount = 0,
                options = new List<object>(),
                message = "No context menu options were generated for the current selection and target."
            };
        }

        if (menu == null)
            menu = new FloatMenu(options) { givesColonistOrders = true };

        Find.WindowStack.Add(menu);
        PositionDebugMenu(menu);
        var snapshot = RimBridgeContextMenus.Store(provider, menu, options, clickCell, targetLabel);

        return new
        {
            success = true,
            menuId = snapshot.Id,
            provider,
            clickCell = new { x = clickCell.x, z = clickCell.z },
            target = targetLabel,
            selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
            optionCount = options.Count,
            options = DescribeOptions(snapshot.Options)
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

    private static void PositionDebugMenu(FloatMenu menu)
    {
        menu.vanishIfMouseDistant = false;

        var size = menu.InitialSize;
        const float margin = 24f;
        var desiredX = (float)UI.screenWidth * 0.22f;
        var desiredY = 48f;
        var x = Mathf.Clamp(desiredX, margin, Mathf.Max(margin, (float)UI.screenWidth - size.x - margin));
        var y = Mathf.Clamp(desiredY, margin, Mathf.Max(margin, (float)UI.screenHeight - size.y - margin));
        menu.windowRect = new Rect(x, y, size.x, size.y);
    }
}
