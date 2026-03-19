using System;
using System.Collections;
using System.Linq;
using Verse;

namespace RimBridgeServer;

internal sealed class SelectionCapabilityModule
{
    public object ListColonists(bool currentMapOnly = false)
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded" };

        var pawns = currentMapOnly
            ? RimWorldState.CurrentMapColonists(RimWorldState.CurrentMapOrThrow())
            : RimWorldState.AllPlayerColonists();

        return new
        {
            success = true,
            count = pawns.Count,
            colonists = pawns.Select(RimWorldState.DescribePawn).ToList()
        };
    }

    public object ClearSelection()
    {
        Find.Selector.ClearSelection();
        return new { success = true, selectedCount = Find.Selector.NumSelected };
    }

    public object SelectPawn(string pawnName = null, string pawnId = null, bool append = false)
    {
        var pawn = RimWorldState.ResolveColonist(pawnName, pawnId);
        if (!append)
            Find.Selector.ClearSelection();

        Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);

        return new
        {
            success = true,
            selected = RimWorldState.DescribePawn(pawn),
            selectedCount = Find.Selector.NumSelected
        };
    }

    public object DeselectPawn(string pawnName = null, string pawnId = null)
    {
        var pawn = RimWorldState.ResolveSelectedPawn(pawnName, pawnId);
        Find.Selector.Deselect(pawn);

        return new
        {
            success = true,
            deselected = pawn.Name?.ToStringShort ?? pawn.LabelShort,
            deselectedPawn = RimWorldState.DescribePawn(pawn),
            selectedCount = Find.Selector.NumSelected
        };
    }

    public object SetDraft(string pawnName = null, string pawnId = null, bool drafted = true)
    {
        var pawn = RimWorldState.ResolveColonist(pawnName, pawnId);
        if (pawn.drafter == null)
            return new { success = false, message = $"Pawn '{(string.IsNullOrWhiteSpace(pawnId) ? pawnName : pawnId)}' cannot be drafted." };

        pawn.drafter.Drafted = drafted;

        return new
        {
            success = true,
            pawn = pawn.Name?.ToStringShort ?? pawn.LabelShort,
            pawnInfo = RimWorldState.DescribePawn(pawn),
            drafted = pawn.drafter.Drafted
        };
    }

    public object GetSelectedPawnInventoryState()
    {
        var selectedPawns = Find.Selector.SelectedPawns
            .Where(pawn => pawn != null)
            .Distinct()
            .ToList();

        if (selectedPawns.Count != 1)
        {
            return new
            {
                success = false,
                error = "No single pawn is selected."
            };
        }

        var pawn = selectedPawns[0];

        object DescribeInventoryThing(Thing thing)
        {
            if (thing == null)
                return null;

            return new
            {
                thingId = RimWorldState.GetThingId(thing),
                defName = thing.def?.defName,
                label = thing.LabelCapNoCount.ToString(),
                stackCount = thing.stackCount
            };
        }

        var inventoryItems = pawn.inventory?.innerContainer?
            .Select(DescribeInventoryThing)
            .Where(item => item != null)
            .ToArray()
            ?? [];

        string[] hauledInventoryThingIds = [];
        var comp = pawn.AllComps?.FirstOrDefault(candidate => candidate?.GetType().FullName == "PickUpAndHaul.CompHauledToInventory");
        if (comp != null)
        {
            var getHashSet = comp.GetType().GetMethod("GetHashSet", Type.EmptyTypes);
            if (getHashSet?.Invoke(comp, null) is IEnumerable trackedThings)
            {
                hauledInventoryThingIds = trackedThings
                    .OfType<Thing>()
                    .Select(RimWorldState.GetThingId)
                    .Where(id => string.IsNullOrWhiteSpace(id) == false)
                    .ToArray();
            }
        }

        return new
        {
            success = true,
            pawnId = RimWorldState.GetThingId(pawn),
            pawnName = pawn.Name?.ToStringShort ?? pawn.LabelShort,
            currentJob = pawn.jobs?.curJob?.def?.defName,
            currentJobReport = pawn.jobs?.curJob?.GetReport(pawn),
            carriedThing = DescribeInventoryThing(pawn.carryTracker?.CarriedThing),
            inventoryCount = inventoryItems.Length,
            inventoryItems,
            hauledInventoryThingIds
        };
    }
}
