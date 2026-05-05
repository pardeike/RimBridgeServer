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
