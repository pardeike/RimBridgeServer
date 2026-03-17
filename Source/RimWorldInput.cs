using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal sealed class UiWindowSnapshot
{
    public int Index { get; set; }

    public int Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; }

    public string Layer { get; set; } = string.Empty;

    public bool IsOpen { get; set; }

    public bool GetsInput { get; set; }

    public bool CloseOnAccept { get; set; }

    public bool CloseOnCancel { get; set; }

    public bool ForcePause { get; set; }

    public bool PreventCameraMotion { get; set; }

    public bool AbsorbInputAroundWindow { get; set; }

    public bool DrawInScreenshotMode { get; set; }

    public string SemanticKind { get; set; } = string.Empty;

    public object SemanticDetails { get; set; }

    public UiRectSnapshot Rect { get; set; } = new();
}

internal sealed class UiRectSnapshot
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }
}

internal sealed class UiStateSnapshot
{
    public bool Success { get; set; }

    public string ProgramState { get; set; } = string.Empty;

    public bool InEntryScene { get; set; }

    public bool HasCurrentGame { get; set; }

    public bool NonImmediateDialogWindowOpen { get; set; }

    public bool CurrentWindowGetsInput { get; set; }

    public bool MouseObscuredNow { get; set; }

    public bool WindowsForcePause { get; set; }

    public bool WindowsPreventCameraMotion { get; set; }

    public bool WindowsPreventSave { get; set; }

    public bool AnyWindowAbsorbingAllInput { get; set; }

    public bool AnySearchWidgetFocused { get; set; }

    public bool FloatMenuOpen { get; set; }

    public int WindowCount { get; set; }

    public string FocusedWindowType { get; set; }

    public string FocusedWindowTitle { get; set; }

    public string TopWindowType { get; set; }

    public string TopWindowTitle { get; set; }

    public List<UiWindowSnapshot> Windows { get; set; } = [];
}

internal class UiCommandResult
{
    public bool Success { get; set; }

    public string Command { get; set; } = string.Empty;

    public bool Changed { get; set; }

    public int WindowCountDelta { get; set; }

    public string Message { get; set; } = string.Empty;

    public UiStateSnapshot Before { get; set; }

    public UiStateSnapshot After { get; set; }

    public List<string> OpenedWindowTypes { get; set; } = [];

    public List<string> ClosedWindowTypes { get; set; } = [];
}

internal sealed class ScreenTargetCommandResult : UiCommandResult
{
    public string TargetId { get; set; } = string.Empty;

    public string TargetKind { get; set; } = string.Empty;

    public string ActionKind { get; set; } = string.Empty;

    public int ExecutedOptionIndex { get; set; }

    public string ExecutedLabel { get; set; }
}

internal static class RimWorldInput
{
    public static object GetUiStateResponse()
    {
        return DescribeUiState(GetUiState());
    }

    public static object PressAcceptResponse()
    {
        return ToToolResponse(PressAccept());
    }

    public static object PressCancelResponse()
    {
        return ToToolResponse(PressCancel());
    }

    public static object CloseWindowResponse(string windowType = null)
    {
        return ToToolResponse(CloseWindow(windowType));
    }

    public static object ClickScreenTargetResponse(string targetId)
    {
        return ToToolResponse(ClickScreenTarget(targetId));
    }

    public static UiStateSnapshot GetUiState()
    {
        var windowStack = Find.WindowStack;
        var windows = windowStack?.Windows?
            .OfType<Window>()
            .Select((window, index) => DescribeWindow(windowStack, window, index))
            .ToList() ?? [];

        var focusedWindow = windowStack?.focusedWindow;
        var topWindow = windows.LastOrDefault();

        return new UiStateSnapshot
        {
            Success = true,
            ProgramState = Current.ProgramState.ToString(),
            InEntryScene = GenScene.InEntryScene,
            HasCurrentGame = Current.Game != null,
            NonImmediateDialogWindowOpen = windowStack?.NonImmediateDialogWindowOpen ?? false,
            CurrentWindowGetsInput = windowStack?.CurrentWindowGetsInput ?? false,
            MouseObscuredNow = windowStack?.MouseObscuredNow ?? false,
            WindowsForcePause = windowStack?.WindowsForcePause ?? false,
            WindowsPreventCameraMotion = windowStack?.WindowsPreventCameraMotion ?? false,
            WindowsPreventSave = windowStack?.WindowsPreventSave ?? false,
            AnyWindowAbsorbingAllInput = windowStack?.AnyWindowAbsorbingAllInput ?? false,
            AnySearchWidgetFocused = windowStack?.AnySearchWidgetFocused ?? false,
            FloatMenuOpen = windowStack?.FloatMenu != null,
            WindowCount = windows.Count,
            FocusedWindowType = focusedWindow?.GetType().FullName,
            FocusedWindowTitle = GetWindowTitle(focusedWindow),
            TopWindowType = topWindow?.Type,
            TopWindowTitle = topWindow?.Title,
            Windows = windows
        };
    }

