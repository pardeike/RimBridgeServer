using System;
using System.Collections.Generic;
using System.Linq;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldTargeting
{
    internal sealed class ScreenTargetClipArea
    {
        public string TargetId { get; set; } = string.Empty;

        public string TargetKind { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public UiRectSnapshot Rect { get; set; } = new();
    }

    public static object GetScreenTargetsResponse()
    {
        return new
        {
            success = true,
            targets = CreateScreenTargetsPayload()
        };
    }

    public static object CreateScreenTargetsPayload()
    {
        var uiState = RimWorldInput.GetUiState();
        var topWindowType = uiState.TopWindowType;
        var focusedWindowType = uiState.FocusedWindowType;

        return new
        {
            capturedAtUtc = DateTime.UtcNow,
            uiState = new
            {
                programState = uiState.ProgramState,
                inEntryScene = uiState.InEntryScene,
                hasCurrentGame = uiState.HasCurrentGame,
                windowCount = uiState.WindowCount,
                floatMenuOpen = uiState.FloatMenuOpen,
                nonImmediateDialogWindowOpen = uiState.NonImmediateDialogWindowOpen,
                mainTabOpen = uiState.MainTabOpen,
                openMainTabId = uiState.OpenMainTabId,
                openMainTabType = uiState.OpenMainTabType,
                focusedWindowType = focusedWindowType,
                topWindowType = topWindowType
            },
            camera = TryDescribeCamera(),
            selectedPawns = Find.Selector.SelectedPawns.Select(RimWorldState.DescribePawn).ToList(),
            mainTab = CreateMainTabPayload(uiState.MainTab),
            windows = uiState.Windows.Select(window => CreateWindowPayload(window, topWindowType, focusedWindowType)).ToList(),
            contextMenu = CreateContextMenuPayload()
        };
    }

    public static bool TryResolveClipArea(string targetId, out ScreenTargetClipArea clipArea, out string error)
    {
        clipArea = null;
        error = string.Empty;

        if (!ScreenTargetIds.TryParse(targetId, out var target))
        {
            error = $"Target id '{targetId}' is not a supported screen target identifier.";
            return false;
        }

        switch (target.Kind)
        {
            case ScreenTargetKind.Window:
            case ScreenTargetKind.WindowDismiss:
                return TryResolveWindowClipArea(target, out clipArea, out error);
            case ScreenTargetKind.ContextMenuOption:
                return TryResolveContextMenuOptionClipArea(target, out clipArea, out error);
            case ScreenTargetKind.MainTab:
                return TryResolveMainTabClipArea(target, out clipArea, out error);
            default:
                error = $"Target id '{targetId}' is not clip-capable.";
                return false;
        }
    }

    private static object CreateContextMenuPayload()
    {
        var snapshot = RimBridgeContextMenus.Current;
        if (snapshot?.Menu == null)
            return null;

        if (!ReferenceEquals(Find.WindowStack?.FloatMenu, snapshot.Menu))
            return null;

        var menu = snapshot.Menu;
        var menuWindowType = menu.GetType().FullName ?? menu.GetType().Name;
        var menuWindowTargetId = ScreenTargetIds.CreateWindowTargetId(menu.ID, menuWindowType);
        var dismissTargetId = ScreenTargetIds.CreateWindowDismissTargetId(menu.ID, menuWindowType);
        var optionRects = ComputeContextMenuOptionRects(menu, snapshot);

        var options = snapshot.Options.Select((option, index) =>
        {
            var rect = index < optionRects.Count ? optionRects[index] : null;
            return new
            {
                kind = "context_menu_option",
                targetId = ScreenTargetIds.CreateContextMenuOptionTargetId(snapshot.Id, index + 1),
                index = index + 1,
                label = option.Label,
                disabled = option.Disabled,
                hasAction = option.action != null,
                priority = option.Priority.ToString(),
                orderInPriority = option.orderInPriority,
                rect = rect == null ? null : new
                {
                    x = rect.X,
                    y = rect.Y,
                    width = rect.Width,
                    height = rect.Height
                }
            };
        }).ToList();

        return new
        {
            kind = "context_menu",
            menuId = snapshot.Id,
            windowTargetId = menuWindowTargetId,
            dismissTargetId = dismissTargetId,
            provider = snapshot.Provider,
            target = snapshot.TargetLabel,
            clickCell = new
            {
                x = snapshot.ClickCell.x,
                z = snapshot.ClickCell.z
            },
            menuRect = new
            {
                x = menu.windowRect.x,
                y = menu.windowRect.y,
                width = menu.windowRect.width,
                height = menu.windowRect.height
            },
            title = menu.title,
            columnCount = menu.ColumnCount,
            usingScrollbar = menu.UsingScrollbar,
            optionCount = options.Count,
            options
        };
    }

    private static object CreateWindowPayload(UiWindowSnapshot window, string topWindowType, string focusedWindowType)
    {
        var windowTargetId = ScreenTargetIds.CreateWindowTargetId(window.Id, window.Type);
        var dismissTargetId = CanDismissWindow(window)
            ? ScreenTargetIds.CreateWindowDismissTargetId(window.Id, window.Type)
            : null;

        return new
        {
            kind = "window",
            index = window.Index,
            id = window.Id,
            type = window.Type,
            title = window.Title,
            layer = window.Layer,
            isTopWindow = string.Equals(window.Type, topWindowType, StringComparison.Ordinal),
            isFocusedWindow = string.Equals(window.Type, focusedWindowType, StringComparison.Ordinal),
            getsInput = window.GetsInput,
            windowTargetId = windowTargetId,
            dismissTargetId = dismissTargetId,
            rect = CreateRectPayload(window.Rect)
        };
    }

    private static object CreateMainTabPayload(UiMainTabSnapshot mainTab)
    {
        if (mainTab == null || mainTab.IsOpen == false)
            return null;

        return new
        {
            kind = "main_tab",
            targetId = mainTab.Id,
            defName = mainTab.DefName,
            label = mainTab.Label,
            type = mainTab.Type,
            order = mainTab.Order,
            visible = mainTab.Visible,
            disabled = mainTab.Disabled,
            rect = CreateRectPayload(mainTab.Rect)
        };
    }

    private static object CreateRectPayload(UiRectSnapshot rect)
    {
        return new
        {
            x = rect.X,
            y = rect.Y,
            width = rect.Width,
            height = rect.Height
        };
    }

    private static object TryDescribeCamera()
    {
        try
        {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
                return null;

            return RimWorldState.DescribeCamera();
        }
        catch
        {
            return null;
        }
    }

    private static bool CanDismissWindow(UiWindowSnapshot window)
    {
        if (window == null || !window.IsOpen)
            return false;

        if (string.Equals(window.Type, typeof(ImmediateWindow).FullName, StringComparison.Ordinal))
            return false;

        return window.CloseOnAccept
            || window.CloseOnCancel
            || string.Equals(window.Layer, "Dialog", StringComparison.Ordinal)
            || string.Equals(window.Layer, "Super", StringComparison.Ordinal);
    }

    private static bool TryResolveWindowClipArea(ScreenTargetReference target, out ScreenTargetClipArea clipArea, out string error)
    {
        clipArea = null;
        error = string.Empty;

        var uiState = RimWorldInput.GetUiState();
        var window = uiState.Windows.LastOrDefault(candidate =>
            candidate.Id == target.WindowId
            && string.Equals(candidate.Type, target.WindowType, StringComparison.Ordinal));
        if (window == null)
        {
            error = $"Could not find an open window matching target id '{target.TargetId}'.";
            return false;
        }

        clipArea = new ScreenTargetClipArea
        {
            TargetId = target.TargetId,
            TargetKind = target.Kind == ScreenTargetKind.WindowDismiss ? "window_dismiss" : "window",
            Label = string.IsNullOrWhiteSpace(window.Title) ? window.Type : window.Title,
            Rect = new UiRectSnapshot
            {
                X = window.Rect.X,
                Y = window.Rect.Y,
                Width = window.Rect.Width,
                Height = window.Rect.Height
            }
        };
        return true;
    }

    private static bool TryResolveContextMenuOptionClipArea(ScreenTargetReference target, out ScreenTargetClipArea clipArea, out string error)
    {
        clipArea = null;
        error = string.Empty;

        var snapshot = RimBridgeContextMenus.Current;
        if (snapshot?.Menu == null || !ReferenceEquals(Find.WindowStack?.FloatMenu, snapshot.Menu))
        {
            error = $"Could not find an open context menu matching target id '{target.TargetId}'.";
            return false;
        }

        if (snapshot.Id != target.MenuId)
        {
            error = $"The active context menu does not match target id '{target.TargetId}'.";
            return false;
        }

        var optionRects = ComputeContextMenuOptionRects(snapshot.Menu, snapshot);
        if (target.OptionIndex <= 0 || target.OptionIndex > optionRects.Count || target.OptionIndex > snapshot.Options.Count)
        {
            error = $"Context menu option target '{target.TargetId}' is out of range.";
            return false;
        }

        var rect = optionRects[target.OptionIndex - 1];
        var option = snapshot.Options[target.OptionIndex - 1];
        clipArea = new ScreenTargetClipArea
        {
            TargetId = target.TargetId,
            TargetKind = "context_menu_option",
            Label = option.Label ?? string.Empty,
            Rect = new UiRectSnapshot
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height
            }
        };
        return true;
    }

    private static bool TryResolveMainTabClipArea(ScreenTargetReference target, out ScreenTargetClipArea clipArea, out string error)
    {
        clipArea = null;
        error = string.Empty;

        var mainTab = RimWorldMainTabs.GetOpenMainTabSnapshot();
        if (mainTab == null || mainTab.IsOpen == false)
        {
            error = $"Could not find an open main tab matching target id '{target.TargetId}'.";
            return false;
        }

        if (!string.Equals(mainTab.DefName, target.MainTabDefName, StringComparison.Ordinal))
        {
            error = $"The active main tab '{mainTab.DefName}' does not match target id '{target.TargetId}'.";
            return false;
        }

        clipArea = new ScreenTargetClipArea
        {
            TargetId = target.TargetId,
            TargetKind = "main_tab",
            Label = string.IsNullOrWhiteSpace(mainTab.Label) ? mainTab.DefName : mainTab.Label,
            Rect = new UiRectSnapshot
            {
                X = mainTab.Rect.X,
                Y = mainTab.Rect.Y,
                Width = mainTab.Rect.Width,
                Height = mainTab.Rect.Height
            }
        };
        return true;
    }

    private static IReadOnlyList<FloatMenuTargetRect> ComputeContextMenuOptionRects(FloatMenu menu, ContextMenuSnapshot snapshot)
    {
        return FloatMenuTargetLayoutCalculator.Compute(new FloatMenuTargetLayoutRequest
        {
            WindowX = menu.windowRect.x,
            WindowY = menu.windowRect.y,
            Margin = menu.Margin,
            ColumnWidth = menu.ColumnWidth,
            ColumnCount = Math.Max(menu.ColumnCount, 1),
            MaxViewHeight = menu.MaxViewHeight,
            OptionSpacing = FloatMenu.OptionSpacing,
            TitleHeight = 0f,
            OptionHeights = snapshot.Options.Select(option => option.RequiredHeight).ToList()
        });
    }
}
