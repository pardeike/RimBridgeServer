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

    public static object SearchDebugActionsResponse(string query, int limit = 50, bool includeHidden = false, bool supportedOnly = false, string requiredTargetKind = null)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return Failure("A non-empty query is required.");

        if (limit <= 0)
            limit = 50;

        var matches = EnumerateDebugActionNodes(includeHidden)
            .Select(node => TryCreateSearchMatch(node, normalizedQuery, supportedOnly, requiredTargetKind))
            .Where(match => match != null)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Path, StringComparer.Ordinal)
            .ToList();

        var totalMatchCount = matches.Count;
        var limitedMatches = matches.Take(limit).ToList();

        return new
        {
            success = true,
            devModeEnabled = Prefs.DevMode,
            query = normalizedQuery,
            includeHidden,
            supportedOnly,
            requiredTargetKind = string.IsNullOrWhiteSpace(requiredTargetKind) ? null : requiredTargetKind.Trim(),
            limit,
            matchCount = limitedMatches.Count,
            totalMatchCount,
            truncated = totalMatchCount > limitedMatches.Count,
            matches = limitedMatches.Select(match => new
            {
                score = match.Score,
                matchFields = match.MatchFields,
                path = match.Path,
                node = DescribeNode(match.Node)
            }).ToList()
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

    public static object ExecuteDebugActionResponse(string path, string pawnName = null, string pawnId = null)
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

        Pawn targetPawn = null;
        if (assessment.Kind == DebugActionExecutionKind.PawnTarget)
        {
            if (string.IsNullOrWhiteSpace(pawnName) && string.IsNullOrWhiteSpace(pawnId))
            {
                return new
                {
                    success = false,
                    message = $"Debug action '{normalizedPath}' requires a current-map pawn target. Provide pawnName or pawnId.",
                    path = normalizedPath,
                    node = DescribeNode(node),
                    requiredTargetKind = "pawn",
                    state = RimWorldState.ToolStateSnapshot()
                };
            }

            try
            {
                targetPawn = RimWorldState.ResolveCurrentMapPawn(pawnName, pawnId);
            }
            catch (Exception ex)
            {
                return new
                {
                    success = false,
                    message = $"Could not resolve pawn target for debug action '{normalizedPath}': {ex.Message}",
                    path = normalizedPath,
                    node = DescribeNode(node),
                    requiredTargetKind = "pawn",
                    state = RimWorldState.ToolStateSnapshot()
                };
            }
        }

        var before = CaptureExecution();

        try
        {
            if (assessment.Kind == DebugActionExecutionKind.PawnTarget)
            {
                node.pawnAction?.Invoke(targetPawn);
            }
            else
            {
                node.action?.Invoke();
            }
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
                targetPawn = targetPawn == null ? null : RimWorldState.DescribePawn(targetPawn),
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
            targetPawn = targetPawn == null ? null : RimWorldState.DescribePawn(targetPawn),
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

    public static object SetColonistJobLoggingResponse(string pawnName = null, string pawnId = null, bool enabled = true)
    {
        Pawn pawn;
        try
        {
            pawn = RimWorldState.ResolveCurrentMapColonist(pawnName, pawnId);
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                message = $"Could not resolve current-map colonist for job logging: {ex.Message}",
                requestedPawnName = pawnName,
                requestedPawnId = pawnId,
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (pawn.jobs == null)
        {
            return new
            {
                success = false,
                message = $"Colonist '{pawn.Name?.ToStringShort ?? pawn.LabelShort}' does not expose a job tracker.",
                pawn = RimWorldState.DescribePawn(pawn),
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        var previousValue = pawn.jobs.debugLog;
        pawn.jobs.debugLog = enabled;
        var currentValue = pawn.jobs.debugLog;
        var logCursor = RimBridgeCapabilities.LogJournal?.LatestSequence ?? 0;

        return new
        {
            success = true,
            changed = currentValue != previousValue,
            enabled = currentValue,
            pawn = RimWorldState.DescribePawn(pawn),
            state = RimWorldState.ToolStateSnapshot(),
            logCursor,
            consumeLogs = new
            {
                tool = "rimbridge/list_logs",
                recommendedArguments = new
                {
                    afterSequence = logCursor,
                    minimumLevel = "info",
                    limit = 200
                },
                expectedBehavior = currentValue
                    ? "Job logging is now enabled for this colonist. Future pawn job transitions and related tracker events can appear in the normal bridge log journal as they happen. This tool does not wait for the next job change and may not produce an immediate log line."
                    : "Job logging is now disabled for this colonist. No new job-tracker lines are expected for this pawn after the returned cursor unless another tool or debug action enables it again.",
                usageHint = currentValue
                    ? "Poll rimbridge/list_logs with afterSequence from this response while the colonist works. Treat the returned cursor as the starting point for future job logs, not as proof that a new line was already emitted."
                    : "If you were already tailing rimbridge/list_logs for this colonist, you can stop after this cursor unless you are consuming unrelated logs."
            }
        };
    }

    private sealed class DebugActionSearchMatch
    {
        public DebugActionNode Node { get; set; }

        public string Path { get; set; }

        public int Score { get; set; }

        public List<string> MatchFields { get; set; } = [];
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

    private static IEnumerable<DebugActionNode> EnumerateDebugActionNodes(bool includeHidden)
    {
        EnsureNodeGraph();

        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var roots = Dialog_Debug.roots?
            .Where(entry => entry.Value != null)
            .Select(entry => entry.Value)
            .ToList()
            ?? [];

        foreach (var root in roots)
        {
            foreach (var node in EnumerateDebugActionNodes(root, includeHidden, seenPaths))
                yield return node;
        }
    }

    private static IEnumerable<DebugActionNode> EnumerateDebugActionNodes(DebugActionNode node, bool includeHidden, ISet<string> seenPaths)
    {
        if (node == null)
            yield break;

        PrepareNode(node);

        var path = node.Path?.Trim();
        var shouldInclude = !string.IsNullOrWhiteSpace(path) && (includeHidden || node.VisibleNow);
        if (shouldInclude && seenPaths.Add(path))
            yield return node;

        if (node.children == null || node.children.Count == 0)
            yield break;

        foreach (var child in node.children)
        {
            if (child == null)
                continue;

            foreach (var nested in EnumerateDebugActionNodes(child, includeHidden, seenPaths))
                yield return nested;
        }
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

    private static DebugActionSearchMatch TryCreateSearchMatch(DebugActionNode node, string query, bool supportedOnly, string requiredTargetKind)
    {
        PrepareNode(node);

        var assessment = DebugActionExecutionPolicy.Evaluate(
            HasChildren(node),
            node.actionType.ToString(),
            node.action != null,
            node.pawnAction != null);

        if (supportedOnly && !assessment.Supported)
            return null;

        var normalizedTargetKind = requiredTargetKind?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTargetKind) == false
            && !string.Equals(assessment.RequiredTargetKind, normalizedTargetKind, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var queryLower = query.Trim().ToLowerInvariant();
        var matchFields = new List<string>();
        var score = 0;

        score += ScoreField(node.Path, query, queryLower, "path", 400, 1000, matchFields);
        score += ScoreField(node.LabelNow, query, queryLower, "label", 350, 900, matchFields);
        score += ScoreField(node.category, query, queryLower, "category", 200, 500, matchFields);
        score += ScoreField(node.sourceAttribute?.name, query, queryLower, "source.name", 180, 450, matchFields);
        score += ScoreField(node.sourceAttribute?.category, query, queryLower, "source.category", 160, 425, matchFields);
        score += ScoreField(node.actionType.ToString(), query, queryLower, "actionType", 120, 300, matchFields);
        score += ScoreField(assessment.Kind.ToString(), query, queryLower, "execution.kind", 120, 300, matchFields);
        score += ScoreField(assessment.RequiredTargetKind, query, queryLower, "execution.requiredTargetKind", 110, 275, matchFields);

        var combinedText = string.Join(" ",
            new[]
            {
                node.Path,
                node.LabelNow,
                node.category,
                node.sourceAttribute?.name,
                node.sourceAttribute?.category,
                node.actionType.ToString(),
                assessment.Kind.ToString(),
                assessment.RequiredTargetKind
            }.Where(text => string.IsNullOrWhiteSpace(text) == false));

        var queryTokens = queryLower.Split(new[] { ' ', '\t', '-', '_', '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (queryTokens.Length > 1 && queryTokens.All(token => combinedText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            score += 150;
            matchFields.Add("allTokens");
        }

        return score <= 0
            ? null
            : new DebugActionSearchMatch
            {
                Node = node,
                Path = node.Path ?? string.Empty,
                Score = score,
                MatchFields = matchFields
            };
    }

    private static int ScoreField(string value, string rawQuery, string queryLower, string fieldName, int containsScore, int exactScore, ICollection<string> matchFields)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var trimmed = value.Trim();
        if (string.Equals(trimmed, rawQuery, StringComparison.OrdinalIgnoreCase))
        {
            matchFields.Add(fieldName + ".exact");
            return exactScore;
        }

        if (trimmed.IndexOf(queryLower, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            matchFields.Add(fieldName);
            return containsScore;
        }

        return 0;
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
                reason = string.IsNullOrWhiteSpace(assessment.Reason) ? null : assessment.Reason,
                requiredTargetKind = string.IsNullOrWhiteSpace(assessment.RequiredTargetKind) ? null : assessment.RequiredTargetKind
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
