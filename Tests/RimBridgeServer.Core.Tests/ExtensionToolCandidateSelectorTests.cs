using System.Linq;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public sealed class ExtensionToolCandidateSelectorTests
{
    [Fact]
    public void DuplicateMethodDiscoveredThroughMultipleModsSelectsOneSharedAssemblyCandidate()
    {
        var candidates = new[]
        {
            Candidate("candidate-alpha", "shared/run_self_test", "method-1", "assembly-1", "Pardeike.ModLibrary", "Pardeike.ModLibrary, Version=1.0.0.0", "mod.alpha/annotations", "alpha"),
            Candidate("candidate-beta", "shared/run_self_test", "method-1", "assembly-1", "Pardeike.ModLibrary", "Pardeike.ModLibrary, Version=1.0.0.0", "mod.beta/annotations", "beta")
        };

        var selected = ExtensionToolCandidateSelector.Select(candidates, []);

        var entry = Assert.Single(selected);
        Assert.Equal("candidate-alpha", entry.CandidateId);
        Assert.Equal("extension.assembly/pardeike-mod-library/annotations", entry.ProviderId);
    }

    [Fact]
    public void UniqueModAssemblyKeepsModProviderId()
    {
        var candidates = new[]
        {
            Candidate("candidate-alpha", "alpha/ping", "method-1", "assembly-1", "AlphaMod", "AlphaMod, Version=1.0.0.0", "mod.alpha/annotations", "alpha")
        };

        var selected = ExtensionToolCandidateSelector.Select(candidates, []);

        var entry = Assert.Single(selected);
        Assert.Equal("mod.alpha/annotations", entry.ProviderId);
    }

    [Fact]
    public void DifferentMethodsWithSameToolNameSelectDeterministicFirstCandidate()
    {
        var candidates = new[]
        {
            Candidate("candidate-beta", "shared/run_self_test", "method-2", "assembly-2", "BetaLibrary", "BetaLibrary, Version=1.0.0.0", "mod.beta/annotations", "beta"),
            Candidate("candidate-alpha", "shared/run_self_test", "method-1", "assembly-1", "AlphaLibrary", "AlphaLibrary, Version=1.0.0.0", "mod.alpha/annotations", "alpha")
        };

        var selected = ExtensionToolCandidateSelector.Select(candidates, []);

        var entry = Assert.Single(selected);
        Assert.Equal("candidate-alpha", entry.CandidateId);
        Assert.Equal("mod.alpha/annotations", entry.ProviderId);
    }

    [Fact]
    public void ReservedAliasesAreSkipped()
    {
        var candidates = new[]
        {
            Candidate("candidate-alpha", "rimbridge/ping", "method-1", "assembly-1", "AlphaLibrary", "AlphaLibrary, Version=1.0.0.0", "mod.alpha/annotations", "alpha")
        };

        var selected = ExtensionToolCandidateSelector.Select(candidates, ["rimbridge/ping"]);

        Assert.Empty(selected);
    }

    [Fact]
    public void SelectionIsStableWhenInputOrderChanges()
    {
        var alpha = Candidate("candidate-alpha", "alpha/ping", "method-1", "assembly-1", "AlphaLibrary", "AlphaLibrary, Version=1.0.0.0", "mod.alpha/annotations", "alpha");
        var beta = Candidate("candidate-beta", "beta/ping", "method-2", "assembly-2", "BetaLibrary", "BetaLibrary, Version=1.0.0.0", "mod.beta/annotations", "beta");

        var selected = ExtensionToolCandidateSelector.Select([beta, alpha], []);

        Assert.Equal(["candidate-alpha", "candidate-beta"], selected.Select(entry => entry.CandidateId).ToArray());
    }

    [Fact]
    public void SharedAssemblyProviderIdCollisionsGetHashedSuffix()
    {
        var candidates = new[]
        {
            Candidate("candidate-alpha-one", "alpha/one", "method-1", "assembly-1", "Shared.Tools", "Shared.Tools, Version=1.0.0.0", "mod.alpha/annotations", "alpha"),
            Candidate("candidate-beta-one", "alpha/one", "method-1", "assembly-1", "Shared.Tools", "Shared.Tools, Version=1.0.0.0", "mod.beta/annotations", "beta"),
            Candidate("candidate-alpha-two", "alpha/two", "method-2", "assembly-2", "Shared-Tools", "Shared-Tools, Version=1.0.0.0", "mod.alpha/annotations", "alpha"),
            Candidate("candidate-beta-two", "alpha/two", "method-2", "assembly-2", "Shared-Tools", "Shared-Tools, Version=1.0.0.0", "mod.beta/annotations", "beta")
        };

        var selectedProviderIds = ExtensionToolCandidateSelector
            .Select(candidates, [])
            .Select(entry => entry.ProviderId)
            .Distinct()
            .OrderBy(providerId => providerId)
            .ToArray();

        Assert.Equal(2, selectedProviderIds.Length);
        Assert.Contains("extension.assembly/shared-tools/annotations", selectedProviderIds);
        Assert.Contains(selectedProviderIds, providerId => providerId.StartsWith("extension.assembly/shared-tools/annotations-", System.StringComparison.Ordinal));
    }

    private static ExtensionToolDiscoveryCandidate Candidate(
        string candidateId,
        string toolName,
        string methodIdentity,
        string assemblyIdentity,
        string assemblyName,
        string assemblyFullName,
        string modProviderId,
        string modSortKey)
    {
        return new ExtensionToolDiscoveryCandidate
        {
            CandidateId = candidateId,
            ToolName = toolName,
            MethodIdentity = methodIdentity,
            AssemblyIdentity = assemblyIdentity,
            AssemblyName = assemblyName,
            AssemblyFullName = assemblyFullName,
            ModProviderId = modProviderId,
            ModSortKey = modSortKey,
            TypeName = "Example.Tools",
            MethodName = "Run",
            MetadataToken = 1
        };
    }
}
