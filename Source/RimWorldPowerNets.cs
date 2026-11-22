using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldPowerNets
{
    public static object ListPowerNetsResponse(string thingId, bool includeTransmitters)
    {
        var map = RimWorldState.CurrentMapOrThrow();
        var nets = map.powerNetManager.AllNetsListForReading;
        IEnumerable<(PowerNet Net, int Index)> selected = nets.Select((net, index) => (net, index));
        if (string.IsNullOrWhiteSpace(thingId) == false)
        {
            var net = RimWorldState.ResolveCurrentMapThing(thingId).TryGetComp<CompPower>()?.PowerNet;
            if (net == null)
                return new { success = false, message = $"Thing '{thingId}' is not connected to a power net." };

            selected = selected.Where(entry => entry.Net == net);
        }

        return new
        {
            success = true,
            mapId = RimWorldState.GetMapId(map),
            netCount = nets.Count,
            unlimitedPower = DebugSettings.unlimitedPower,
            nets = selected.Select(entry => DescribeNet(entry.Net, entry.Index, includeTransmitters)).ToList()
        };
    }

    private static object DescribeNet(PowerNet net, int index, bool includeTransmitters)
    {
        var traders = net.powerComps.Where(trader => trader != null).ToList();
        var batteries = net.batteryComps.Where(battery => battery != null).ToList();
        var buildings = net.transmitters.Concat(net.connectors)
            .Select(comp => comp?.parent)
            .Where(thing => thing != null)
            .Distinct()
            .Select(thing => (Thing: thing, Role: RoleOf(thing)))
            .ToList();

        return new
        {
            netIndex = index,
            hasPowerSource = net.hasPowerSource,
            producedWatts = traders.Where(trader => trader.PowerOn && trader.PowerOutput > 0f).Sum(trader => trader.PowerOutput),
            consumedWatts = traders.Where(trader => trader.PowerOn && trader.PowerOutput < 0f).Sum(trader => -trader.PowerOutput),
            storedEnergyWd = net.CurrentStoredEnergy(),
            storedEnergyCapacityWd = batteries.Sum(battery => battery.Props.storedEnergyMax),
            buildingCount = buildings.Count,
            transmitterCount = buildings.Count(building => building.Role == TransmitterRole),
            buildings = buildings
                .Where(building => includeTransmitters || building.Role != TransmitterRole)
                .Select(building => DescribeBuilding(building.Thing, building.Role))
                .ToList()
        };
    }

    private const string TransmitterRole = "transmitter";

    // A power plant counts as a producer even while its output is 0, for example a solar generator at night.
    private static string RoleOf(ThingWithComps thing)
    {
        if (thing.GetComp<CompPowerBattery>() != null)
            return "battery";

        var trader = thing.GetComp<CompPowerTrader>();
        if (trader == null)
            return TransmitterRole;

        return trader is CompPowerPlant || trader.PowerOutput > 0f ? "producer" : "consumer";
    }

    // powerOutputWatts is the trader's wanted output. It is negative for a consumer, also while powerOn is false.
    private static object DescribeBuilding(ThingWithComps thing, string role)
    {
        var trader = thing.GetComp<CompPowerTrader>();
        var battery = thing.GetComp<CompPowerBattery>();
        return new
        {
            thingId = RimWorldState.GetThingId(thing),
            defName = thing.def?.defName,
            label = thing.LabelCap.ToString(),
            position = thing.Position.IsValid ? new { x = thing.Position.x, z = thing.Position.z } : null,
            role,
            powerOn = trader?.PowerOn,
            powerOutputWatts = trader?.PowerOutput,
            storedEnergyWd = battery?.StoredEnergy,
            storedEnergyMaxWd = battery?.Props.storedEnergyMax
        };
    }
}
