using System;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldWaits
{
    private static readonly ConditionWaiter Waiter = new();
    private static readonly MainThreadDispatcher Dispatcher = new();

    public static object GetBridgeStatus(OperationJournal journal, LogJournal logJournal)
    {
        var state = SnapshotState();
        return new
        {
            success = true,
            state,
            recentOperationCount = journal.OperationCount,
            trackedOperationCount = journal.OperationCount,
            operationJournalCapacity = journal.MaxOperations,
            retainedResultCount = journal.RetainedResultCount,
            retainedResultCapacity = journal.MaxRetainedResults,
            recentEventCount = journal.EventCount,
            eventJournalCapacity = journal.MaxEvents,
            recentLogCount = logJournal.EntryCount,
            logJournalCapacity = logJournal.MaxEntries,
            latestOperationEventSequence = journal.LatestEventSequence,
            latestLogSequence = logJournal.LatestSequence
        };
    }

    public static object WaitForGameLoaded(int timeoutMs = 30000, int pollIntervalMs = 100, bool waitForScreenFade = true, bool pauseIfNeeded = false)
    {
        var outcome = WaitUntilMainThreadProbe(() =>
        {
            var status = RimWorldState.ReadStatus();
            var readiness = status.Readiness;
            var satisfied = readiness.Playable && (waitForScreenFade == false || readiness.ScreenFadeClear);
            var state = RimWorldState.ToolStateSnapshot(status);

            if (satisfied && pauseIfNeeded && status.HasCurrentGame && status.Paused == false && Find.TickManager != null)
            {
                Find.TickManager.TogglePaused();
                status = RimWorldState.ReadStatus();
                state = RimWorldState.ToolStateSnapshot(status);
            }

            var message = DescribeLoadedWaitState(status, readiness, waitForScreenFade, pauseIfNeeded);

            return new WaitProbeResult
            {
                IsSatisfied = satisfied,
                Message = message,
                Snapshot = state
            };
        }, new WaitOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs,
            HandleProbeException = ex => HandleMainThreadProbeException(ex, "RimWorld main thread was busy while checking game-load readiness. Retrying."),
            TimeoutMessage = waitForScreenFade
                ? "Timed out waiting for RimWorld to become automation-ready after loading."
                : "Timed out waiting for RimWorld to load a playable game."
        });

        return CreateWaitResponse(outcome);
    }

    public static object WaitForLongEventIdle(int timeoutMs = 30000, int pollIntervalMs = 100)
    {
        var outcome = WaitUntilMainThreadProbe(() =>
        {
            var state = RimWorldState.ToolStateSnapshot();
            var satisfied = LongEventHandler.AnyEventNowOrWaiting == false;

            return new WaitProbeResult
            {
                IsSatisfied = satisfied,
                Message = satisfied
                    ? "RimWorld is idle."
                    : "Waiting for RimWorld long events to finish.",
                Snapshot = state
            };
        }, new WaitOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs,
            HandleProbeException = ex => HandleMainThreadProbeException(ex, "RimWorld main thread was busy while checking long-event state. Retrying."),
            TimeoutMessage = "Timed out waiting for RimWorld long events to finish."
        });

        return CreateWaitResponse(outcome);
    }

    private static object SnapshotState()
    {
        return Dispatcher.Invoke(RimWorldState.ToolStateSnapshot, timeoutMs: 5000);
    }

    private static WaitOutcome WaitUntilMainThreadProbe(Func<WaitProbeResult> probe, WaitOptions options)
    {
        return Waiter.WaitUntil(
            () => Dispatcher.Invoke(probe, timeoutMs: ResolveProbeTimeout(options.TimeoutMs)),
            options);
    }

    private static int ResolveProbeTimeout(int waitTimeoutMs)
    {
        if (waitTimeoutMs <= 0)
            return 0;

        return System.Math.Min(waitTimeoutMs, 10000);
    }

    private static string DescribeLoadedWaitState(
        RimWorldState.RuntimeStatus status,
        AutomationReadinessEvaluation readiness,
        bool waitForScreenFade,
        bool pauseIfNeeded)
    {
        if (readiness.Playable == false)
            return "Waiting for RimWorld to finish loading a playable game.";

        if (waitForScreenFade && readiness.ScreenFadeClear == false)
            return $"Waiting for RimWorld screen fade to finish (alpha {status.FadeOverlayAlpha:0.###}).";

        if (pauseIfNeeded)
        {
            return status.Paused
                ? "RimWorld has a loaded automation-ready game and is paused for automation."
                : "RimWorld has a loaded automation-ready game. The game was not yet paused for automation.";
        }

        return waitForScreenFade
            ? "RimWorld has a loaded automation-ready game."
            : "RimWorld has a loaded playable game.";
    }

    private static object CreateWaitResponse(WaitOutcome outcome)
    {
        return new
        {
            success = outcome.Satisfied,
            satisfied = outcome.Satisfied,
            message = outcome.Message,
            elapsedMs = outcome.ElapsedMs,
            attempts = outcome.Attempts,
            probeFailureCount = outcome.ProbeFailureCount,
            lastProbeError = string.IsNullOrWhiteSpace(outcome.LastProbeError) ? null : outcome.LastProbeError,
            state = outcome.Snapshot
        };
    }

    private static WaitProbeResult HandleMainThreadProbeException(System.Exception _, string message)
    {
        return new WaitProbeResult
        {
            IsSatisfied = false,
            Message = message
        };
    }
}
