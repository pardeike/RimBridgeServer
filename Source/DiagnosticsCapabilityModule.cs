using System;
using System.Linq;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal sealed class DiagnosticsCapabilityModule
{
    private readonly OperationJournal _journal;
    private readonly ConditionWaiter _waiter = new();

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

    public object GetBridgeStatus()
    {
        return RimWorldWaits.GetBridgeStatus(_journal);
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

    public object WaitForOperation(string operationId, int timeoutMs = 10000, int pollIntervalMs = 50)
    {
        var outcome = _waiter.WaitUntil(() =>
        {
            var operation = _journal.GetOperation(operationId);
            if (operation == null)
            {
                return new WaitProbeResult
                {
                    IsSatisfied = false,
                    Message = $"Waiting for operation '{operationId}' to appear in the journal."
                };
            }

            var satisfied = operation.Status is Contracts.OperationStatus.Completed
                or Contracts.OperationStatus.Failed
                or Contracts.OperationStatus.Cancelled
                or Contracts.OperationStatus.TimedOut;

            return new WaitProbeResult
            {
                IsSatisfied = satisfied,
                Message = satisfied
                    ? $"Operation '{operationId}' reached status {operation.Status}."
                    : $"Waiting for operation '{operationId}' to reach a terminal status.",
                Snapshot = operation
            };
        }, new WaitOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs,
            TimeoutMessage = $"Timed out waiting for operation '{operationId}'."
        });

        return new
        {
            success = outcome.Satisfied,
            satisfied = outcome.Satisfied,
            message = outcome.Message,
            elapsedMs = outcome.ElapsedMs,
            attempts = outcome.Attempts,
            trackedOperation = outcome.Snapshot
        };
    }

    public object WaitForGameLoaded(int timeoutMs = 30000, int pollIntervalMs = 100)
    {
        return RimWorldWaits.WaitForGameLoaded(timeoutMs, pollIntervalMs);
    }

    public object WaitForLongEventIdle(int timeoutMs = 30000, int pollIntervalMs = 100)
    {
        return RimWorldWaits.WaitForLongEventIdle(timeoutMs, pollIntervalMs);
    }
}
