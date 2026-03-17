using System.Text;

internal static class ReadmeToolSurface
{
    private static readonly string[] AllowedGroups =
    [
        "Bridge Diagnostics",
        "Scripting",
        "Debug Actions And Mods",
        "Architect And Map State",
        "UI And Input",
        "Selection And Colony State",
        "Selection Semantics And Notifications",
        "Camera And Screenshots",
        "Save/Load And Spawning",
        "Context Menus And Map Interaction"
    ];

    private static readonly HashSet<string> AllowedGroupSet = new(AllowedGroups, StringComparer.Ordinal);

    private static readonly string[] InlineCodeTerms =
    [
        "Mod.WriteSettings()",
        "ModsConfig.xml",
        "Dialog_ModSettings",
        "ModSettings",
        "main-tab",
        "ui-element",
        "params",
        "defName",
        ".lua"
    ];

    public static string Render(IReadOnlyList<ToolDefinition> tools)
    {
        ValidateMetadata(tools);

        var inlineCodeTerms = tools
            .Select(tool => tool.Name)
            .Concat(InlineCodeTerms)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(term => term.Length)
            .ToArray();

        var groups = tools
            .GroupBy(tool => tool.ReadmeGroup, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var builder = new StringBuilder();
        foreach (var groupTitle in AllowedGroups)
        {
            if (!groups.TryGetValue(groupTitle, out var groupTools))
                continue;

            if (builder.Length > 0)
                builder.AppendLine();

            builder.AppendLine($"### {groupTitle}");
            builder.AppendLine();
            foreach (var tool in groupTools)
                builder.AppendLine($"- `{tool.Name}` - {FormatSummary(tool.ReadmeSummary, inlineCodeTerms)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void ValidateMetadata(IReadOnlyList<ToolDefinition> tools)
    {
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.ReadmeGroup))
                throw new InvalidOperationException($"Tool '{tool.Name}' is missing a README group.");

            if (!AllowedGroupSet.Contains(tool.ReadmeGroup))
                throw new InvalidOperationException($"Tool '{tool.Name}' uses unknown README group '{tool.ReadmeGroup}'.");

            if (string.IsNullOrWhiteSpace(tool.ReadmeSummary))
                throw new InvalidOperationException($"Tool '{tool.Name}' is missing a README summary.");
        }

        var unknownGroups = tools
            .Select(tool => tool.ReadmeGroup)
            .Distinct(StringComparer.Ordinal)
            .Where(group => !AllowedGroupSet.Contains(group))
            .OrderBy(group => group, StringComparer.Ordinal)
            .ToArray();
        if (unknownGroups.Length > 0)
            throw new InvalidOperationException($"Unknown README groups: {string.Join(", ", unknownGroups)}");
    }

    private static string FormatSummary(string summary, IReadOnlyList<string> inlineCodeTerms)
    {
        var result = summary.Trim();
        var placeholders = new List<(string Placeholder, string Replacement)>(inlineCodeTerms.Count);
        for (var index = 0; index < inlineCodeTerms.Count; index++)
        {
            var codeTerm = inlineCodeTerms[index];
            if (!result.Contains(codeTerm, StringComparison.Ordinal))
                continue;

            var placeholder = $"\u0001CODE{index}\u0001";
            result = result.Replace(codeTerm, placeholder, StringComparison.Ordinal);
            placeholders.Add((placeholder, $"`{codeTerm}`"));
        }

        foreach (var (placeholder, replacement) in placeholders)
            result = result.Replace(placeholder, replacement, StringComparison.Ordinal);

        return result;
    }
}
