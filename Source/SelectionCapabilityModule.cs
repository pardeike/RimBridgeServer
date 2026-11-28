using System;
using System.Collections;
using System.Linq;
using RimWorld;
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

    public object ListPawns(string role = null, int offset = 0)
    {
        return RimWorldPawns.ListPawnsResponse(role, offset);
    }

    public object ClearSelection()
    {
        Find.Selector.ClearSelection();
        return new { success = true, selectedCount = Find.Selector.NumSelected };
    }

    public object SelectPawn(string pawnName = null, string pawnId = null, bool append = false)
    {
        // As a click in the game, this selects any spawned pawn of the current map: a colonist, an animal, a mech,
        // a visitor or an enemy. The selector ignores a pawn on another map.
        var pawn = RimWorldState.ResolveCurrentMapPawn(pawnName, pawnId);
        if (!append)
            Find.Selector.ClearSelection();

        Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
        var selected = Find.Selector.IsSelected(pawn);

        return new
        {
            success = selected,
            message = selected ? null : $"RimWorld did not select '{pawn.LabelShort}'.",
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
        var pawn = RimWorldState.ResolveDraftablePawn(pawnName, pawnId);
        var label = pawn.Name?.ToStringShort ?? pawn.LabelShort;
        if (pawn.drafter.Drafted == drafted)
        {
            return new
            {
                success = true,
                changed = false,
                pawn = label,
                pawnInfo = RimWorldState.DescribePawn(pawn),
                drafted
            };
        }

        // Use the draft toggle that the game shows for the pawn. Its rules then apply: a downed or deathresting pawn,
        // a mech out of its mechanitor's range or bandwidth, and a pawn in a ritual or a ceremony that forbids drafting
        // cannot be drafted. A pawn in a mental state gets no toggle.
        var toggle = pawn.GetGizmos()
            .OfType<Command_Toggle>()
            .FirstOrDefault(command => command.hotKey == KeyBindingDefOf.Command_ColonistDraft && command.toggleAction != null);
        if (toggle == null)
        {
            // Pawn_DraftController.ShowDraftGizmo hides the toggle of a colony mech that no mechanitor controls.
            var reason = ModsConfig.BiotechActive && pawn.IsColonyMech && pawn.GetMechControlGroup() == null
                ? $"RimWorld shows no draft toggle for '{label}', because no mechanitor controls this mech."
                : $"RimWorld shows no draft toggle for '{label}' now. Only a colonist, a colony mech or a colony ghoul under player control can be drafted, for example not one in a mental state.";
            return new
            {
                success = false,
                message = reason,
                pawnInfo = RimWorldState.DescribePawn(pawn),
                drafted = pawn.drafter.Drafted
            };
        }

        if (toggle.Disabled)
        {
            return new
            {
                success = false,
                message = $"RimWorld does not allow drafting '{label}' now: {toggle.disabledReason}",
                disabledReason = toggle.disabledReason,
                pawnInfo = RimWorldState.DescribePawn(pawn),
                drafted = pawn.drafter.Drafted
            };
        }

        toggle.toggleAction();
        return new
        {
            success = pawn.drafter.Drafted == drafted,
            changed = true,
            pawn = label,
            pawnInfo = RimWorldState.DescribePawn(pawn),
            drafted = pawn.drafter.Drafted
        };
    }

    public object GetPawnDetails(string pawnName = null, string pawnId = null)
    {
        return RimWorldPawnDetails.GetPawnDetailsResponse(pawnName, pawnId);
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
