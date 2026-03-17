using System;
using System.Collections.Generic;
using System.Linq;
using RimBridgeServer.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class UiMainTabSnapshot
{
    public string Id { get; set; } = string.Empty;

    public string DefName { get; set; } = string.Empty;

    public string Label { get; set; }

    public string Type { get; set; } = string.Empty;

    public int Order { get; set; }

    public bool IsOpen { get; set; }

    public bool Visible { get; set; }

    public bool Disabled { get; set; }

    public bool ValidWithoutMap { get; set; }

    public bool Minimized { get; set; }

    public UiRectSnapshot Rect { get; set; } = new();
}

internal static class RimWorldMainTabs
{
    public static object ListMainTabsResponse(bool includeHidden = false)
    {
        var tabs = ListMainTabs(includeHidden);
        var openTab = GetOpenMainTabSnapshot();

        return new
        {
            success = true,
            count = tabs.Count,
            openMainTabId = openTab?.Id,
            openMainTabType = openTab?.Type,
            openMainTabLabel = openTab?.Label,
            tabs = tabs.Select(ToToolResponse).ToList()
        };
    }

    public static object OpenMainTabResponse(string mainTabId)
    {
        var before = RimWorldInput.GetUiState();
        if (!TryResolveMainTab(mainTabId, out var tab, out var failure))
            return CreateFailedCommandResponse("open_main_tab", before, failure);

        if (!IsVisible(tab, out var visibilityFailure))
            return CreateFailedCommandResponse("open_main_tab", before, visibilityFailure);

        if (IsDisabled(tab))
        {
            return CreateFailedCommandResponse(
                "open_main_tab",
                before,
                $"Main tab '{tab.defName}' is currently disabled and cannot be opened.");
        }

        var root = TryGetMainTabsRoot();
        if (root == null)
            return CreateFailedCommandResponse("open_main_tab", before, "RimWorld main tabs are not available.");

        root.SetCurrentTab(tab, playSound: false);
        var after = RimWorldInput.GetUiState();
        return CreateCommandResponse(
            "open_main_tab",
            before,
            after,
            $"Opened RimWorld main tab '{tab.defName}'.");
    }

    public static object CloseMainTabResponse(string mainTabId = null)
    {
        var before = RimWorldInput.GetUiState();
        var root = TryGetMainTabsRoot();
        var openTab = root?.OpenTab;
        if (openTab == null)
        {
            return CreateFailedCommandResponse(
                "close_main_tab",
                before,
                "No RimWorld main tab is currently open.");
        }

        if (!string.IsNullOrWhiteSpace(mainTabId)
            && !MainTabMatches(mainTabId, openTab))
        {
            return CreateFailedCommandResponse(
                "close_main_tab",
                before,
                $"The currently open main tab '{openTab.defName}' does not match '{mainTabId}'.");
        }

        if (root == null)
            return CreateFailedCommandResponse("close_main_tab", before, "RimWorld main tabs are not available.");

        root.EscapeCurrentTab(playSound: false);
        var after = RimWorldInput.GetUiState();
        return CreateCommandResponse(
            "close_main_tab",
            before,
            after,
            $"Closed RimWorld main tab '{openTab.defName}'.");
    }

    public static UiMainTabSnapshot GetOpenMainTabSnapshot()
    {
        var openTab = TryGetMainTabsRoot()?.OpenTab;
        return openTab == null ? null : DescribeMainTab(openTab, isOpen: true);
    }

    internal static List<UiMainTabSnapshot> ListMainTabs(bool includeHidden)
    {
        var openTab = TryGetMainTabsRoot()?.OpenTab;
        return DefDatabase<MainButtonDef>.AllDefsListForReading?
            .OrderBy(tab => tab.order)
            .ThenBy(tab => tab.defName, StringComparer.Ordinal)
            .Select(tab => DescribeMainTab(tab, ReferenceEquals(tab, openTab)))
            .Where(tab => includeHidden || tab.Visible)
            .ToList() ?? [];
    }

    private static MainTabsRoot TryGetMainTabsRoot()
    {
        try
        {
            return Find.MainTabsRoot;
        }
        catch
        {
            return null;
        }
    }

