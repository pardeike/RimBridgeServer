using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class ContextMenuCapabilityModule
{
    private sealed class MapClickTarget
    {
        public IntVec3 Cell { get; set; } = IntVec3.Invalid;

        public string Label { get; set; } = string.Empty;
    }

    public object OpenContextMenu(string targetPawnName = null, int x = 0, int z = 0, string mode = "vanilla")
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded." };

        var selectedPawns = Find.Selector.SelectedPawns.ToList();
        if (selectedPawns.Count == 0)
            return new { success = false, message = "No pawns are currently selected." };

        var map = RimWorldState.CurrentMapOrThrow();
        if (!TryResolveMapClickTarget(map, targetPawnName, x, z, out var target, out var failure))
            return failure;
        var clickCell = target.Cell;
        var targetLabel = target.Label;

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

    public object RightClickCell(string targetPawnName = null, int x = 0, int z = 0)
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded." };

        var selectedPawns = Find.Selector.SelectedPawns.ToList();
        if (selectedPawns.Count == 0)
            return new { success = false, message = "No pawns are currently selected." };

        var map = RimWorldState.CurrentMapOrThrow();
        if (!TryResolveMapClickTarget(map, targetPawnName, x, z, out var target, out var failure))
            return failure;

        if (Find.WindowStack.FloatMenu != null)
            Find.WindowStack.TryRemove(Find.WindowStack.FloatMenu, doCloseSound: false);

        var clickPos = RimWorldState.CellCenter(target.Cell);
        List<FloatMenuOption> options = null;
        FloatMenuContext context = null;
        try
        {
            options = FloatMenuMakerMap.GetOptions(selectedPawns, clickPos, out context).ToList();
        }
        catch (System.Exception ex)
        {
            RimBridgeContextMenus.Clear();
            return new
            {
                success = false,
                message = $"RimWorld failed while generating right-click options: {ex.Message}"
            };
        }

        if (TryExecuteMultiPawnGoto(context, selectedPawns, out var gotoResponse))
        {
            RimBridgeContextMenus.Clear();
            return gotoResponse;
        }

        if (!options.NullOrEmpty())
        {
            var autoTakeOption = FloatMenuMakerMap.GetAutoTakeOption(options);
            if (autoTakeOption != null)
            {
                autoTakeOption.Chosen(colonistOrdering: true, null);
                RimBridgeContextMenus.Clear();
                return new
                {
                    success = true,
                    actionKind = "auto_take",
                    clickCell = new { x = target.Cell.x, z = target.Cell.z },
                    target = target.Label,
                    selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
                    label = autoTakeOption.Label,
                    message = $"Executed the default right-click action '{autoTakeOption.Label}'."
                };
            }

            var title = context != null && context.IsMultiselect == false
                ? context.FirstSelectedPawn?.LabelCap.ToString()
                : null;
            var menu = new FloatMenuMap(options, title, clickPos) { givesColonistOrders = true };
            Find.WindowStack.Add(menu);
            PositionDebugMenu(menu);
            var snapshot = RimBridgeContextMenus.Store("vanilla", menu, options, target.Cell, target.Label);

            return new
            {
                success = true,
                actionKind = "menu_opened",
                menuId = snapshot.Id,
                provider = snapshot.Provider,
                clickCell = new { x = target.Cell.x, z = target.Cell.z },
                target = target.Label,
                selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
                optionCount = options.Count,
                options = DescribeOptions(snapshot.Options),
                message = "Right-click opened a context menu because no default action was available."
            };
        }

        RimBridgeContextMenus.Clear();
        return new
        {
            success = true,
            actionKind = "no_action",
            clickCell = new { x = target.Cell.x, z = target.Cell.z },
            target = target.Label,
            selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
            message = "No right-click action was available for the current selection and target."
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

    private static bool TryResolveMapClickTarget(Map map, string targetPawnName, int x, int z, out MapClickTarget target, out object failure)
    {
        target = null;
        failure = null;

        if (!string.IsNullOrWhiteSpace(targetPawnName))
        {
            var targetPawn = RimWorldState.ResolveCurrentMapPawn(targetPawnName);
            if (!targetPawn.Spawned || targetPawn.Map != map)
            {
                failure = new { success = false, message = $"Pawn '{targetPawnName}' is not spawned on the current map." };
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

    private static bool TryExecuteMultiPawnGoto(FloatMenuContext context, List<Pawn> selectedPawns, out object response)
    {
        response = null;
        if (context == null || !context.IsMultiselect)
            return false;

        var draftedGotoPawns = new List<Pawn>();
        foreach (var pawn in context.allSelectedPawns)
        {
            if ((bool)FloatMenuMakerMap.ShouldGenerateFloatMenuForPawn(pawn) && pawn.Drafted)
                draftedGotoPawns.Add(pawn);
        }

        if (draftedGotoPawns.Count == 0)
            return false;

        if (draftedGotoPawns.Count == 1)
        {
            if (!(bool)FloatMenuOptionProvider_DraftedMove.PawnCanGoto(draftedGotoPawns[0], context.ClickedCell))
                return false;

            FloatMenuOptionProvider_DraftedMove.PawnGotoAction(context.ClickedCell, draftedGotoPawns[0], context.ClickedCell);
            response = new
            {
                success = true,
                actionKind = "single_pawn_goto",
                clickCell = new { x = context.ClickedCell.x, z = context.ClickedCell.z },
                target = $"cell {context.ClickedCell.x},{context.ClickedCell.z}",
                selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
                orderedPawnCount = 1,
                message = "Executed RimWorld's drafted single-pawn right-click goto."
            };
            return true;
        }

        var destination = CellFinder.StandableCellNear(context.ClickedCell, context.map, 2.9f);
        if (!destination.IsValid)
            return false;

        var controller = Find.Selector.gotoController;
        controller.StartInteraction(destination);
        foreach (var pawn in draftedGotoPawns)
            controller.AddPawn(pawn);
        controller.FinalizeInteraction();

        response = new
        {
            success = true,
            actionKind = "multi_pawn_goto",
            clickCell = new { x = context.ClickedCell.x, z = context.ClickedCell.z },
            target = $"cell {context.ClickedCell.x},{context.ClickedCell.z}",
            selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
            orderedPawnCount = draftedGotoPawns.Count,
            destinationCell = new { x = destination.x, z = destination.z },
            message = "Executed RimWorld's grouped drafted right-click goto."
        };
        return true;
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
