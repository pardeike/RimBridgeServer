using System;
using System.Collections.Generic;
using System.Linq;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldTargeting
{
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
                focusedWindowType = focusedWindowType,
                topWindowType = topWindowType
            },
            camera = TryDescribeCamera(),
            selectedPawns = Find.Selector.SelectedPawns.Select(RimWorldState.DescribePawn).ToList(),
            windows = uiState.Windows.Select(window => CreateWindowPayload(window, topWindowType, focusedWindowType)).ToList(),
            contextMenu = CreateContextMenuPayload()
        };
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
        var optionRects = FloatMenuTargetLayoutCalculator.Compute(new FloatMenuTargetLayoutRequest
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
}
