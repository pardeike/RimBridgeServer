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
            programState: "Playing",
            longEventPending: false,
            screenFading: false,
            fadeOverlayAlpha: 0f);

        Assert.True(result.Playable);
        Assert.True(result.ScreenFadeClear);
        Assert.True(result.AutomationReady);
    }

    [Fact]
    public void RejectsAutomationReadyWhileScreenFadeIsActive()
    {
        var result = AutomationReadiness.Evaluate(
            hasCurrentGame: true,
            programState: "Playing",
            longEventPending: false,
            screenFading: true,
            fadeOverlayAlpha: 0.35f);

        Assert.True(result.Playable);
        Assert.False(result.ScreenFadeClear);
        Assert.False(result.AutomationReady);
    }

    [Fact]
    public void RejectsPlayableStateWhenLongEventIsPending()
    {
        var result = AutomationReadiness.Evaluate(
            hasCurrentGame: true,
            programState: "Playing",
            longEventPending: true,
            screenFading: false,
            fadeOverlayAlpha: 0f);

        Assert.False(result.Playable);
        Assert.True(result.ScreenFadeClear);
        Assert.False(result.AutomationReady);
    }
}
