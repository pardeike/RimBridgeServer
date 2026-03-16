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

    public object SelectPawn(string pawnName, bool append = false)
    {
        var pawn = RimWorldState.ResolveColonist(pawnName);
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

    public object DeselectPawn(string pawnName)
    {
        var pawn = RimWorldState.ResolveSelectedPawn(pawnName);
        Find.Selector.Deselect(pawn);

        return new
        {
            success = true,
            deselected = pawn.Name?.ToStringShort ?? pawn.LabelShort,
            selectedCount = Find.Selector.NumSelected
        };
    }

    public object SetDraft(string pawnName, bool drafted = true)
    {
        var pawn = RimWorldState.ResolveColonist(pawnName);
        if (pawn.drafter == null)
            return new { success = false, message = $"Pawn '{pawnName}' cannot be drafted." };

        pawn.drafter.Drafted = drafted;

        return new
        {
            success = true,
            pawn = pawn.Name?.ToStringShort ?? pawn.LabelShort,
            drafted = pawn.drafter.Drafted
        };
    }
}
