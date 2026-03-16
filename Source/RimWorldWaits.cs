using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldWaits
{
    private static readonly ConditionWaiter Waiter = new();
    private static readonly MainThreadDispatcher Dispatcher = new();

    public static object GetBridgeStatus(OperationJournal journal)
    {
        var state = SnapshotState();
        return new
        {
            success = true,
            state,
            recentOperationCount = journal.GetRecentOperations(200).Count,
            recentEventCount = journal.GetRecentEvents(200).Count
        };
    }

    public static object WaitForGameLoaded(int timeoutMs = 30000, int pollIntervalMs = 100)
    {
        var outcome = Waiter.WaitUntil(() => Dispatcher.Invoke(() =>
        {
            var state = RimWorldState.ToolStateSnapshot();
            var satisfied = Current.Game != null
                && LongEventHandler.AnyEventNowOrWaiting == false
                && Current.ProgramState == ProgramState.Playing;

            return new WaitProbeResult
            {
                IsSatisfied = satisfied,
                Message = satisfied
                    ? "RimWorld has a loaded playable game."
                    : "Waiting for RimWorld to finish loading a playable game.",
                Snapshot = state
            };
        }, timeoutMs: 2000), new WaitOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs,
            TimeoutMessage = "Timed out waiting for RimWorld to load a playable game."
        });

        return CreateWaitResponse(outcome);
    }

    public static object WaitForLongEventIdle(int timeoutMs = 30000, int pollIntervalMs = 100)
    {
        var outcome = Waiter.WaitUntil(() => Dispatcher.Invoke(() =>
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
        }, timeoutMs: 2000), new WaitOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs,
            TimeoutMessage = "Timed out waiting for RimWorld long events to finish."
        });

        return CreateWaitResponse(outcome);
    }

    private static object SnapshotState()
    {
        return Dispatcher.Invoke(RimWorldState.ToolStateSnapshot, timeoutMs: 2000);
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
            state = outcome.Snapshot
        };
    }
}
