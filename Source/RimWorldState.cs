using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldState
{
    public static object ToolStateSnapshot()
    {
        return new
        {
            programState = Current.ProgramState.ToString(),
            inEntryScene = GenScene.InEntryScene,
            hasCurrentGame = Current.Game != null,
            longEventPending = LongEventHandler.AnyEventNowOrWaiting
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

    public static Pawn ResolveColonist(string pawnName)
    {
        return ResolvePawn(pawnName, AllPlayerColonists(), "player-controlled colonist");
    }

    public static Pawn ResolveCurrentMapPawn(string pawnName)
    {
        return ResolvePawn(pawnName, AllCurrentMapPawns(CurrentMapOrThrow()), "current-map pawn");
    }

    public static Pawn ResolveSelectedPawn(string pawnName)
    {
        return ResolvePawn(pawnName, Find.Selector.SelectedPawns, "selected pawn");
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
            name = pawn.Name?.ToStringShort ?? pawn.LabelShort,
            fullName = pawn.Name?.ToStringFull ?? pawn.LabelCap,
            label = pawn.LabelCap,
            selected = Find.Selector.IsSelected(pawn),
            drafted = pawn.drafter?.Drafted ?? false,
            downed = pawn.Downed,
            dead = pawn.Dead,
            spawned = pawn.Spawned,
            map = pawn.Map?.Index.ToString(CultureInfo.InvariantCulture),
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

    private static Pawn ResolvePawn(string pawnName, IEnumerable<Pawn> source, string description)
    {
        if (string.IsNullOrWhiteSpace(pawnName))
            throw new ArgumentException($"A {description} name is required.", nameof(pawnName));

        var normalized = pawnName.Trim();
        var pawns = source
            .Where(pawn => pawn != null)
            .Distinct()
            .ToList();

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
