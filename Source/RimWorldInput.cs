using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    public bool MainTabOpen { get; set; }

    public string OpenMainTabId { get; set; }

    public string OpenMainTabType { get; set; }

    public string OpenMainTabLabel { get; set; }

    public UiMainTabSnapshot MainTab { get; set; }

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

    public static object OpenWindowByTypeResponse(string windowType, bool replaceExisting = true)
    {
        return ToToolResponse(OpenWindowByType(windowType, replaceExisting));
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
        var mainTab = RimWorldMainTabs.GetOpenMainTabSnapshot();

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
            MainTabOpen = mainTab != null,
            OpenMainTabId = mainTab?.Id,
            OpenMainTabType = mainTab?.Type,
            OpenMainTabLabel = mainTab?.Label,
            MainTab = mainTab,
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

    public static UiCommandResult OpenWindowByType(string windowType, bool replaceExisting = true)
    {
        var before = GetUiState();
        var windowStack = Find.WindowStack;
        if (windowStack == null)
        {
            return new UiCommandResult
            {
                Success = false,
                Command = "open_window_by_type",
                Message = "RimWorld window stack is not available.",
                Before = before,
                After = before
            };
        }

        var trimmed = windowType?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new UiCommandResult
            {
                Success = false,
                Command = "open_window_by_type",
                Message = "A short or full .NET type name is required.",
                Before = before,
                After = before
            };
        }

        if (!TryResolveWindowType(trimmed, out var resolvedType, out var resolutionError))
        {
            return new UiCommandResult
            {
                Success = false,
                Command = "open_window_by_type",
                Message = resolutionError,
                Before = before,
                After = before
            };
        }

        var existingWindows = windowStack.Windows?
            .OfType<Window>()
            .Where(window => window.GetType() == resolvedType)
            .ToList() ?? [];

        if (existingWindows.Count > 0 && !replaceExisting)
        {
            return new UiCommandResult
            {
                Success = true,
                Command = "open_window_by_type",
                Changed = false,
                WindowCountDelta = 0,
                Message = $"Window '{resolvedType.FullName ?? resolvedType.Name}' is already open.",
                Before = before,
                After = before
            };
        }

        foreach (var existing in existingWindows)
            windowStack.TryRemove(existing, doCloseSound: false);

        Window createdWindow;
        try
        {
            createdWindow = Activator.CreateInstance(resolvedType) as Window;
            if (createdWindow == null)
                throw new InvalidOperationException($"Type '{resolvedType.FullName ?? resolvedType.Name}' did not create a Verse.Window instance.");
        }
        catch (Exception ex)
        {
            var message = ex is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException.Message
                : ex.Message;
            return new UiCommandResult
            {
                Success = false,
                Command = "open_window_by_type",
                Message = $"Opening window type '{resolvedType.FullName ?? resolvedType.Name}' failed: {message}",
                Before = before,
                After = before
            };
        }

        windowStack.Add(createdWindow);
        var after = GetUiState();
        return BuildCommandResult("open_window_by_type", before, after, $"Opened RimWorld window '{resolvedType.FullName ?? resolvedType.Name}'.");
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
            case ScreenTargetKind.MainTab:
                return BuildFailedScreenTargetResult(
                    before,
                    targetId,
                    "main_tab",
                    string.Empty,
                    "Main-tab targets are descriptive only. Use rimworld/open_main_tab or rimworld/close_main_tab for navigation.");
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
            || before.MainTabOpen != after.MainTabOpen
            || before.OpenMainTabId != after.OpenMainTabId
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

    private static bool TryResolveWindowType(string windowType, out Type resolvedType, out string error)
    {
        resolvedType = null;
        error = string.Empty;

        var matches = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly != null && !assembly.IsDynamic)
            .SelectMany(SafeGetTypes)
            .Where(type =>
                type != null
                && typeof(Window).IsAssignableFrom(type)
                && !type.IsAbstract
                && type.GetConstructor(Type.EmptyTypes) != null
                && (string.Equals(type.Name, windowType, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type.FullName, windowType, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

        if (matches.Count == 1)
        {
            resolvedType = matches[0];
            return true;
        }

        if (matches.Count > 1)
        {
            error = $"Window type query '{windowType}' matched multiple loaded types: {string.Join(", ", matches.Select(type => type.FullName ?? type.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}. Use the full .NET type name.";
            return false;
        }

        error = $"Could not resolve a loaded Verse.Window type matching '{windowType}' with a public parameterless constructor.";
        return false;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null);
        }
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
            mainTabOpen = state.MainTabOpen,
            openMainTabId = state.OpenMainTabId,
            openMainTabType = state.OpenMainTabType,
            openMainTabLabel = state.OpenMainTabLabel,
            mainTab = state.MainTab == null ? null : ToToolResponse(state.MainTab),
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

    private static object ToToolResponse(UiMainTabSnapshot tab)
    {
        return new
        {
            targetId = tab.Id,
            defName = tab.DefName,
            label = tab.Label,
            type = tab.Type,
            order = tab.Order,
            isOpen = tab.IsOpen,
            visible = tab.Visible,
            disabled = tab.Disabled,
            validWithoutMap = tab.ValidWithoutMap,
            minimized = tab.Minimized,
            rect = ToToolResponse(tab.Rect)
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
