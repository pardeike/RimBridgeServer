using System;
using System.Linq;
using Verse;

namespace RimBridgeServer;

internal sealed class DiagnosticsCapabilityModule
{
    public object Ping()
    {
        return new { message = "pong", timestamp = DateTime.UtcNow };
    }

    public object GetGameInfo()
    {
        var currentGame = Current.Game;
        if (currentGame == null)
        {
            return new { status = "no_game", message = "No game is currently loaded" };
        }

        return new
        {
            status = "game_loaded",
            ticksGame = currentGame.tickManager.TicksGame,
            mapCount = currentGame.Maps?.Count ?? 0,
            selectedPawns = Find.Selector.SelectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList()
        };
    }
}
