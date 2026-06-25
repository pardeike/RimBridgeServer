using System;
using System.IO;
using System.Linq;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public sealed class CompanionFileDiscoveryTests : IDisposable
{
    private readonly string _root;

    public CompanionFileDiscoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rbs-companion-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void DiscoversLooseRootDllsWithoutRequiringSubfolders()
    {
        var bridgeTools = Path.Combine(_root, "BridgeTools");
        Directory.CreateDirectory(bridgeTools);
        Touch(Path.Combine(bridgeTools, "SimpleGlobalTool.dll"));
        Touch(Path.Combine(bridgeTools, CompanionFileDiscovery.SdkAssemblyFileName));

        var candidates = CompanionFileDiscovery.DiscoverBridgeToolsRoot(bridgeTools, CompanionRootKind.Global, "global");

        var candidate = Assert.Single(candidates);
        Assert.Equal(Path.Combine(bridgeTools, "SimpleGlobalTool.dll"), candidate.AssemblyPath);
        Assert.False(candidate.IsBundled);
    }

    [Fact]
    public void DiscoversFirstLevelBundleEntryDllAndIgnoresNestedHelpersAsEntries()
    {
        var bridgeTools = Path.Combine(_root, "BridgeTools");
        var bundle = Path.Combine(bridgeTools, "PoseHarness");
        var nested = Path.Combine(bundle, "Nested");
        Directory.CreateDirectory(nested);
        Touch(Path.Combine(bundle, "PoseHarness.dll"));
        Touch(Path.Combine(bundle, "PoseHarness.Helpers.dll"));
        Touch(Path.Combine(nested, "NotAnEntry.dll"));

        var candidates = CompanionFileDiscovery.DiscoverBridgeToolsRoot(bridgeTools, CompanionRootKind.Global, "global");

        var candidate = Assert.Single(candidates);
        Assert.Equal(Path.Combine(bundle, "PoseHarness.dll"), candidate.AssemblyPath);
        Assert.True(candidate.IsBundled);
        Assert.Equal(bundle, candidate.BundleDirectory);
    }

    [Fact]
    public void DiscoversAllDirectBundleDllsWhenNoFolderNamedEntryExists()
    {
        var bridgeTools = Path.Combine(_root, "BridgeTools");
        var bundle = Path.Combine(bridgeTools, "CustomBundle");
        Directory.CreateDirectory(bundle);
        Touch(Path.Combine(bundle, "Alpha.dll"));
        Touch(Path.Combine(bundle, "Beta.dll"));

        var candidates = CompanionFileDiscovery.DiscoverBridgeToolsRoot(bridgeTools, CompanionRootKind.Mod, "some.mod");

        Assert.Equal(["Alpha.dll", "Beta.dll"], candidates.Select(candidate => Path.GetFileName(candidate.AssemblyPath)).ToArray());
        Assert.All(candidates, candidate => Assert.True(candidate.IsBundled));
    }

    private static void Touch(string path)
    {
        File.WriteAllBytes(path, []);
    }
}
