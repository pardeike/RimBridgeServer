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
        // The wealth getters recount when the last count is more than 5000 ticks old, as for the game's own displays.
        // A forced recount would also move the game's recount schedule, which the storyteller's threat points read.
        var roles = pawns.AllPawnsSpawned.GroupBy(RimWorldPawns.RoleOf).ToDictionary(group => group.Key, group => group.Count());
        int CountRole(string role) => roles.TryGetValue(role, out var count) ? count : 0;
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
            // The role counts use the roles of rimworld/list_pawns, so they add up to total.
            pawnCounts = new
            {
                colonists = CountRole("colonist"),
                prisoners = CountRole("prisoner"),
                slaves = CountRole("slave"),
                animals = CountRole("animal"),
                mechs = CountRole("mech"),
                hostiles = CountRole("hostile"),
                others = CountRole("other"),
                colonyAnimals = pawns.SpawnedColonyAnimals.Count,
                total = pawns.AllPawnsSpawnedCount
            }
        };
    }
}
