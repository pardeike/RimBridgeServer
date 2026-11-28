using System;
using System.Linq;
using RimBridgeServer.Core;
using RimWorld;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldBuildings
{
    public static object ListBuildingsResponse(string defName, string category, bool playerOnly, int offset)
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded" };

        var map = RimWorldState.CurrentMapOrThrow();
        var wantedDef = defName?.Trim();
        var wantedCategory = category?.Trim();
        // An unknown filter value fails, so that a typo does not look like a map without such buildings.
        if (string.IsNullOrEmpty(wantedDef) == false
            && DefDatabase<ThingDef>.AllDefsListForReading.Any(def => string.Equals(def.defName, wantedDef, StringComparison.OrdinalIgnoreCase)) == false)
            return new { success = false, message = $"No ThingDef named '{defName}'." };
        if (string.IsNullOrEmpty(wantedCategory) == false
            && DefDatabase<DesignationCategoryDef>.AllDefsListForReading.Any(def => ArchitectDesignatorIds.CategoryMatches(wantedCategory, def.defName)) == false)
            return new { success = false, message = $"Unknown architect category '{category}'. Use an id from rimworld/list_architect_categories or a DesignationCategoryDef name." };
        var buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
            .OfType<Building>()
            .Where(building => string.IsNullOrEmpty(wantedDef) || string.Equals(building.def?.defName, wantedDef, StringComparison.OrdinalIgnoreCase))
            .Where(building => string.IsNullOrEmpty(wantedCategory) || CategoryMatches(building, wantedCategory))
            .Where(building => playerOnly == false || building.Faction?.IsPlayer == true)
            .OrderBy(building => building.def?.defName, StringComparer.Ordinal)
            .ThenBy(building => RimWorldState.GetThingId(building), StringComparer.Ordinal)
            .ToList();
        var rows = ResponseBudget.TakePage(buildings, offset, DescribeBuilding, out var page);

        return new
        {
            success = true,
            mapId = RimWorldState.GetMapId(map),
            buildings = rows,
            page
        };
    }

    // A building without an architect category, such as a frame or a ruin, never matches a category filter.
    private static bool CategoryMatches(Building building, string category)
    {
        var categoryDefName = building.def?.designationCategory?.defName;
        return string.IsNullOrEmpty(categoryDefName) == false && ArchitectDesignatorIds.CategoryMatches(category, categoryDefName);
    }

    private static object DescribeBuilding(Building building)
    {
        var def = building.def;
        var trader = building.GetComp<CompPowerTrader>();
        var occupied = building.OccupiedRect();
        return new
        {
            thingId = RimWorldState.GetThingId(building),
            defName = def?.defName,
            frameBuildDefName = (building as Frame)?.BuildDef?.defName,
            label = building.LabelCap.ToString(),
            position = building.Position.IsValid ? new { x = building.Position.x, z = building.Position.z } : null,
            rotation = def?.rotatable == true ? RimWorldArchitect.DescribeRotation(building.Rotation) : null,
            // The covered cells after rotation, in the x, z, width and height form of rimworld/get_cells_info.
            occupiedRect = new { x = occupied.minX, z = occupied.minZ, width = occupied.Width, height = occupied.Height },
            faction = building.Faction?.Name,
            factionIsPlayer = building.Faction?.IsPlayer ?? false,
            hitPoints = def?.useHitPoints == true ? building.HitPoints : (int?)null,
            maxHitPoints = def?.useHitPoints == true ? building.MaxHitPoints : (int?)null,
            powerOn = trader?.PowerOn
        };
    }
}
