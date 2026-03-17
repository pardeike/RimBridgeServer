using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RimWorld;
using RimBridgeServer.Core;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldState
{
    internal readonly struct RuntimeStatus
    {
        public RuntimeStatus(
            string programState,
            bool inEntryScene,
            bool hasCurrentGame,
            bool longEventPending,
            bool paused,
            bool screenFading,
            float fadeOverlayAlpha)
        {
            ProgramState = programState;
            InEntryScene = inEntryScene;
            HasCurrentGame = hasCurrentGame;
            LongEventPending = longEventPending;
            Paused = paused;
            ScreenFading = screenFading;
            FadeOverlayAlpha = fadeOverlayAlpha;
        }

        public string ProgramState { get; }

        public bool InEntryScene { get; }

        public bool HasCurrentGame { get; }

        public bool LongEventPending { get; }

        public bool Paused { get; }

        public bool ScreenFading { get; }

        public float FadeOverlayAlpha { get; }

        public AutomationReadinessEvaluation Readiness =>
            AutomationReadiness.Evaluate(HasCurrentGame, ProgramState, LongEventPending, ScreenFading, FadeOverlayAlpha);
    }

    public static object ToolStateSnapshot()
    {
        return ToolStateSnapshot(ReadStatus());
    }

    public static RuntimeStatus ReadStatus()
    {
        var hasCurrentGame = Current.Game != null;
        var paused = hasCurrentGame && Find.TickManager?.Paused == true;
        var screenFading = ScreenFader.IsFading();
        var fadeOverlayAlpha = Mathf.Clamp01(ScreenFader.CurrentInstantColor().a);

        return new RuntimeStatus(
            Current.ProgramState.ToString(),
            GenScene.InEntryScene,
            hasCurrentGame,
            LongEventHandler.AnyEventNowOrWaiting,
            paused,
            screenFading,
            fadeOverlayAlpha);
    }

    public static object ToolStateSnapshot(RuntimeStatus status)
    {
        var readiness = status.Readiness;
        var currentMap = Find.CurrentMap;

        return new
        {
            programState = status.ProgramState,
            inEntryScene = status.InEntryScene,
            hasCurrentGame = status.HasCurrentGame,
            currentMapId = GetMapId(currentMap),
            currentMapIndex = currentMap?.Index,
            longEventPending = status.LongEventPending,
            paused = status.Paused,
            screenFading = status.ScreenFading,
            fadeOverlayAlpha = Math.Round(status.FadeOverlayAlpha, 4),
            screenFadeClear = readiness.ScreenFadeClear,
            playable = readiness.Playable,
            automationReady = readiness.AutomationReady
        };
    }

    public static Map CurrentMapOrThrow()
    {
        var map = Find.CurrentMap;
        if (map == null)
            throw new InvalidOperationException("No current map is active.");

        return map;
    }

    public static List<Pawn> CurrentMapColonists(Map map)
    {
        return map.mapPawns.FreeColonistsSpawned
            .OrderBy(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort)
            .ToList();
    }

    public static List<Pawn> AllPlayerColonists()
    {
        if (Current.Game == null)
            return [];

        return Find.Maps
            .Where(map => map != null)
            .SelectMany(map => map.mapPawns.FreeColonistsSpawned)
            .Distinct()
            .OrderBy(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort)
            .ToList();
    }

    public static List<Pawn> AllCurrentMapPawns(Map map)
    {
        return map.mapPawns.AllPawnsSpawned
            .OrderBy(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort)
            .ToList();
    }

    public static Pawn ResolveColonist(string pawnName = null, string pawnId = null)
    {
        return ResolvePawn(pawnName, pawnId, AllPlayerColonists(), "player-controlled colonist");
    }

    public static Pawn ResolveCurrentMapPawn(string pawnName = null, string pawnId = null)
    {
        return ResolvePawn(pawnName, pawnId, AllCurrentMapPawns(CurrentMapOrThrow()), "current-map pawn");
    }

    public static Pawn ResolveSelectedPawn(string pawnName = null, string pawnId = null)
    {
        return ResolvePawn(pawnName, pawnId, Find.Selector.SelectedPawns, "selected pawn");
    }

    public static Vector3 CellCenter(IntVec3 cell)
    {
        return new Vector3(cell.x + 0.5f, 0f, cell.z + 0.5f);
    }

    public static IEnumerable<string> ParseNames(string pawnNamesCsv)
    {
        if (string.IsNullOrWhiteSpace(pawnNamesCsv))
            yield break;

        var separators = new[] { ',', ';', '|', '\n' };
        foreach (var part in pawnNamesCsv.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

    public static string GetThingId(Thing thing)
    {
        return thing?.GetUniqueLoadID();
    }

    public static string GetMapId(Map map)
    {
        return map?.GetUniqueLoadID();
    }

    public static string SanitizeName(string name, string fallbackPrefix)
    {
        var raw = string.IsNullOrWhiteSpace(name)
            ? $"{fallbackPrefix}_{DateTime.Now:yyyyMMdd_HHmmss}"
            : name.Trim();

        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    public static object DescribePawn(Pawn pawn)
    {
        return new
        {
            pawnId = GetThingId(pawn),
            thingIdNumber = pawn.thingIDNumber,
            name = pawn.Name?.ToStringShort ?? pawn.LabelShort,
            fullName = pawn.Name?.ToStringFull ?? pawn.LabelCap,
            label = pawn.LabelCap,
            selected = Find.Selector.IsSelected(pawn),
            drafted = pawn.drafter?.Drafted ?? false,
            downed = pawn.Downed,
            dead = pawn.Dead,
            spawned = pawn.Spawned,
            map = pawn.Map?.Index.ToString(CultureInfo.InvariantCulture),
            mapId = GetMapId(pawn.Map),
            mapIndex = pawn.Map?.Index,
            position = pawn.Position.IsValid ? new { x = pawn.Position.x, z = pawn.Position.z } : null,
            job = pawn.CurJob?.def?.defName,
            mentalState = pawn.MentalStateDef?.defName,
            faction = pawn.Faction?.Name
        };
    }

    public static object DescribeCamera()
    {
        var driver = Find.CameraDriver;
        var currentMap = Find.CurrentMap;

        return new
        {
            success = true,
            map = currentMap?.Index.ToString(CultureInfo.InvariantCulture),
            mapId = GetMapId(currentMap),
            mapIndex = currentMap?.Index,
            rootSize = driver.RootSize,
            zoomRootSize = driver.ZoomRootSize,
            zoomRange = driver.CurrentZoom.ToString(),
            mapPosition = new { x = driver.MapPosition.x, z = driver.MapPosition.z },
            viewRect = new
            {
                minX = driver.CurrentViewRect.minX,
                minZ = driver.CurrentViewRect.minZ,
                maxX = driver.CurrentViewRect.maxX,
                maxZ = driver.CurrentViewRect.maxZ,
                width = driver.CurrentViewRect.Width,
                height = driver.CurrentViewRect.Height
            }
        };
    }

    public static float ComputeFrameRootSize(IEnumerable<Pawn> pawns)
    {
        var list = pawns.Where(pawn => pawn.Spawned).ToList();
        if (list.Count == 0)
            return Find.CameraDriver.RootSize;

        var minX = list.Min(pawn => pawn.Position.x);
        var maxX = list.Max(pawn => pawn.Position.x);
        var minZ = list.Min(pawn => pawn.Position.z);
        var maxZ = list.Max(pawn => pawn.Position.z);
        var span = Mathf.Max(maxX - minX + 1, maxZ - minZ + 1);

        return Mathf.Clamp(span * 0.9f + 12f, 8f, 140f);
    }

    private static Pawn ResolvePawn(string pawnName, string pawnId, IEnumerable<Pawn> source, string description)
    {
        var pawns = source
            .Where(pawn => pawn != null)
            .Distinct()
            .ToList();

        if (string.IsNullOrWhiteSpace(pawnId) == false)
        {
            var normalizedId = pawnId.Trim();
            var idMatches = pawns
                .Where(pawn => string.Equals(GetThingId(pawn), normalizedId, StringComparison.Ordinal))
                .ToList();
            if (idMatches.Count == 1)
                return idMatches[0];
            if (idMatches.Count > 1)
                throw new InvalidOperationException($"Ambiguous {description} id '{pawnId}'.");

            throw new InvalidOperationException($"Could not find {description} id '{pawnId}'.");
        }

        if (string.IsNullOrWhiteSpace(pawnName))
            throw new ArgumentException($"A {description} name or id is required.");

        var normalized = pawnName.Trim();

        var exactMatches = pawns.Where(pawn => MatchesPawnName(pawn, normalized, exact: true)).ToList();
        if (exactMatches.Count == 1)
            return exactMatches[0];
        if (exactMatches.Count > 1)
            throw new InvalidOperationException($"Ambiguous {description} name '{pawnName}'. Matches: {string.Join(", ", exactMatches.Select(NameForDisplay))}");

        var fuzzyMatches = pawns.Where(pawn => MatchesPawnName(pawn, normalized, exact: false)).ToList();
        if (fuzzyMatches.Count == 1)
            return fuzzyMatches[0];
        if (fuzzyMatches.Count > 1)
            throw new InvalidOperationException($"Ambiguous {description} name '{pawnName}'. Matches: {string.Join(", ", fuzzyMatches.Select(NameForDisplay))}");

        throw new InvalidOperationException($"Could not find {description} '{pawnName}'.");
    }

    private static bool MatchesPawnName(Pawn pawn, string query, bool exact)
    {
        var candidates = new[]
        {
            pawn.Name?.ToStringShort,
            pawn.Name?.ToStringFull,
            pawn.LabelShort,
            pawn.LabelCap
        }
        .Where(text => string.IsNullOrWhiteSpace(text) == false);

        return exact
            ? candidates.Any(candidate => string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase))
            : candidates.Any(candidate => candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string NameForDisplay(Pawn pawn)
    {
        return pawn.Name?.ToStringShort ?? pawn.LabelShort;
    }
}