    public static UiCommandResult PressAccept()
    {
        return DispatchWindowCommand("accept", stack => stack.Notify_PressedAccept());
    }

    public static UiCommandResult PressCancel()
    {
        return DispatchWindowCommand("cancel", stack => stack.Notify_PressedCancel());
    }

    public static UiCommandResult CloseWindow(string windowType = null)
    {
        var before = GetUiState();
        var windowStack = Find.WindowStack;
        if (windowStack == null)
        {
            return new UiCommandResult
            {
                Success = false,
                Command = "close_window",
                Message = "RimWorld window stack is not available.",
                Before = before,
                After = before
            };
        }

        var target = ResolveWindow(windowStack, windowType);
        if (target == null)
        {
            return new UiCommandResult
            {
                Success = false,
                Command = "close_window",
                Message = string.IsNullOrWhiteSpace(windowType)
                    ? "No open window was available to close."
                    : $"Could not find an open window matching '{windowType}'.",
                Before = before,
                After = before
            };
        }

        windowStack.TryRemove(target, doCloseSound: false);
        var after = GetUiState();
        var targetType = target.GetType().FullName ?? target.GetType().Name;
        return BuildCommandResult("close_window", before, after, $"Closed RimWorld window '{targetType}'.");
    }

    public static ScreenTargetCommandResult ClickScreenTarget(string targetId)
    {
        var before = GetUiState();
        if (!ScreenTargetIds.TryParse(targetId, out var target))
        {
            return BuildFailedScreenTargetResult(
                before,
                targetId,
                string.Empty,
                string.Empty,
                $"Target id '{targetId}' is not a supported screen target identifier.");
        }

        switch (target.Kind)
        {
            case ScreenTargetKind.ContextMenuOption:
                return ClickContextMenuOptionTarget(before, targetId, target);
            case ScreenTargetKind.WindowDismiss:
                return ClickWindowDismissTarget(before, targetId, target);
            case ScreenTargetKind.Window:
                return BuildFailedScreenTargetResult(
                    before,
                    targetId,
                    "window",
                    string.Empty,
                    "Window body targets are descriptive only. Use a dismissTargetId or another actionable target id.");
            default:
                return BuildFailedScreenTargetResult(
                    before,
                    targetId,
                    target.Kind.ToString(),
                    string.Empty,
                    $"Target id '{targetId}' is not actionable.");
        }
    }

    private static UiCommandResult DispatchWindowCommand(string command, Action<WindowStack> dispatch)
    {
        var before = GetUiState();
        var windowStack = Find.WindowStack;
        if (windowStack == null)
        {
            return new UiCommandResult
            {
                Success = false,
                Command = command,
                Message = "RimWorld window stack is not available.",
                Before = before,
                After = before
            };
        }

        dispatch(windowStack);
        var after = GetUiState();
        return BuildCommandResult(command, before, after, $"Dispatched semantic '{command}' input to RimWorld's window stack.");
    }

    private static ScreenTargetCommandResult ClickContextMenuOptionTarget(UiStateSnapshot before, string targetId, ScreenTargetReference target)
    {
        var execution = RimWorldContextMenuActions.ExecuteOption(optionIndex: target.OptionIndex, expectedMenuId: target.MenuId);
        if (!execution.Success)
        {
            return BuildFailedScreenTargetResult(
                before,
                targetId,
                "context_menu_option",
                "execute_context_menu_option",
                execution.Message);
        }

        var after = GetUiState();
        var result = BuildScreenTargetCommandResult(
            before,
            after,
            targetId,
            "context_menu_option",
            "execute_context_menu_option",
            execution.Message);
        result.ExecutedOptionIndex = execution.ResolvedIndex;
        result.ExecutedLabel = execution.Label;
        return result;
    }

    private static ScreenTargetCommandResult ClickWindowDismissTarget(UiStateSnapshot before, string targetId, ScreenTargetReference target)
    {
        var windowStack = Find.WindowStack;
        if (windowStack == null)
        {
            return BuildFailedScreenTargetResult(
                before,
                targetId,
                "window_dismiss",
                "dismiss_window",
                "RimWorld window stack is not available.");
        }

        var window = ResolveWindow(windowStack, target.WindowId, target.WindowType);
        if (window == null)
        {
            return BuildFailedScreenTargetResult(
                before,
                targetId,
                "window_dismiss",
                "dismiss_window",
                $"Could not find an open window matching target id '{targetId}'.");
        }

        windowStack.TryRemove(window, doCloseSound: false);
        if (ReferenceEquals(RimBridgeContextMenus.Current?.Menu, window))
            RimBridgeContextMenus.Clear();

        var after = GetUiState();
        return BuildScreenTargetCommandResult(
            before,
            after,
            targetId,
            "window_dismiss",
            "dismiss_window",
            $"Dismissed RimWorld window '{target.WindowType}' ({target.WindowId}).");
    }

