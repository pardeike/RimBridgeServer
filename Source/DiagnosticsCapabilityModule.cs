using System;
using System.Linq;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal sealed class DiagnosticsCapabilityModule
{
    private readonly OperationJournal _journal;
    private readonly LogJournal _logJournal;
    private readonly ConditionWaiter _waiter = new();

    public DiagnosticsCapabilityModule(OperationJournal journal, LogJournal logJournal)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _logJournal = logJournal ?? throw new ArgumentNullException(nameof(logJournal));
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
        return RimWorldWaits.GetBridgeStatus(_journal, _logJournal);
    }

    public object ListOperations(int limit = 20, bool includeResults = false)
    {
        return new
        {
            operations = _journal.GetRecentOperations(limit, includeResults)
        };
    }

    public object ListOperationEvents(int limit = 50, string eventType = null, long afterSequence = 0, string operationId = null, bool includeDiagnostics = false)
    {
        var events = _journal.GetRecentEvents(Math.Max(limit * 4, limit), eventType, afterSequence, operationId);
        if (includeDiagnostics == false)
        {
            events = events
                .Where(entry => entry.CapabilityId.StartsWith("rimbridge.core/diagnostics/", StringComparison.Ordinal) == false)
                .ToList();
        }

        return new
        {
            events = events.Take(limit).ToList()
        };
    }

    public object ListLogs(int limit = 50, string minimumLevel = "info", long afterSequence = 0, string operationId = null, string rootOperationId = null, string capabilityId = null)
    {
        return new
        {
            logs = _logJournal.GetEntries(limit, minimumLevel, afterSequence, operationId, rootOperationId, capabilityId)
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

    public object WaitForGameLoaded(int timeoutMs = 30000, int pollIntervalMs = 100, bool waitForScreenFade = true, bool pauseIfNeeded = false)
    {
        return RimWorldWaits.WaitForGameLoaded(timeoutMs, pollIntervalMs, waitForScreenFade, pauseIfNeeded);
    }

    public object WaitForLongEventIdle(int timeoutMs = 30000, int pollIntervalMs = 100)
    {
        return RimWorldWaits.WaitForLongEventIdle(timeoutMs, pollIntervalMs);
    }
}
