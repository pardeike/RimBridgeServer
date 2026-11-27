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
        return new
        {
            thingId = RimWorldState.GetThingId(building),
            defName = def?.defName,
            frameBuildDefName = (building as Frame)?.BuildDef?.defName,
            label = building.LabelCap.ToString(),
            position = building.Position.IsValid ? new { x = building.Position.x, z = building.Position.z } : null,
            rotation = def?.rotatable == true ? RimWorldArchitect.DescribeRotation(building.Rotation) : null,
            size = def == null ? null : new { x = def.size.x, z = def.size.z },
            faction = building.Faction?.Name,
            factionIsPlayer = building.Faction?.IsPlayer ?? false,
            hitPoints = def?.useHitPoints == true ? building.HitPoints : (int?)null,
            maxHitPoints = def?.useHitPoints == true ? building.MaxHitPoints : (int?)null,
            powerOn = trader?.PowerOn
        };
    }
}
