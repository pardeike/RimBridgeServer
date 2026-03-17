using System;

namespace RimBridgeServer.Core;

public enum ScreenTargetKind
{
    Unknown = 0,
    Window = 1,
    WindowDismiss = 2,
    ContextMenuOption = 3,
    MainTab = 4
}

public sealed class ScreenTargetReference
{
    public string TargetId { get; set; } = string.Empty;

    public ScreenTargetKind Kind { get; set; }

    public int WindowId { get; set; }

    public string WindowType { get; set; } = string.Empty;

    public int MenuId { get; set; }

    public int OptionIndex { get; set; }

    public string MainTabDefName { get; set; } = string.Empty;
}

public static class ScreenTargetIds
{
    public static string CreateWindowTargetId(int windowId, string windowType)
    {
        return CreateWindowScopedId("window", windowId, windowType);
    }

    public static string CreateWindowDismissTargetId(int windowId, string windowType)
    {
        return CreateWindowScopedId("window-dismiss", windowId, windowType);
    }

    public static string CreateContextMenuOptionTargetId(int menuId, int optionIndex)
    {
        if (menuId <= 0)
            throw new ArgumentOutOfRangeException(nameof(menuId));
        if (optionIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(optionIndex));

        return "context-menu-option:" + menuId + ":" + optionIndex;
    }

    public static string CreateMainTabTargetId(string mainTabDefName)
    {
        if (string.IsNullOrWhiteSpace(mainTabDefName))
            throw new ArgumentException("Main tab defName is required.", nameof(mainTabDefName));

        return "main-tab:" + mainTabDefName.Trim();
    }

    public static bool TryParse(string targetId, out ScreenTargetReference target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        var segments = targetId.Split(':');
        if (string.Equals(segments[0], "main-tab", StringComparison.Ordinal)
            && segments.Length == 2
            && string.IsNullOrWhiteSpace(segments[1]) == false)
        {
            target = new ScreenTargetReference
            {
                TargetId = targetId,
                Kind = ScreenTargetKind.MainTab,
                MainTabDefName = segments[1]
            };
            return true;
        }

        if (segments.Length < 3)
            return false;

        if (string.Equals(segments[0], "window", StringComparison.Ordinal))
            return TryParseWindowScopedId(targetId, segments, ScreenTargetKind.Window, out target);

        if (string.Equals(segments[0], "window-dismiss", StringComparison.Ordinal))
            return TryParseWindowScopedId(targetId, segments, ScreenTargetKind.WindowDismiss, out target);

        if (string.Equals(segments[0], "context-menu-option", StringComparison.Ordinal)
            && segments.Length == 3
            && int.TryParse(segments[1], out var menuId)
            && int.TryParse(segments[2], out var optionIndex)
            && menuId > 0
            && optionIndex > 0)
        {
            target = new ScreenTargetReference
            {
                TargetId = targetId,
                Kind = ScreenTargetKind.ContextMenuOption,
                MenuId = menuId,
                OptionIndex = optionIndex
            };
            return true;
        }

        return false;
    }

    private static string CreateWindowScopedId(string prefix, int windowId, string windowType)
    {
        if (windowId < 0)
        {
            return prefix + ":" + windowId + ":" + RequireWindowType(windowType);
        }

        return prefix + ":" + windowId + ":" + RequireWindowType(windowType);
    }

    private static bool TryParseWindowScopedId(string targetId, string[] segments, ScreenTargetKind kind, out ScreenTargetReference target)
    {
        target = null;
        if (segments.Length != 3 || !int.TryParse(segments[1], out var windowId) || string.IsNullOrWhiteSpace(segments[2]))
            return false;

        target = new ScreenTargetReference
        {
            TargetId = targetId,
            Kind = kind,
            WindowId = windowId,
            WindowType = segments[2]
        };
        return true;
    }

    private static string RequireWindowType(string windowType)
    {
        if (string.IsNullOrWhiteSpace(windowType))
            throw new ArgumentException("Window type is required.", nameof(windowType));

        return windowType.Trim();
    }
}
