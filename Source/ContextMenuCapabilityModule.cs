using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class ContextMenuCapabilityModule
{
    public object GetAchtungState()
    {
        return AchtungIntegration.DescribeState();
    }

    public object SetAchtungShowDraftedOrdersWhenUndrafted(bool enabled)
    {
        if (!AchtungIntegration.IsLoaded())
            return new { success = false, message = "Achtung is not loaded." };

        var value = AchtungIntegration.SetShowDraftedOrdersWhenUndrafted(enabled);
        return new
        {
            success = true,
            loaded = true,
            showDraftedOrdersWhenUndrafted = value
        };
    }

    public object OpenContextMenu(string targetPawnName = null, int x = 0, int z = 0, string mode = "auto")
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
        var normalizedMode = (mode ?? "auto").Trim().ToLowerInvariant();
        if (normalizedMode != "auto" && normalizedMode != "achtung" && normalizedMode != "vanilla")
        {
            return new
            {
                success = false,
                message = $"Unsupported context menu mode '{mode}'. Use auto, achtung, or vanilla."
            };
        }

        FloatMenu menu;
        List<FloatMenuOption> options;
        string provider;

        if (normalizedMode == "achtung" || (normalizedMode == "auto" && AchtungIntegration.IsLoaded()))
        {
            if (!AchtungIntegration.IsLoaded())
                return new { success = false, message = "Achtung is not loaded, so an Achtung context menu is unavailable." };

            (menu, options) = AchtungIntegration.BuildMenu(clickPos);
            provider = "achtung";
        }
        else
        {
            var vanillaOptions = FloatMenuMakerMap.GetOptions(selectedPawns, clickPos, out _);
            menu = new FloatMenu(vanillaOptions) { givesColonistOrders = true };
            options = vanillaOptions.ToList();
            provider = "vanilla";
        }

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
        var snapshot = RimBridgeContextMenus.Current;
        if (snapshot == null || snapshot.Menu == null)
            return new { success = false, message = "No debug context menu is available." };
        if (Find.WindowStack.FloatMenu != snapshot.Menu)
        {
            RimBridgeContextMenus.Clear();
            return new { success = false, message = "No debug context menu is available." };
        }

        FloatMenuOption option = null;
        var resolvedIndex = -1;

        if (optionIndex > 0)
        {
            if (optionIndex > snapshot.Options.Count)
                return new { success = false, message = $"Option index {optionIndex} is out of range for a menu with {snapshot.Options.Count} options." };

            resolvedIndex = optionIndex;
            option = snapshot.Options[optionIndex - 1];
        }
        else
        {
            if (string.IsNullOrWhiteSpace(label))
                return new { success = false, message = "Either optionIndex or label must be provided." };

            var exactMatches = snapshot.Options
                .Select((candidate, index) => new { candidate, index })
                .Where(item => string.Equals(item.candidate.Label, label, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exactMatches.Count == 1)
            {
                option = exactMatches[0].candidate;
                resolvedIndex = exactMatches[0].index + 1;
            }
            else if (exactMatches.Count > 1)
            {
                return new { success = false, message = $"Label '{label}' is ambiguous within the current menu." };
            }
            else
            {
                var partialMatches = snapshot.Options
                    .Select((candidate, index) => new { candidate, index })
                    .Where(item => item.candidate.Label.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                if (partialMatches.Count != 1)
                {
                    return new { success = false, message = $"Could not resolve menu label '{label}' to a single option." };
                }

                option = partialMatches[0].candidate;
                resolvedIndex = partialMatches[0].index + 1;
            }
        }

        if (option.Disabled)
            return new { success = false, message = $"Menu option {resolvedIndex} is disabled.", label = option.Label };
        if (option.action == null)
            return new { success = false, message = $"Menu option {resolvedIndex} has no executable action.", label = option.Label };

        option.Chosen(snapshot.Menu.givesColonistOrders, snapshot.Menu);
        RimBridgeContextMenus.Clear();

        return new
        {
            success = true,
            executedIndex = resolvedIndex,
            label = option.Label
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
