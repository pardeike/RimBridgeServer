using System;
using System.Linq;
using Verse;

namespace RimBridgeServer;

internal sealed class ContextMenuOptionExecutionResult
{
    public bool Success { get; set; }

    public int MenuId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public int ResolvedIndex { get; set; }

    public string Label { get; set; }

    public string Message { get; set; } = string.Empty;
}

internal static class RimWorldContextMenuActions
{
    public static ContextMenuOptionExecutionResult ExecuteOption(int optionIndex = -1, string label = null, int expectedMenuId = 0)
    {
        var snapshot = GetActiveSnapshot();
        if (snapshot == null)
            return Failure("No debug context menu is available.");

        if (expectedMenuId > 0 && snapshot.Id != expectedMenuId)
        {
            return Failure($"Context menu target id refers to menu {expectedMenuId}, but the active debug context menu is {snapshot.Id}.");
        }

        FloatMenuOption option;
        int resolvedIndex;
        var resolutionError = TryResolveOption(snapshot, optionIndex, label, out option, out resolvedIndex);
        if (resolutionError != null)
            return Failure(resolutionError);

        if (option.Disabled)
            return Failure($"Menu option {resolvedIndex} is disabled.", snapshot, resolvedIndex, option.Label);
        if (option.action == null)
            return Failure($"Menu option {resolvedIndex} has no executable action.", snapshot, resolvedIndex, option.Label);

        option.Chosen(snapshot.Menu.givesColonistOrders, snapshot.Menu);
        Find.WindowStack?.TryRemove(snapshot.Menu, doCloseSound: false);
        RimBridgeContextMenus.Clear();

        return new ContextMenuOptionExecutionResult
        {
            Success = true,
            MenuId = snapshot.Id,
            Provider = snapshot.Provider ?? string.Empty,
            ResolvedIndex = resolvedIndex,
            Label = option.Label,
            Message = $"Executed menu option {resolvedIndex} '{option.Label}'."
        };
    }

    private static ContextMenuSnapshot GetActiveSnapshot()
    {
        var snapshot = RimBridgeContextMenus.Current;
        if (snapshot == null || snapshot.Menu == null)
            return null;

        if (Find.WindowStack?.FloatMenu == snapshot.Menu)
            return snapshot;

        RimBridgeContextMenus.Clear();
        return null;
    }

    private static string TryResolveOption(ContextMenuSnapshot snapshot, int optionIndex, string label, out FloatMenuOption option, out int resolvedIndex)
    {
        option = null;
        resolvedIndex = -1;

        if (optionIndex > 0)
        {
            if (optionIndex > snapshot.Options.Count)
                return $"Option index {optionIndex} is out of range for a menu with {snapshot.Options.Count} options.";

            resolvedIndex = optionIndex;
            option = snapshot.Options[optionIndex - 1];
            return null;
        }

        if (string.IsNullOrWhiteSpace(label))
            return "Either optionIndex or label must be provided.";

        var exactMatches = snapshot.Options
            .Select((candidate, index) => new { candidate, index })
            .Where(item => string.Equals(item.candidate.Label, label, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exactMatches.Count == 1)
        {
            option = exactMatches[0].candidate;
            resolvedIndex = exactMatches[0].index + 1;
            return null;
        }

        if (exactMatches.Count > 1)
            return $"Label '{label}' is ambiguous within the current menu.";

        var partialMatches = snapshot.Options
            .Select((candidate, index) => new { candidate, index })
            .Where(item => item.candidate.Label.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        if (partialMatches.Count != 1)
            return $"Could not resolve menu label '{label}' to a single option.";

        option = partialMatches[0].candidate;
        resolvedIndex = partialMatches[0].index + 1;
        return null;
    }

    private static ContextMenuOptionExecutionResult Failure(string message, ContextMenuSnapshot snapshot = null, int resolvedIndex = -1, string label = null)
    {
        return new ContextMenuOptionExecutionResult
        {
            Success = false,
            MenuId = snapshot?.Id ?? 0,
            Provider = snapshot?.Provider ?? string.Empty,
            ResolvedIndex = resolvedIndex,
            Label = label,
            Message = message
        };
    }
}