    private static UiCommandResult BuildCommandResult(string command, UiStateSnapshot before, UiStateSnapshot after, string message)
    {
        var beforeIds = new HashSet<string>(before.Windows.Select(BuildIdentity), StringComparer.Ordinal);
        var afterIds = new HashSet<string>(after.Windows.Select(BuildIdentity), StringComparer.Ordinal);

        var openedWindowTypes = after.Windows
            .Where(window => beforeIds.Contains(BuildIdentity(window)) == false)
            .Select(window => window.Type)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var closedWindowTypes = before.Windows
            .Where(window => afterIds.Contains(BuildIdentity(window)) == false)
            .Select(window => window.Type)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var changed = before.WindowCount != after.WindowCount
            || before.NonImmediateDialogWindowOpen != after.NonImmediateDialogWindowOpen
            || before.CurrentWindowGetsInput != after.CurrentWindowGetsInput
            || before.FocusedWindowType != after.FocusedWindowType
            || openedWindowTypes.Count > 0
            || closedWindowTypes.Count > 0;

        return new UiCommandResult
        {
            Success = true,
            Command = command,
            Changed = changed,
            WindowCountDelta = after.WindowCount - before.WindowCount,
            Message = changed ? message : message + " UI state did not change.",
            Before = before,
            After = after,
            OpenedWindowTypes = openedWindowTypes,
            ClosedWindowTypes = closedWindowTypes
        };
    }

    private static ScreenTargetCommandResult BuildScreenTargetCommandResult(
        UiStateSnapshot before,
        UiStateSnapshot after,
        string targetId,
        string targetKind,
        string actionKind,
        string message)
    {
        var baseResult = BuildCommandResult("click_screen_target", before, after, message);
        return new ScreenTargetCommandResult
        {
            Success = baseResult.Success,
            Command = baseResult.Command,
            Changed = baseResult.Changed,
            WindowCountDelta = baseResult.WindowCountDelta,
            Message = baseResult.Message,
            Before = baseResult.Before,
            After = baseResult.After,
            OpenedWindowTypes = baseResult.OpenedWindowTypes,
            ClosedWindowTypes = baseResult.ClosedWindowTypes,
            TargetId = targetId ?? string.Empty,
            TargetKind = targetKind ?? string.Empty,
            ActionKind = actionKind ?? string.Empty
        };
    }

    private static ScreenTargetCommandResult BuildFailedScreenTargetResult(
        UiStateSnapshot before,
        string targetId,
        string targetKind,
        string actionKind,
        string message)
    {
        return new ScreenTargetCommandResult
        {
            Success = false,
            Command = "click_screen_target",
            Changed = false,
            WindowCountDelta = 0,
            Message = message,
            Before = before,
            After = before,
            TargetId = targetId ?? string.Empty,
            TargetKind = targetKind ?? string.Empty,
            ActionKind = actionKind ?? string.Empty
        };
    }

    private static UiWindowSnapshot DescribeWindow(WindowStack windowStack, Window window, int index)
    {
        var semanticKind = string.Empty;
        object semanticDetails = null;
        RimWorldModSettings.TryDescribeWindow(window, out semanticKind, out semanticDetails);

        return new UiWindowSnapshot
        {
            Index = index,
            Id = window.ID,
            Type = window.GetType().FullName ?? window.GetType().Name,
            Title = GetWindowTitle(window),
            Layer = window.layer.ToString(),
            IsOpen = window.IsOpen,
            GetsInput = windowStack.GetsInput(window),
            CloseOnAccept = window.closeOnAccept,
            CloseOnCancel = window.closeOnCancel,
            ForcePause = window.forcePause,
            PreventCameraMotion = window.preventCameraMotion,
            AbsorbInputAroundWindow = window.absorbInputAroundWindow,
            DrawInScreenshotMode = window.drawInScreenshotMode,
            SemanticKind = semanticKind,
            SemanticDetails = semanticDetails,
            Rect = new UiRectSnapshot
            {
                X = window.windowRect.x,
                Y = window.windowRect.y,
                Width = window.windowRect.width,
                Height = window.windowRect.height
            }
        };
    }

    private static string BuildIdentity(UiWindowSnapshot window)
    {
        return window.Type + "#" + window.Id;
    }

