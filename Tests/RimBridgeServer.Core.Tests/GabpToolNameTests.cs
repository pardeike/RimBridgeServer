using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class GabpToolNameTests
{
    private static readonly Regex ToolAttributeRegex = new(@"\[Tool\s*\(\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ToolAttributeLineRegex = new(@"\[Tool\s*\(\s*""(?<name>[^""]+)""(?<rest>.*)\)\]", RegexOptions.Compiled);

    [Fact]
    public void BuiltInToolAttributesUseCanonicalGabpNames()
    {
        var root = FindRepositoryRoot();
        var sourceDir = Path.Combine(root, "Source");
        var names = new List<(string File, string Name)>();

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, file);
            if (relativePath.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relativePath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(file);
            foreach (Match match in ToolAttributeRegex.Matches(text))
            {
                names.Add((relativePath, match.Groups[1].Value));
            }
        }

        Assert.NotEmpty(names);

        var invalid = names
            .Where(entry => GabpToolNameValidator.IsCanonical(entry.Name) == false)
            .Select(entry => entry.File + ": " + entry.Name)
            .ToList();

        Assert.Empty(invalid);
    }

    [Fact]
    public void BuiltInAttentionDiagnosticsDeclareBypassTags()
    {
        var root = FindRepositoryRoot();
        var sourcePath = Path.Combine(root, "Source", "RimBridgeTools.cs");
        var tagsByTool = File.ReadLines(sourcePath)
            .Select(line => ToolAttributeLineRegex.Match(line))
            .Where(match => match.Success && match.Groups["rest"].Value.Contains("Tags =", StringComparison.Ordinal))
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["rest"].Value, StringComparer.Ordinal);

        AssertToolTags(tagsByTool, "rimbridge/ping", "diagnostic", "read-only");
        AssertToolTags(tagsByTool, "rimworld/get_game_info", "diagnostic", "read-only");
        AssertToolTags(tagsByTool, "rimbridge/get_operation", "diagnostic", "read-only");
        AssertToolTags(tagsByTool, "rimbridge/get_bridge_status", "diagnostic", "status", "read-only");
        AssertToolTags(tagsByTool, "rimbridge/list_capabilities", "diagnostic", "read-only");
        AssertToolTags(tagsByTool, "rimbridge/get_capability", "diagnostic", "read-only");
        AssertToolTags(tagsByTool, "rimbridge/list_operations", "diagnostic", "read-only");
        AssertToolTags(tagsByTool, "rimbridge/list_operation_events", "diagnostic", "lifecycle", "read-only");
        AssertToolTags(tagsByTool, "rimbridge/list_logs", "diagnostic", "read-only");
        AssertToolTags(tagsByTool, "rimbridge/wait_for_operation", "diagnostic", "lifecycle");
        AssertToolTags(tagsByTool, "rimbridge/wait_for_game_loaded", "diagnostic", "lifecycle");
        AssertToolTags(tagsByTool, "rimbridge/wait_for_long_event_idle", "diagnostic", "lifecycle", "read-only");
    }

    [Fact]
    public void AnnotatedExtensionProviderCarriesTagsIntoToolInfo()
    {
        var root = FindRepositoryRoot();
        var sourcePath = Path.Combine(root, "Source", "AnnotatedExtensionCapabilityProvider.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("Tags = NormalizeTags(attribute.Tags)", source, StringComparison.Ordinal);
    }

    private static void AssertToolTags(IReadOnlyDictionary<string, string> tagsByTool, string toolName, params string[] expectedTags)
    {
        Assert.True(tagsByTool.TryGetValue(toolName, out var attributeText), $"Tool {toolName} should declare tags.");
        foreach (var tag in expectedTags)
            Assert.Contains($"\"{tag}\"", attributeText, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(directory.FullName, "Source")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find RimBridgeServer repository root.");
    }
}
