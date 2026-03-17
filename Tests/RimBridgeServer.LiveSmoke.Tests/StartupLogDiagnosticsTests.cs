using System;
using System.IO;
using RimBridgeServer.LiveSmoke;
using Xunit;

namespace RimBridgeServer.LiveSmoke.Tests;

public class StartupLogDiagnosticsTests
{
    [Fact]
    public void DetectsRimBridgeStartupFailureMarkers()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var playerLogPath = Path.Combine(directory, "Player.log");
        File.WriteAllText(playerLogPath, """
            RimWorld 1.6
            [RimBridge] STARTUP_OPTIONAL_PATCH_FAILURE: RimBridgeServer.Widgets_ButtonInvisibleDraggable_UiWorkbench_Patch: HarmonyLib.HarmonyException: broken patch
              at Some.Stack.Trace()
            """);

        var result = StartupLogDiagnostics.Inspect(playerLogPath, tailLineCount: 20, excerptLineCount: 4);

        Assert.True(result.FailureDetected);
        Assert.Single(result.Diagnostics);
        Assert.Equal("[RimBridge] STARTUP_OPTIONAL_PATCH_FAILURE:", result.Diagnostics[0].Marker);
        Assert.Contains("HarmonyLib.HarmonyException", result.Diagnostics[0].Summary, StringComparison.Ordinal);
        Assert.Contains("Some.Stack.Trace()", result.Diagnostics[0].Excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresLogsWithoutRimBridgeStartupMarkers()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var playerLogPath = Path.Combine(directory, "Player.log");
        File.WriteAllText(playerLogPath, """
            RimWorld 1.6
            Fallback handler could not load library foo
            Normal startup message
            """);

        var result = StartupLogDiagnostics.Inspect(playerLogPath, tailLineCount: 20, excerptLineCount: 4);

        Assert.False(result.FailureDetected);
        Assert.Empty(result.Diagnostics);
    }
}
