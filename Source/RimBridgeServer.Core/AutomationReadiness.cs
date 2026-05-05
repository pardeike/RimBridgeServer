using System;

namespace RimBridgeServer.Core;

public enum AutomationReadinessTarget
{
    GameData,
    MapData,
    CurrentMap,
    Playable,
    Visual
}

public readonly struct AutomationReadinessEvaluation
{
    public AutomationReadinessEvaluation(
        bool gameDataReady,
        bool mapDataReady,
        bool currentMapReady,
        bool playable,
        bool screenFadeClear,
        bool visualReady)
    {
        GameDataReady = gameDataReady;
        MapDataReady = mapDataReady;
        CurrentMapReady = currentMapReady;
        Playable = playable;
        ScreenFadeClear = screenFadeClear;
        VisualReady = visualReady;
    }

    public bool GameDataReady { get; }

    public bool MapDataReady { get; }

    public bool CurrentMapReady { get; }

    public bool Playable { get; }

    public bool ScreenFadeClear { get; }

    public bool VisualReady { get; }

    public bool AutomationReady => VisualReady;

    public bool IsSatisfied(AutomationReadinessTarget target)
    {
        return target switch
        {
            AutomationReadinessTarget.GameData => GameDataReady,
            AutomationReadinessTarget.MapData => MapDataReady,
            AutomationReadinessTarget.CurrentMap => CurrentMapReady,
            AutomationReadinessTarget.Playable => Playable,
            AutomationReadinessTarget.Visual => VisualReady,
            _ => false
        };
    }
}

public static class AutomationReadiness
{
    public const float FadeOverlayReadyThreshold = 0.01f;

    public const string DefaultTargetName = "mapData";

    public static AutomationReadinessEvaluation Evaluate(
        bool hasCurrentGame,
        int mapCount,
        bool hasCurrentMap,
        string programState,
        bool longEventPending,
        bool screenFading,
        float fadeOverlayAlpha)
    {
        var gameDataReady = hasCurrentGame;
        var mapDataReady = hasCurrentGame && mapCount > 0;
        var currentMapReady = hasCurrentGame && hasCurrentMap;
        var playable = hasCurrentGame
            && longEventPending == false
            && string.Equals(programState, "Playing", StringComparison.OrdinalIgnoreCase);
        var screenFadeClear = screenFading == false && fadeOverlayAlpha <= FadeOverlayReadyThreshold;

        return new AutomationReadinessEvaluation(
            gameDataReady,
            mapDataReady,
            currentMapReady,
            playable,
            screenFadeClear,
            playable && screenFadeClear);
    }

    public static bool TryParseTarget(string value, out AutomationReadinessTarget target)
    {
        var normalized = NormalizeTargetName(value);
        target = normalized switch
        {
            "" => AutomationReadinessTarget.MapData,
            "gamedata" => AutomationReadinessTarget.GameData,
            "mapdata" => AutomationReadinessTarget.MapData,
            "currentmap" => AutomationReadinessTarget.CurrentMap,
            "playable" => AutomationReadinessTarget.Playable,
            "visual" => AutomationReadinessTarget.Visual,
            "visualready" => AutomationReadinessTarget.Visual,
            "automationready" => AutomationReadinessTarget.Visual,
            "automation" => AutomationReadinessTarget.Visual,
            _ => default
        };

        return normalized is "" or "gamedata" or "mapdata" or "currentmap" or "playable" or "visual" or "visualready" or "automationready" or "automation";
    }

    public static string FormatTarget(AutomationReadinessTarget target)
    {
        return target switch
        {
            AutomationReadinessTarget.GameData => "gameData",
            AutomationReadinessTarget.MapData => "mapData",
            AutomationReadinessTarget.CurrentMap => "currentMap",
            AutomationReadinessTarget.Playable => "playable",
            AutomationReadinessTarget.Visual => "visual",
            _ => DefaultTargetName
        };
    }

    public static string SupportedTargetNames => "gameData, mapData, currentMap, playable, visual";

    private static string NormalizeTargetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }
}