    private static string GetWindowTitle(Window window)
    {
        if (window == null || string.IsNullOrWhiteSpace(window.optionalTitle))
            return null;

        return window.optionalTitle;
    }

    private static Window ResolveWindow(WindowStack windowStack, string windowType)
    {
        var windows = windowStack.Windows?.OfType<Window>() ?? Enumerable.Empty<Window>();
        if (string.IsNullOrWhiteSpace(windowType))
            return windows.LastOrDefault();

        var trimmed = windowType.Trim();
        return windows
            .Where(window =>
            {
                var type = window.GetType();
                return string.Equals(type.Name, trimmed, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type.FullName, trimmed, StringComparison.OrdinalIgnoreCase);
            })
            .LastOrDefault();
    }

    private static Window ResolveWindow(WindowStack windowStack, int windowId, string windowType)
    {
        return windowStack.Windows?
            .OfType<Window>()
            .LastOrDefault(window =>
            {
                var type = window.GetType();
                var fullName = type.FullName ?? type.Name;
                return window.ID == windowId
                    && string.Equals(fullName, windowType, StringComparison.Ordinal);
            });
    }

    internal static object DescribeUiState(UiStateSnapshot state)
    {
        return new
        {
            success = state.Success,
            programState = state.ProgramState,
            inEntryScene = state.InEntryScene,
            hasCurrentGame = state.HasCurrentGame,
            nonImmediateDialogWindowOpen = state.NonImmediateDialogWindowOpen,
            currentWindowGetsInput = state.CurrentWindowGetsInput,
            mouseObscuredNow = state.MouseObscuredNow,
            windowsForcePause = state.WindowsForcePause,
            windowsPreventCameraMotion = state.WindowsPreventCameraMotion,
            windowsPreventSave = state.WindowsPreventSave,
            anyWindowAbsorbingAllInput = state.AnyWindowAbsorbingAllInput,
            anySearchWidgetFocused = state.AnySearchWidgetFocused,
            floatMenuOpen = state.FloatMenuOpen,
            windowCount = state.WindowCount,
            focusedWindowType = state.FocusedWindowType,
            focusedWindowTitle = state.FocusedWindowTitle,
            topWindowType = state.TopWindowType,
            topWindowTitle = state.TopWindowTitle,
            windows = state.Windows.Select(ToToolResponse).ToList()
        };
    }

    private static object ToToolResponse(UiWindowSnapshot window)
    {
        return new
        {
            index = window.Index,
            id = window.Id,
            type = window.Type,
            title = window.Title,
            layer = window.Layer,
            isOpen = window.IsOpen,
            getsInput = window.GetsInput,
            closeOnAccept = window.CloseOnAccept,
            closeOnCancel = window.CloseOnCancel,
            forcePause = window.ForcePause,
            preventCameraMotion = window.PreventCameraMotion,
            absorbInputAroundWindow = window.AbsorbInputAroundWindow,
            drawInScreenshotMode = window.DrawInScreenshotMode,
            semanticKind = string.IsNullOrWhiteSpace(window.SemanticKind) ? null : window.SemanticKind,
            semanticDetails = window.SemanticDetails,
            rect = ToToolResponse(window.Rect)
        };
    }

    private static object ToToolResponse(UiRectSnapshot rect)
    {
        return new
        {
            x = rect.X,
            y = rect.Y,
            width = rect.Width,
            height = rect.Height
        };
    }

    private static object ToToolResponse(UiCommandResult result)
    {
        return new
        {
            success = result.Success,
            command = result.Command,
            changed = result.Changed,
            windowCountDelta = result.WindowCountDelta,
            message = result.Message,
            before = DescribeUiState(result.Before),
            after = DescribeUiState(result.After),
            openedWindowTypes = result.OpenedWindowTypes.ToList(),
            closedWindowTypes = result.ClosedWindowTypes.ToList()
        };
    }

    private static object ToToolResponse(ScreenTargetCommandResult result)
    {
        return new
        {
            success = result.Success,
            command = result.Command,
            changed = result.Changed,
            windowCountDelta = result.WindowCountDelta,
            message = result.Message,
            targetId = result.TargetId,
            targetKind = result.TargetKind,
            actionKind = result.ActionKind,
            executedOptionIndex = result.ExecutedOptionIndex > 0 ? result.ExecutedOptionIndex : (int?)null,
            executedLabel = result.ExecutedLabel,
            before = DescribeUiState(result.Before),
            after = DescribeUiState(result.After),
            openedWindowTypes = result.OpenedWindowTypes.ToList(),
            closedWindowTypes = result.ClosedWindowTypes.ToList()
        };
    }
}
