using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimBridgeServer.Core;
using RimWorld;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldDebugActions
{
    private sealed class ExecutionCapture
    {
        public object State { get; set; }

        public UiStateSnapshot UiState { get; set; }

        public long LogSequence { get; set; }
    }

    public static object ListDebugActionRootsResponse(bool includeHidden = false)
    {
        EnsureNodeGraph();

        var roots = Dialog_Debug.roots?
            .Where(entry => entry.Value != null)
            .Select(entry =>
            {
                PrepareNode(entry.Value);
                return new
                {
                    TabDef = entry.Key,
                    Node = entry.Value
                };
            })
            .Where(entry => includeHidden || entry.Node.VisibleNow)
            .OrderBy(entry => entry.TabDef?.defName ?? entry.Node.Path ?? entry.Node.LabelNow ?? string.Empty, StringComparer.Ordinal)
            .ToList()
            ?? [];

        return new
        {
            success = true,
            devModeEnabled = Prefs.DevMode,
            rootCount = roots.Count,
            roots = roots.Select(entry => DescribeNode(entry.Node, entry.TabDef?.defName)).ToList()
        };
    }

    public static object ListDebugActionChildrenResponse(string path, bool includeHidden = false)
    {
        if (!TryResolveNode(path, out var node, out var normalizedPath, out var error))
            return Failure(error);

        var children = GetChildren(node, includeHidden);
        return new
        {
            success = true,
            devModeEnabled = Prefs.DevMode,
            path = normalizedPath,
            childCount = children.Count,
            children = children.Select(child => DescribeNode(child)).ToList()
        };
    }

    public static object GetDebugActionResponse(string path, bool includeChildren = true, bool includeHiddenChildren = false)
    {
        if (!TryResolveNode(path, out var node, out var normalizedPath, out var error))
            return Failure(error);

        var children = includeChildren ? GetChildren(node, includeHiddenChildren) : null;
        return new
        {
            success = true,
            devModeEnabled = Prefs.DevMode,
            path = normalizedPath,
            node = DescribeNode(node),
            childCount = children?.Count ?? 0,
            children = children?.Select(child => DescribeNode(child)).ToList()
        };
    }

    public static object ExecuteDebugActionResponse(string path)
    {
        if (!TryResolveNode(path, out var node, out var normalizedPath, out var error))
            return Failure(error);

        var assessment = DebugActionExecutionPolicy.Evaluate(
            HasChildren(node),
            node.actionType.ToString(),
            node.action != null,
            node.pawnAction != null);

        if (!assessment.Supported)
        {
            return new
            {
                success = false,
                message = assessment.Reason,
                path = normalizedPath,
                node = DescribeNode(node),
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        var before = CaptureExecution();

        try
        {
            node.action?.Invoke();
        }
        catch (Exception ex)
        {
            RefreshNode(node);
            var after = CaptureExecution();
            return new
            {
                success = false,
                message = $"Debug action '{normalizedPath}' failed: {ex.Message}",
                exceptionType = ex.GetType().FullName,
                path = normalizedPath,
                node = DescribeNode(node),
                stateBefore = before.State,
                stateAfter = after.State,
                effects = DescribeEffects(before, after)
            };
        }

        RefreshNode(node);
        var completed = CaptureExecution();
        return new
        {
            success = true,
            path = normalizedPath,
            node = DescribeNode(node),
            stateBefore = before.State,
            stateAfter = completed.State,
            effects = DescribeEffects(before, completed)
        };
    }

    public static object SetDebugSettingResponse(string path, bool enabled)
    {
        if (!TryResolveNode(path, out var node, out var normalizedPath, out var error))
            return Failure(error);

        if (node.settingsField == null)
        {
            return new
            {
                success = false,
                message = $"Debug node '{normalizedPath}' is not a settings toggle.",
                path = normalizedPath,
                node = DescribeNode(node),
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        var before = CaptureExecution();
        var previousValue = node.On;
        if (previousValue == enabled)
        {
            return new
            {
                success = true,
                changed = false,
                path = normalizedPath,
                value = previousValue,
                node = DescribeNode(node),
                stateBefore = before.State,
                stateAfter = before.State,
                effects = DescribeEffects(before, before)
            };
        }

        try
        {
            node.action?.Invoke();
        }
        catch (Exception ex)
        {
            RefreshNode(node);
            var after = CaptureExecution();
            return new
            {
                success = false,
                message = $"Setting debug toggle '{normalizedPath}' failed: {ex.Message}",
                exceptionType = ex.GetType().FullName,
                path = normalizedPath,
                value = node.On,
                node = DescribeNode(node),
                stateBefore = before.State,
                stateAfter = after.State,
                effects = DescribeEffects(before, after)
            };
        }

        RefreshNode(node);
        var completed = CaptureExecution();
        var currentValue = node.On;
        if (currentValue != enabled)
        {
            return new
            {
                success = false,
                changed = currentValue != previousValue,
                message = $"Debug setting '{normalizedPath}' did not reach the requested value '{enabled}'.",
                path = normalizedPath,
                value = currentValue,
                node = DescribeNode(node),
                stateBefore = before.State,
                stateAfter = completed.State,
                effects = DescribeEffects(before, completed)
            };
        }

        return new
        {
            success = true,
            changed = currentValue != previousValue,
            path = normalizedPath,
            value = currentValue,
            node = DescribeNode(node),
            stateBefore = before.State,
            stateAfter = completed.State,
            effects = DescribeEffects(before, completed)
        };
    }

    private static bool TryResolveNode(string path, out DebugActionNode node, out string normalizedPath, out string error)
    {
        node = null;
        normalizedPath = NormalizePath(path);
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            error = "A debug action path is required.";
            return false;
        }

        EnsureNodeGraph();
        node = Dialog_Debug.GetNode(normalizedPath);
        if (node == null)
        {
            error = $"Could not find debug action '{normalizedPath}'.";
            return false;
        }

        PrepareNode(node);
        return true;
    }

    private static void EnsureNodeGraph()
    {
        Dialog_Debug.TrySetupNodeGraph();
    }

    private static void PrepareNode(DebugActionNode node)
    {
        if (node == null)
            return;

        node.TrySetupChildren();
        node.TrySort();
    }

    private static void RefreshNode(DebugActionNode node)
    {
        if (node == null)
            return;

        node.cachedCheckOn = null;
        node.DirtyLabelCache();
        PrepareNode(node);
    }

    private static List<DebugActionNode> GetChildren(DebugActionNode node, bool includeHidden)
    {
        PrepareNode(node);
        if (node.children == null || node.children.Count == 0)
            return [];

        var children = new List<DebugActionNode>(node.children.Count);
        foreach (var child in node.children)
        {
            if (child == null)
                continue;

            PrepareNode(child);
            if (!includeHidden && !child.VisibleNow)
                continue;

            children.Add(child);
        }

        return children;
    }

    private static bool HasChildren(DebugActionNode node)
    {
        PrepareNode(node);
        return node.children != null && node.children.Count > 0;
    }

    private static object DescribeNode(DebugActionNode node, string tabDefName = null)
    {
        PrepareNode(node);
        var childCount = node.children?.Count ?? 0;
        var visibleChildCount = node.children?.Count(child => child != null && child.VisibleNow) ?? 0;
        var tabRootPath = ResolveTabRootPath(node);
        var (tabId, tabTitle) = ResolveTabMetadata(tabRootPath, tabDefName);
        var assessment = DebugActionExecutionPolicy.Evaluate(
            childCount > 0,
            node.actionType.ToString(),
            node.action != null,
            node.pawnAction != null);
        var attribute = node.sourceAttribute;

        return new
        {
            path = node.Path,
            parentPath = node.parent?.Path,
            label = node.LabelNow,
            category = node.category ?? attribute?.category,
            actionType = node.actionType.ToString(),
            isRoot = node.IsRoot,
            visible = node.VisibleNow,
            active = node.ActiveNow,
            on = node.On,
            displayPriority = node.displayPriority,
            tabId,
            tabTitle,
            tabRootPath,
            tabDefName = tabDefName,
            hasChildren = childCount > 0,
            childCount,
            visibleChildCount,
            hasDirectAction = node.action != null,
            hasPawnAction = node.pawnAction != null,
            hasSettingsToggle = node.settingsField != null,
            execution = new
            {
                kind = assessment.Kind.ToString(),
                supported = assessment.Supported,
                reason = string.IsNullOrWhiteSpace(assessment.Reason) ? null : assessment.Reason
            },
            source = attribute == null
                ? null
                : new
                {
                    name = attribute.name,
                    category = attribute.category,
                    allowedGameStates = attribute.allowedGameStates.ToString(),
                    hideInSubMenu = attribute.hideInSubMenu,
                    requiresRoyalty = attribute.requiresRoyalty,
                    requiresIdeology = attribute.requiresIdeology,
                    requiresBiotech = attribute.requiresBiotech,
                    requiresAnomaly = attribute.requiresAnomaly,
                    requiresOdyssey = attribute.requiresOdyssey
                }
        };
    }

    private static ExecutionCapture CaptureExecution()
    {
        return new ExecutionCapture
        {
            State = RimWorldState.ToolStateSnapshot(),
            UiState = RimWorldInput.GetUiState(),
            LogSequence = RimBridgeCapabilities.LogJournal?.LatestSequence ?? 0
        };
    }

    private static object DescribeEffects(ExecutionCapture before, ExecutionCapture after)
    {
        var logEntries = RimBridgeCapabilities.LogJournal?.GetEntries(limit: 50, afterSequence: before.LogSequence) ?? [];
        return new
        {
            logCount = logEntries.Count,
            logs = logEntries.Select(entry => new
            {
                sequence = entry.Sequence,
                level = entry.Level,
                message = entry.Message,
                source = entry.Source,
                repeatCount = entry.RepeatCount,
                timestampUtc = entry.TimestampUtc
            }).ToList(),
            windowCountDelta = after.UiState.WindowCount - before.UiState.WindowCount,
            openedWindowTypes = GetOpenedWindowTypes(before.UiState, after.UiState),
            closedWindowTypes = GetClosedWindowTypes(before.UiState, after.UiState)
        };
    }

    private static List<string> GetOpenedWindowTypes(UiStateSnapshot before, UiStateSnapshot after)
    {
        var beforeCounts = CountWindowTypes(before);
        var afterCounts = CountWindowTypes(after);
        return DescribeWindowTypeDelta(beforeCounts, afterCounts);
    }

    private static List<string> GetClosedWindowTypes(UiStateSnapshot before, UiStateSnapshot after)
    {
        var beforeCounts = CountWindowTypes(before);
        var afterCounts = CountWindowTypes(after);
        return DescribeWindowTypeDelta(afterCounts, beforeCounts);
    }

    private static Dictionary<string, int> CountWindowTypes(UiStateSnapshot state)
    {
        return state.Windows
            .Where(window => string.IsNullOrWhiteSpace(window.Type) == false)
            .GroupBy(window => window.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static List<string> DescribeWindowTypeDelta(IReadOnlyDictionary<string, int> baseline, IReadOnlyDictionary<string, int> updated)
    {
        var changed = new List<string>();
        foreach (var pair in updated)
        {
            baseline.TryGetValue(pair.Key, out var baselineCount);
            if (pair.Value <= baselineCount)
                continue;

            for (var i = baselineCount; i < pair.Value; i++)
                changed.Add(pair.Key);
        }

        return changed;
    }

    private static string ResolveTabRootPath(DebugActionNode node)
    {
        var path = node.Path;
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var separator = path.IndexOf('\\');
        return separator < 0 ? path : path[..separator];
    }

    private static (string TabId, string TabTitle) ResolveTabMetadata(string tabRootPath, string tabDefName)
    {
        return tabRootPath switch
        {
            "Actions" => ("actions", "Actions/tools"),
            "Outputs" => ("output", "Output"),
            "Settings" => ("settings", "Settings"),
            _ => (string.IsNullOrWhiteSpace(tabDefName) ? string.Empty : tabDefName.ToLowerInvariant(), tabDefName ?? tabRootPath)
        };
    }

    private static object Failure(string message)
    {
        return new
        {
            success = false,
            message,
            devModeEnabled = Prefs.DevMode,
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
    }
}
