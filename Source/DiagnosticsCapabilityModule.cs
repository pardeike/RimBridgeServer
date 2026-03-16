using System;
using System.Linq;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal sealed class DiagnosticsCapabilityModule
{
    private readonly OperationJournal _journal;

    public DiagnosticsCapabilityModule(OperationJournal journal)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

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

    public object GetOperation(string operationId)
    {
        var operation = _journal.GetOperation(operationId);
        if (operation == null)
            return new { success = false, message = $"Operation '{operationId}' was not found in the journal." };

        return new
        {
            success = true,
            trackedOperation = operation
        };
    }

    public object ListOperations(int limit = 20)
    {
        return new
        {
            operations = _journal.GetRecentOperations(limit)
        };
    }

    public object ListOperationEvents(int limit = 50)
    {
        return new
        {
            events = _journal.GetRecentEvents(limit)
        };
    }
}
