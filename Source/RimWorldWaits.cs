using System;
using System.Collections.Generic;
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
            patches = RimBridgePatches.DescribeStatus(),
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

    public static object WaitForGameLoaded(int timeoutMs = 30000, int pollIntervalMs = 50, string readiness = AutomationReadiness.DefaultTargetName, bool pauseIfNeeded = false)
    {
        return WaitForGameLoadedResult(timeoutMs, pollIntervalMs, readiness, pauseIfNeeded);
    }

    public static Dictionary<string, object> WaitForGameLoadedResult(int timeoutMs = 30000, int pollIntervalMs = 50, string readiness = AutomationReadiness.DefaultTargetName, bool pauseIfNeeded = false)
    {
        if (AutomationReadiness.TryParseTarget(readiness, out var target) == false)
            return CreateInvalidReadinessResponse(readiness);

        var targetReadiness = AutomationReadiness.FormatTarget(target);
        var outcome = WaitUntilMainThreadProbe(() =>
        {
            var status = RimWorldState.ReadStatus();
            var readinessEvaluation = status.Readiness;
            var satisfied = readinessEvaluation.IsSatisfied(target);
            var state = RimWorldState.ToolStateSnapshot(status);

            if (satisfied && pauseIfNeeded && status.HasCurrentGame && status.Paused == false && Find.TickManager != null)
            {
                Find.TickManager.TogglePaused();
                status = RimWorldState.ReadStatus();
                state = RimWorldState.ToolStateSnapshot(status);
            }

            var blockingReason = DescribeBlockingReason(status, readinessEvaluation, target);
            var message = DescribeLoadedWaitState(status, readinessEvaluation, target, pauseIfNeeded, blockingReason);

            return new WaitProbeResult
            {
                IsSatisfied = satisfied,
                Message = message,
                BlockingReason = satisfied ? null : blockingReason,
                Snapshot = state
            };
        }, new WaitOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs,
            HandleProbeException = ex => HandleMainThreadProbeException(ex, "RimWorld main thread was busy while checking game-load readiness. Retrying."),
            TimeoutMessage = $"Timed out waiting for RimWorld readiness '{targetReadiness}'."
        });

        return CreateWaitResponse(outcome, targetReadiness);
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

    public static Dictionary<string, object> WaitForEntrySceneReadyResult(int timeoutMs = 30000, int pollIntervalMs = 50)
    {
        var outcome = WaitUntilMainThreadProbe(() =>
        {
            var status = RimWorldState.ReadStatus();
            var state = RimWorldState.ToolStateSnapshot(status);
            var satisfied = status.InEntryScene
                && string.Equals(status.ProgramState, "Entry", StringComparison.OrdinalIgnoreCase)
                && status.HasCurrentGame == false;

            return new WaitProbeResult
            {
                IsSatisfied = satisfied,
                Message = satisfied
                    ? "RimWorld entry scene can queue a debug game."
                    : "Waiting for RimWorld entry scene to accept a debug-game start.",
                BlockingReason = satisfied ? null : DescribeEntrySceneBlockingReason(status),
                Snapshot = state
            };
        }, new WaitOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs,
            HandleProbeException = ex => HandleMainThreadProbeException(ex, "RimWorld main thread was busy while checking entry-scene readiness. Retrying."),
            TimeoutMessage = "Timed out waiting for RimWorld entry scene readiness."
        });

        return CreateWaitResponse(outcome, "entryScene");
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
        AutomationReadinessTarget target,
        bool pauseIfNeeded,
        string blockingReason)
    {
        if (readiness.IsSatisfied(target) == false)
            return $"Waiting for RimWorld readiness '{AutomationReadiness.FormatTarget(target)}'. {blockingReason}";

        if (pauseIfNeeded)
        {
            return status.Paused
                ? $"RimWorld {DescribeSatisfiedTarget(target)} and is paused for automation."
                : $"RimWorld {DescribeSatisfiedTarget(target)}. The game was not paused for automation.";
        }

        return $"RimWorld {DescribeSatisfiedTarget(target)}.";
    }

    private static string DescribeBlockingReason(
        RimWorldState.RuntimeStatus status,
        AutomationReadinessEvaluation readiness,
        AutomationReadinessTarget target)
    {
        if (readiness.GameDataReady == false)
            return "No current game is loaded.";

        if (target == AutomationReadinessTarget.GameData)
            return string.Empty;

        if (readiness.MapDataReady == false)
            return "No RimWorld map data is available yet.";

        if (target == AutomationReadinessTarget.MapData)
            return string.Empty;

        if (target == AutomationReadinessTarget.CurrentMap && readiness.CurrentMapReady == false)
            return "No current map is active yet.";

        if (target == AutomationReadinessTarget.CurrentMap)
            return string.Empty;

        if (readiness.Playable == false)
        {
            if (string.Equals(status.ProgramState, "Playing", StringComparison.OrdinalIgnoreCase) == false)
                return $"RimWorld programState is {status.ProgramState}, not Playing.";

            if (status.LongEventPending)
                return "RimWorld still has a pending long event.";

            return "RimWorld is not playable yet.";
        }

        if (target == AutomationReadinessTarget.Playable)
            return string.Empty;

        if (readiness.ScreenFadeClear == false)
            return $"RimWorld screen fade is still active (alpha {status.FadeOverlayAlpha:0.###}).";

        return string.Empty;
    }

    private static string DescribeEntrySceneBlockingReason(RimWorldState.RuntimeStatus status)
    {
        if (status.HasCurrentGame)
            return "A game is already loaded.";

        if (string.Equals(status.ProgramState, "Entry", StringComparison.OrdinalIgnoreCase) == false)
            return $"RimWorld programState is {status.ProgramState}, not Entry.";

        if (status.InEntryScene == false)
            return "RimWorld is not in the entry scene.";

        return string.Empty;
    }

    private static string DescribeSatisfiedTarget(AutomationReadinessTarget target)
    {
        return target switch
        {
            AutomationReadinessTarget.GameData => "game data is available",
            AutomationReadinessTarget.MapData => "map data is available",
            AutomationReadinessTarget.CurrentMap => "current map is available",
            AutomationReadinessTarget.Playable => "has a loaded playable game",
            AutomationReadinessTarget.Visual => "has a loaded visual-ready game",
            _ => "is ready"
        };
    }

    private static Dictionary<string, object> CreateWaitResponse(WaitOutcome outcome, string targetReadiness = null)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["success"] = outcome.Satisfied,
            ["satisfied"] = outcome.Satisfied,
            ["message"] = outcome.Message,
            ["targetReadiness"] = targetReadiness,
            ["blockingReason"] = string.IsNullOrWhiteSpace(outcome.BlockingReason) ? null : outcome.BlockingReason,
            ["elapsedMs"] = outcome.ElapsedMs,
            ["attempts"] = outcome.Attempts,
            ["probeFailureCount"] = outcome.ProbeFailureCount,
            ["lastProbeError"] = string.IsNullOrWhiteSpace(outcome.LastProbeError) ? null : outcome.LastProbeError,
            ["state"] = outcome.Snapshot
        };
    }

    private static Dictionary<string, object> CreateInvalidReadinessResponse(string readiness)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["success"] = false,
            ["satisfied"] = false,
            ["message"] = $"Unknown RimWorld readiness '{readiness}'. Supported values: {AutomationReadiness.SupportedTargetNames}.",
            ["targetReadiness"] = readiness,
            ["blockingReason"] = "Invalid readiness target.",
            ["elapsedMs"] = 0L,
            ["attempts"] = 0,
            ["probeFailureCount"] = 0,
            ["lastProbeError"] = null,
            ["state"] = null
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
