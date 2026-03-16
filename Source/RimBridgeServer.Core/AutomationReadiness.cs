using System;

namespace RimBridgeServer.Core;

public readonly struct AutomationReadinessEvaluation
{
    public AutomationReadinessEvaluation(bool playable, bool screenFadeClear, bool automationReady)
    {
        Playable = playable;
        ScreenFadeClear = screenFadeClear;
        AutomationReady = automationReady;
    }

    public bool Playable { get; }

    public bool ScreenFadeClear { get; }

    public bool AutomationReady { get; }
}

public static class AutomationReadiness
{
    public const float FadeOverlayReadyThreshold = 0.01f;

    public static AutomationReadinessEvaluation Evaluate(
        bool hasCurrentGame,
        string programState,
        bool longEventPending,
        bool screenFading,
        float fadeOverlayAlpha)
    {
        var playable = hasCurrentGame
            && longEventPending == false
            && string.Equals(programState, "Playing", StringComparison.OrdinalIgnoreCase);
        var screenFadeClear = screenFading == false && fadeOverlayAlpha <= FadeOverlayReadyThreshold;

        return new AutomationReadinessEvaluation(
            playable,
            screenFadeClear,
            playable && screenFadeClear);
    }
}
