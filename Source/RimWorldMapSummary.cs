using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldMapSummary
{
    public static object GetMapSummaryResponse()
    {
        if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
            return new { success = false, message = "No game is currently loaded" };

        var map = RimWorldState.CurrentMapOrThrow();
        var pawns = map.mapPawns;
        // The map manager holds only its own conditions. World conditions live in its parent, and this call walks both.
        var conditions = new List<GameCondition>();
        map.gameConditionManager.GetAllGameConditionsAffectingMap(map, conditions);
        // WealthWatcher recounts on its own only every 5000 ticks, so the stored values can be stale.
        map.wealthWatcher.ForceRecount();
        return new
        {
            success = true,
            mapId = RimWorldState.GetMapId(map),
            mapIndex = map.Index,
            isPlayerHome = map.IsPlayerHome,
            size = new { x = map.Size.x, z = map.Size.z },
            biome = map.Biome?.defName,
            tick = Find.TickManager.TicksGame,
            date = new
            {
                year = GenLocalDate.Year(map),
                quadrum = GenLocalDate.Twelfth(map).GetQuadrum().ToString(),
                // DayOfQuadrum is zero based. The date readout shows it plus one.
                dayOfQuadrum = GenLocalDate.DayOfQuadrum(map) + 1,
                hour = GenLocalDate.HourOfDay(map),
                season = GenLocalDate.Season(map).ToString()
            },
            weather = map.weatherManager?.curWeather?.defName,
            outdoorTemperatureC = map.mapTemperature.OutdoorTemp,
            activeConditions = conditions.Count == 0 ? null : conditions.Select(condition => condition.def?.defName).ToList(),
            wealth = new
            {
                total = map.wealthWatcher.WealthTotal,
                items = map.wealthWatcher.WealthItems,
                buildings = map.wealthWatcher.WealthBuildings,
                pawns = map.wealthWatcher.WealthPawns
            },
            pawnCounts = new
            {
                // FreeColonistsSpawned keeps the colony's slaves. colonists leaves them out so the groups do not overlap.
                colonists = pawns.FreeColonistsSpawned.Count(pawn => pawn.IsSlaveOfColony == false),
                prisoners = pawns.PrisonersOfColonySpawnedCount,
                slaves = pawns.SlavesOfColonySpawned.Count,
                colonyAnimals = pawns.SpawnedColonyAnimals.Count,
                hostiles = pawns.AllPawnsSpawned.Count(pawn => Faction.OfPlayer != null && pawn.HostileTo(Faction.OfPlayer)),
                total = pawns.AllPawnsSpawnedCount
            }
        };
    }
}
