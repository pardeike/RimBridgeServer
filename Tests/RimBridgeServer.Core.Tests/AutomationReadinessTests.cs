using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class AutomationReadinessTests
{
    [Fact]
    public void MarksPlayingNonFadingGameAsAutomationReady()
    {
        var result = AutomationReadiness.Evaluate(
            hasCurrentGame: true,
            mapCount: 1,
            hasCurrentMap: true,
            programState: "Playing",
            longEventPending: false,
            screenFading: false,
            fadeOverlayAlpha: 0f);

        Assert.True(result.GameDataReady);
        Assert.True(result.MapDataReady);
        Assert.True(result.CurrentMapReady);
        Assert.True(result.Playable);
        Assert.True(result.ScreenFadeClear);
        Assert.True(result.VisualReady);
        Assert.True(result.AutomationReady);
    }

    [Fact]
    public void RejectsAutomationReadyWhileScreenFadeIsActive()
    {
        var result = AutomationReadiness.Evaluate(
            hasCurrentGame: true,
            mapCount: 1,
            hasCurrentMap: true,
            programState: "Playing",
            longEventPending: false,
            screenFading: true,
            fadeOverlayAlpha: 0.35f);

        Assert.True(result.Playable);
        Assert.False(result.ScreenFadeClear);
        Assert.False(result.VisualReady);
        Assert.False(result.AutomationReady);
    }

    [Fact]
    public void MapDataCanBeReadyWhileLongEventIsPending()
    {
        var result = AutomationReadiness.Evaluate(
            hasCurrentGame: true,
            mapCount: 1,
            hasCurrentMap: false,
            programState: "MapInitializing",
            longEventPending: true,
            screenFading: false,
            fadeOverlayAlpha: 0f);

        Assert.True(result.GameDataReady);
        Assert.True(result.MapDataReady);
        Assert.False(result.CurrentMapReady);
        Assert.False(result.Playable);
        Assert.True(result.ScreenFadeClear);
        Assert.False(result.AutomationReady);
    }

    [Fact]
    public void CurrentMapRequiresActiveCurrentMap()
    {
        var result = AutomationReadiness.Evaluate(
            hasCurrentGame: true,
            mapCount: 1,
            hasCurrentMap: false,
            programState: "Playing",
            longEventPending: false,
            screenFading: false,
            fadeOverlayAlpha: 0f);

        Assert.True(result.MapDataReady);
        Assert.False(result.CurrentMapReady);
    }

    [Theory]
    [InlineData("gameData", AutomationReadinessTarget.GameData)]
    [InlineData("mapData", AutomationReadinessTarget.MapData)]
    [InlineData("currentMap", AutomationReadinessTarget.CurrentMap)]
    [InlineData("playable", AutomationReadinessTarget.Playable)]
    [InlineData("visual", AutomationReadinessTarget.Visual)]
    [InlineData("automationReady", AutomationReadinessTarget.Visual)]
    [InlineData("", AutomationReadinessTarget.MapData)]
    public void ParsesReadinessTargets(string value, AutomationReadinessTarget expected)
    {
        Assert.True(AutomationReadiness.TryParseTarget(value, out var target));
        Assert.Equal(expected, target);
    }

    [Fact]
    public void RejectsUnknownReadinessTarget()
    {
        Assert.False(AutomationReadiness.TryParseTarget("not-ready", out _));
    }
}
