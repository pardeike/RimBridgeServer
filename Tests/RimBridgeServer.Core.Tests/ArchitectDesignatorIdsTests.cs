using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class ArchitectDesignatorIdsTests
{
    [Fact]
    public void CreatesStableCategoryIds()
    {
        var categoryId = ArchitectDesignatorIds.CreateCategoryId("Structure");

        Assert.Equal("architect-category:structure", categoryId);
    }

    [Fact]
    public void CreatesStableDropdownChildDesignatorIds()
    {
        var designatorId = ArchitectDesignatorIds.CreateDropdownChildDesignatorId("Structure", "Floors", "Wooden Floor");

        Assert.Equal("architect-designator:structure:floors:wooden-floor", designatorId);
    }

    [Theory]
    [InlineData("Structure", "structure")]
    [InlineData("architect-category:structure", "structure")]
    [InlineData("  architect-category:Orders  ", "orders")]
    public void ParsesCategoryKeysFromIdsAndRawNames(string input, string expected)
    {
        var parsed = ArchitectDesignatorIds.TryGetCategoryKey(input, out var categoryKey);

        Assert.True(parsed);
        Assert.Equal(expected, categoryKey);
    }

    [Theory]
    [InlineData("Build Wall", "build-wall")]
    [InlineData("build:wall", "build-wall")]
    [InlineData("  Wall / Granite  ", "wall-granite")]
    public void NormalizesSegments(string input, string expected)
    {
        var normalized = ArchitectDesignatorIds.NormalizeSegment(input);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public void RejectsEmptySegments(string input)
    {
        Assert.Throws<System.ArgumentException>(() => ArchitectDesignatorIds.NormalizeSegment(input));
    }
}
