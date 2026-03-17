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
}
