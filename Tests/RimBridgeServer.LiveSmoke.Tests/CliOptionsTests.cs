using System;
using System.IO;
using RimBridgeServer.LiveSmoke;
using Xunit;

namespace RimBridgeServer.LiveSmoke.Tests;

public class CliOptionsTests
{
    [Fact]
    public void ParsesHumanVerifyFlagsAndDirectories()
    {
        var reportDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "reports");
        var humanVerifyDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "verify");

        var options = CliOptions.Parse(
        [
            "--scenario", SmokeScenarioCatalog.SaveLoadRoundTripScenarioName,
            "--gabs-bin", "/tmp/gabs",
            "--report-dir", reportDirectory,
            "--human-verify",
            "--human-verify-dir", humanVerifyDirectory,
            "--stop-after"
        ]);

        Assert.Equal(SmokeScenarioCatalog.SaveLoadRoundTripScenarioName, options.Scenario);
        Assert.Equal("/tmp/gabs", options.GabsBinaryPath);
        Assert.Equal(Path.GetFullPath(reportDirectory), options.ReportDirectory);
        Assert.True(options.HumanVerify);
        Assert.Equal(Path.GetFullPath(humanVerifyDirectory), options.HumanVerifyDirectory);
        Assert.True(options.StopAfter);
    }

    [Fact]
    public void DefaultsHumanVerifyDirectoryToDesktop()
    {
        var options = CliOptions.Parse(
        [
            "--scenario", SmokeScenarioCatalog.DebugGameLoadScenarioName,
            "--gabs-bin", "/tmp/gabs"
        ]);

        Assert.False(options.HumanVerify);
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            options.HumanVerifyDirectory);
    }
}
