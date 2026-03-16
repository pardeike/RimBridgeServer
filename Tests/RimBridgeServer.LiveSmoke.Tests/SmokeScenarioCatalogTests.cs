using System.Linq;
using RimBridgeServer.LiveSmoke;
using Xunit;

namespace RimBridgeServer.LiveSmoke.Tests;

public class SmokeScenarioCatalogTests
{
    [Fact]
    public void ListsExpectedScenarios()
    {
        var scenarios = SmokeScenarioCatalog.List();

        Assert.Contains(scenarios, scenario => scenario.Name == SmokeScenarioCatalog.DebugGameLoadScenarioName);
        Assert.Contains(scenarios, scenario => scenario.Name == SmokeScenarioCatalog.SelectionRoundTripScenarioName);
        Assert.Contains(scenarios, scenario => scenario.Name == SmokeScenarioCatalog.SaveLoadRoundTripScenarioName);
        Assert.Contains(scenarios, scenario => scenario.Name == SmokeScenarioCatalog.ScreenshotCaptureScenarioName);
    }

    [Fact]
    public void ResolvesKnownScenarioByName()
    {
        var scenario = SmokeScenarioCatalog.GetOrThrow(SmokeScenarioCatalog.SelectionRoundTripScenarioName);

        Assert.Equal("selection-roundtrip", scenario.Name);
        Assert.Contains("selection", scenario.Description, System.StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(scenario.RunAsync);
    }

    [Fact]
    public void ThrowsForUnknownScenario()
    {
        var error = Assert.Throws<System.InvalidOperationException>(() => SmokeScenarioCatalog.GetOrThrow("missing"));

        Assert.Contains("Unknown scenario", error.Message);
    }

    [Fact]
    public void UsesDebugGameLoadAsDefaultScenario()
    {
        Assert.Equal(SmokeScenarioCatalog.DebugGameLoadScenarioName, SmokeScenarioCatalog.DefaultScenarioName);
    }
}
