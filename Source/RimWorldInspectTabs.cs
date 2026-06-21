using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RimBridgeServer.Core;
using RimWorld;
using Verse;

namespace RimBridgeServer;

internal sealed class InspectTabSnapshot
{
    public int Ordinal { get; set; }

    public string Id { get; set; } = string.Empty;

    public string SelectionFingerprint { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string ShortType { get; set; } = string.Empty;

    public string Label { get; set; }

    public string LabelKey { get; set; }

    public string TutorTag { get; set; }

    public bool IsOpen { get; set; }

    public bool IsVisible { get; set; }

    public bool Hidden { get; set; }

    internal InspectTabBase Tab { get; set; }
}

internal static class RimWorldInspectTabs
{
    private const string InspectTabIdPrefix = "inspect-tab";

    public static object ListInspectTabsResponse(bool includeHidden = false)
    {
        if (Current.Game == null)
            return Failure("No game is currently loaded.");

        var tabs = GetCurrentTabs(includeHidden);
        return new
        {
            success = true,
            hasSelection = tabs.SelectedCount > 0,
            selectionFingerprint = tabs.SelectionFingerprint,
            selectedCount = tabs.SelectedCount,
            tabCount = tabs.Tabs.Count,
            openInspectTabId = tabs.Tabs.FirstOrDefault(tab => tab.IsOpen)?.Id,
            openInspectTabType = tabs.OpenTabType?.FullName,
            openInspectTabLabel = tabs.Tabs.FirstOrDefault(tab => tab.IsOpen)?.Label,
            tabs = tabs.Tabs.Select(ToToolResponse).ToList(),
            uiState = RimWorldInput.GetUiState()
        };
    }

    public static object OpenInspectTabResponse(string inspectTabId)
    {
        var before = RimWorldInput.GetUiState();
        if (Current.Game == null)
            return FailedCommand("open_inspect_tab", before, "No game is currently loaded.", inspectTabId);
        if (string.IsNullOrWhiteSpace(inspectTabId))
            return FailedCommand("open_inspect_tab", before, "Provide an inspect tab id, translated label, type name, or tutor tag.", inspectTabId);

        var tabs = GetCurrentTabs(includeHidden: true);
        if (tabs.SelectedCount == 0)
            return FailedCommand("open_inspect_tab", before, "No object is currently selected.", inspectTabId, tabs);
        if (tabs.Tabs.Count == 0)
            return FailedCommand("open_inspect_tab", before, "The current selection does not expose any inspect tabs.", inspectTabId, tabs);

        if (TryReadSelectionFingerprint(inspectTabId, out var requestedFingerprint)
            && !string.Equals(requestedFingerprint, tabs.SelectionFingerprint, StringComparison.Ordinal))
        {
            return FailedCommand(
                "open_inspect_tab",
                before,
                "The requested inspect tab id no longer matches the current selection.",
                inspectTabId,
                tabs);
        }

        var target = ResolveTab(tabs.Tabs, inspectTabId);
        if (target == null)
        {
            return FailedCommand(
                "open_inspect_tab",
                before,
                $"Could not find inspect tab '{inspectTabId}' for the current selection.",
                inspectTabId,
                tabs);
        }

        if (!target.IsVisible || target.Hidden)
        {
            return FailedCommand(
                "open_inspect_tab",
                before,
                $"Inspect tab '{target.Label ?? target.ShortType}' is currently hidden or not visible for the current selection.",
                inspectTabId,
                tabs);
        }

        InspectPaneUtility.OpenTab(target.Tab.GetType());
        var afterTabs = GetCurrentTabs(includeHidden: true);
        var opened = afterTabs.Tabs.FirstOrDefault(tab => string.Equals(tab.Type, target.Type, StringComparison.Ordinal));
        var after = RimWorldInput.GetUiState();
        var changed = !string.Equals(before.FocusedWindowType, after.FocusedWindowType, StringComparison.Ordinal)
            || before.WindowCount != after.WindowCount
            || !string.Equals(before.OpenMainTabId, after.OpenMainTabId, StringComparison.Ordinal)
            || !string.Equals(tabs.OpenTabType?.FullName, afterTabs.OpenTabType?.FullName, StringComparison.Ordinal);

        return new
        {
            success = true,
            command = "open_inspect_tab",
            changed,
            requestedInspectTabId = inspectTabId,
            selectionFingerprint = afterTabs.SelectionFingerprint,
            openedInspectTab = opened == null ? ToToolResponse(target) : ToToolResponse(opened),
            openInspectTabId = opened?.Id,
            openInspectTabType = afterTabs.OpenTabType?.FullName,
            before = RimWorldInput.DescribeUiState(before),
            after = RimWorldInput.DescribeUiState(after),
            tabs = afterTabs.Tabs.Select(ToToolResponse).ToList()
        };
    }

