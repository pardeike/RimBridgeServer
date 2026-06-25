using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimBridgeServer.Core;

public enum CompanionRootKind
{
    Global,
    Mod
}

public sealed class CompanionAssemblyCandidate
{
    public string AssemblyPath { get; set; } = string.Empty;

    public string BridgeToolsRoot { get; set; } = string.Empty;

    public string BundleDirectory { get; set; }

    public CompanionRootKind RootKind { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public bool IsBundled => string.IsNullOrWhiteSpace(BundleDirectory) == false;
}

public static class CompanionFileDiscovery
{
    public const string BridgeToolsFolderName = "BridgeTools";
    public const string SdkAssemblyName = "RimBridgeServer.Sdk";
    public const string SdkAssemblyFileName = SdkAssemblyName + ".dll";

    public static IReadOnlyList<CompanionAssemblyCandidate> DiscoverBridgeToolsRoot(
        string bridgeToolsRoot,
        CompanionRootKind rootKind,
        string ownerId)
    {
        if (string.IsNullOrWhiteSpace(bridgeToolsRoot) || Directory.Exists(bridgeToolsRoot) == false)
            return [];

        var root = Path.GetFullPath(bridgeToolsRoot);
        var candidates = new List<CompanionAssemblyCandidate>();

        foreach (var file in Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(IsNotSdkAssembly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(new CompanionAssemblyCandidate
            {
                AssemblyPath = Path.GetFullPath(file),
                BridgeToolsRoot = root,
                RootKind = rootKind,
                OwnerId = ownerId ?? string.Empty
            });
        }

        foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            candidates.AddRange(DiscoverBundleDirectory(root, directory, rootKind, ownerId));
        }

        return candidates;
    }

    private static IEnumerable<CompanionAssemblyCandidate> DiscoverBundleDirectory(
        string root,
        string bundleDirectory,
        CompanionRootKind rootKind,
        string ownerId)
    {
        var bundleName = Path.GetFileName(bundleDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var preferredEntry = Path.Combine(bundleDirectory, bundleName + ".dll");
        var entries = File.Exists(preferredEntry)
            ? [preferredEntry]
            : Directory.GetFiles(bundleDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(IsNotSdkAssembly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (var entry in entries)
        {
            if (IsNotSdkAssembly(entry) == false)
                continue;

            yield return new CompanionAssemblyCandidate
            {
                AssemblyPath = Path.GetFullPath(entry),
                BridgeToolsRoot = root,
                BundleDirectory = Path.GetFullPath(bundleDirectory),
                RootKind = rootKind,
                OwnerId = ownerId ?? string.Empty
            };
        }
    }

    private static bool IsNotSdkAssembly(string path)
    {
        return string.Equals(Path.GetFileName(path), SdkAssemblyFileName, StringComparison.OrdinalIgnoreCase) == false;
    }
}