    private static UiMainTabSnapshot DescribeMainTab(MainButtonDef tab, bool isOpen)
    {
        var windowType = tab.tabWindowClass?.FullName
            ?? tab.tabWindowClass?.Name
            ?? tab.TabWindow?.GetType().FullName
            ?? tab.TabWindow?.GetType().Name
            ?? string.Empty;
        var windowRect = isOpen ? GetOpenTabRect(tab) : null;

        return new UiMainTabSnapshot
        {
            Id = ScreenTargetIds.CreateMainTabTargetId(tab.defName),
            DefName = tab.defName,
            Label = DescribeLabel(tab),
            Type = windowType,
            Order = tab.order,
            IsOpen = isOpen,
            Visible = IsVisible(tab, out _),
            Disabled = IsDisabled(tab),
            ValidWithoutMap = tab.validWithoutMap,
            Minimized = tab.minimized,
            Rect = windowRect == null
                ? new UiRectSnapshot()
                : new UiRectSnapshot
                {
                    X = windowRect.Value.x,
                    Y = windowRect.Value.y,
                    Width = windowRect.Value.width,
                    Height = windowRect.Value.height
                }
        };
    }

    private static Rect? GetOpenTabRect(MainButtonDef tab)
    {
        try
        {
            var window = tab.TabWindow;
            if (window == null)
                return null;

            return window.windowRect;
        }
        catch
        {
            return null;
        }
    }

    private static string DescribeLabel(MainButtonDef tab)
    {
        if (!string.IsNullOrWhiteSpace(tab.label))
            return tab.LabelCap.ToString();

        return tab.defName;
    }

    private static bool IsVisible(MainButtonDef tab, out string failure)
    {
        failure = string.Empty;

        try
        {
            return tab.Worker.Visible;
        }
        catch (Exception ex)
        {
            failure = $"Main tab '{tab.defName}' could not be evaluated for visibility: {ex.Message}";
            return false;
        }
    }

    private static bool IsDisabled(MainButtonDef tab)
    {
        try
        {
            return tab.Worker.Disabled;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveMainTab(string mainTabId, out MainButtonDef tab, out string failure)
    {
        tab = null;
        failure = string.Empty;

        if (string.IsNullOrWhiteSpace(mainTabId))
        {
            failure = "A RimWorld main tab id is required.";
            return false;
        }

        var trimmed = mainTabId.Trim();
        var mainTabDefName = trimmed;
        if (ScreenTargetIds.TryParse(trimmed, out var target))
        {
            if (target.Kind != ScreenTargetKind.MainTab)
            {
                failure = $"Target id '{mainTabId}' is not a RimWorld main-tab target id.";
                return false;
            }

            mainTabDefName = target.MainTabDefName;
        }
        else if (trimmed.StartsWith("main-tab:", StringComparison.Ordinal))
        {
            mainTabDefName = trimmed["main-tab:".Length..];
        }

        tab = DefDatabase<MainButtonDef>.AllDefsListForReading?
            .FirstOrDefault(candidate =>
                string.Equals(candidate.defName, mainTabDefName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.label, mainTabDefName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.LabelCap.ToString(), mainTabDefName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.tabWindowClass?.Name, mainTabDefName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.tabWindowClass?.FullName, mainTabDefName, StringComparison.OrdinalIgnoreCase));
        if (tab != null)
            return true;

        failure = $"Could not find a RimWorld main tab matching '{mainTabId}'.";
        return false;
    }

    private static bool MainTabMatches(string mainTabId, MainButtonDef openTab)
    {
        return TryResolveMainTab(mainTabId, out var expectedTab, out _)
            && ReferenceEquals(expectedTab, openTab);
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
            rect = new
            {
                x = tab.Rect.X,
                y = tab.Rect.Y,
                width = tab.Rect.Width,
                height = tab.Rect.Height
            }
        };
    }

    private static object CreateFailedCommandResponse(string command, UiStateSnapshot before, string message)
    {
        return new
        {
            success = false,
            command,
            changed = false,
            message,
            before = RimWorldInput.DescribeUiState(before),
            after = RimWorldInput.DescribeUiState(before)
        };
    }

    private static object CreateCommandResponse(string command, UiStateSnapshot before, UiStateSnapshot after, string message)
    {
        var changed = !string.Equals(before.OpenMainTabId, after.OpenMainTabId, StringComparison.Ordinal)
            || before.MainTabOpen != after.MainTabOpen;

        return new
        {
            success = true,
            command,
            changed,
            message = changed ? message : message + " UI state did not change.",
            before = RimWorldInput.DescribeUiState(before),
            after = RimWorldInput.DescribeUiState(after)
        };
    }
}