    private static CurrentInspectTabs GetCurrentTabs(bool includeHidden)
    {
        var selectedObjects = Find.Selector?.SelectedObjectsListForReading?
            .Where(selectedObject => selectedObject != null)
            .Cast<object>()
            .ToList()
            ?? [];
        var selectionFingerprint = SelectionGizmoIds.CreateSelectionFingerprint(selectedObjects.Select(GetSelectionToken));
        var inspectPane = MainButtonDefOf.Inspect?.TabWindow as MainTabWindow_Inspect;
        var openTabType = inspectPane?.OpenTabType;
        var rawTabs = inspectPane?.CurTabs?.Where(tab => tab != null).ToList() ?? [];
        var snapshots = new List<InspectTabSnapshot>(rawTabs.Count);

        for (var i = 0; i < rawTabs.Count; i++)
        {
            var tab = rawTabs[i];
            var isVisible = SafeBool(() => tab.IsVisible);
            var hidden = SafeBool(() => tab.Hidden);
            if (!includeHidden && (!isVisible || hidden))
                continue;

            var type = tab.GetType();
            var label = GetTabLabel(tab);
            var ordinal = i + 1;
            snapshots.Add(new InspectTabSnapshot
            {
                Ordinal = ordinal,
                Id = CreateInspectTabId(selectionFingerprint, ordinal, tab, label),
                SelectionFingerprint = selectionFingerprint,
                Type = type.FullName ?? type.Name,
                ShortType = type.Name,
                Label = label,
                LabelKey = string.IsNullOrWhiteSpace(tab.labelKey) ? null : tab.labelKey,
                TutorTag = string.IsNullOrWhiteSpace(tab.tutorTag) ? null : tab.tutorTag,
                IsOpen = openTabType != null && type == openTabType,
                IsVisible = isVisible,
                Hidden = hidden,
                Tab = tab
            });
        }

        return new CurrentInspectTabs
        {
            SelectionFingerprint = selectionFingerprint,
            SelectedCount = selectedObjects.Count,
            OpenTabType = openTabType,
            Tabs = snapshots
        };
    }

    private static InspectTabSnapshot ResolveTab(IReadOnlyList<InspectTabSnapshot> tabs, string query)
    {
        var normalized = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var exact = tabs.FirstOrDefault(tab => string.Equals(tab.Id, query.Trim(), StringComparison.Ordinal));
        if (exact != null)
            return exact;

        exact = tabs.FirstOrDefault(tab =>
            string.Equals(NormalizeQuery(tab.Type), normalized, StringComparison.Ordinal)
            || string.Equals(NormalizeQuery(tab.ShortType), normalized, StringComparison.Ordinal)
            || string.Equals(NormalizeQuery(tab.Label), normalized, StringComparison.Ordinal)
            || string.Equals(NormalizeQuery(tab.LabelKey), normalized, StringComparison.Ordinal)
            || string.Equals(NormalizeQuery(tab.TutorTag), normalized, StringComparison.Ordinal));
        if (exact != null)
            return exact;

        return tabs.FirstOrDefault(tab =>
            NormalizeQuery(tab.Type).Contains(normalized)
            || NormalizeQuery(tab.ShortType).Contains(normalized)
            || NormalizeQuery(tab.Label).Contains(normalized));
    }

    private static object ToToolResponse(InspectTabSnapshot tab)
    {
        return new
        {
            ordinal = tab.Ordinal,
            inspectTabId = tab.Id,
            selectionFingerprint = tab.SelectionFingerprint,
            type = tab.Type,
            shortType = tab.ShortType,
            label = tab.Label,
            labelKey = tab.LabelKey,
            tutorTag = tab.TutorTag,
            isOpen = tab.IsOpen,
            isVisible = tab.IsVisible,
            hidden = tab.Hidden
        };
    }

    private static object Failure(string message)
    {
        return new
        {
            success = false,
            message
        };
    }

    private static object FailedCommand(string command, UiStateSnapshot before, string message, string requestedInspectTabId, CurrentInspectTabs tabs = null)
    {
        return new
        {
            success = false,
            command,
            changed = false,
            message,
            requestedInspectTabId,
            selectionFingerprint = tabs?.SelectionFingerprint,
            availableInspectTabIds = tabs?.Tabs
                .Where(tab => tab.IsVisible && !tab.Hidden)
                .Select(tab => tab.Id)
                .ToList(),
            tabs = tabs?.Tabs.Select(ToToolResponse).ToList(),
            before = RimWorldInput.DescribeUiState(before),
            after = RimWorldInput.DescribeUiState(before)
        };
    }

    private static string CreateInspectTabId(string selectionFingerprint, int ordinal, InspectTabBase tab, string label)
    {
        return $"{InspectTabIdPrefix}:{selectionFingerprint}:{ordinal}:{ComputeStableHash(new[] { tab.GetType().FullName ?? tab.GetType().Name, label ?? string.Empty })}";
    }

    private static bool TryReadSelectionFingerprint(string inspectTabId, out string selectionFingerprint)
    {
        selectionFingerprint = string.Empty;
        if (string.IsNullOrWhiteSpace(inspectTabId))
            return false;

        var segments = inspectTabId.Split(':');
        if (segments.Length != 4 || !string.Equals(segments[0], InspectTabIdPrefix, StringComparison.Ordinal))
            return false;

        selectionFingerprint = segments[1];
        return selectionFingerprint.Length > 0;
    }

    private static string ComputeStableHash(IEnumerable<string> parts)
    {
        var payload = string.Join("\n", parts.Select(part => part?.Trim() ?? string.Empty));
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return string.Concat(hash.Take(8).Select(static value => value.ToString("x2")));
    }

    private static string GetTabLabel(InspectTabBase tab)
    {
        if (string.IsNullOrWhiteSpace(tab.labelKey))
            return tab.GetType().Name;

        try
        {
            return tab.labelKey.Translate().ToString();
        }
        catch
        {
            return tab.labelKey;
        }
    }

    private static bool SafeBool(Func<bool> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeQuery(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
    }

    private static string GetSelectionToken(object selectedObject)
    {
        return selectedObject switch
        {
            Thing thing => thing.GetUniqueLoadID(),
            Zone zone => zone.GetUniqueLoadID(),
            Plan plan => plan.GetUniqueLoadID(),
            _ => (selectedObject?.GetType().FullName ?? "null") + ":" + selectedObject
        };
    }

    private sealed class CurrentInspectTabs
    {
        public string SelectionFingerprint { get; set; } = string.Empty;

        public int SelectedCount { get; set; }

        public Type OpenTabType { get; set; }

        public List<InspectTabSnapshot> Tabs { get; set; } = [];
    }
}
