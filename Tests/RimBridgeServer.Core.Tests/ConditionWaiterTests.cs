using System;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class ConditionWaiterTests
{
    [Fact]
    public void ReturnsSuccessWhenProbeEventuallySatisfiesCondition()
    {
        var waiter = new ConditionWaiter();
        var attempts = 0;

        var outcome = waiter.WaitUntil(() =>
        {
            attempts++;
            return new WaitProbeResult
            {
                IsSatisfied = attempts >= 3,
                Message = attempts >= 3 ? "ready" : "waiting",
                Snapshot = attempts
            };
        }, new WaitOptions
        {
            TimeoutMs = 1000,
            PollIntervalMs = 1
        });

        Assert.True(outcome.Satisfied);
        Assert.Equal(3, outcome.Attempts);
        Assert.Equal("ready", outcome.Message);
        Assert.Equal(3, outcome.Snapshot);
    }

    [Fact]
    public void ReturnsTimeoutWithLastSnapshotWhenConditionNeverSatisfies()
    {
        var waiter = new ConditionWaiter();
        var attempts = 0;

        var outcome = waiter.WaitUntil(() =>
        {
            attempts++;
            return new WaitProbeResult
            {
                IsSatisfied = false,
                Message = "still waiting",
                Snapshot = attempts
            };
        }, new WaitOptions
        {
            TimeoutMs = 5,
            PollIntervalMs = 1,
            TimeoutMessage = "timed out"
        });

        Assert.False(outcome.Satisfied);
        Assert.True(outcome.Attempts >= 1);
        Assert.Equal("timed out", outcome.Message);
        Assert.Equal(attempts, outcome.Snapshot);
    }

    [Fact]
    public void CanRetryAfterHandledProbeExceptions()
    {
        var waiter = new ConditionWaiter();
        var attempts = 0;

        var outcome = waiter.WaitUntil(() =>
        {
            attempts++;
            if (attempts == 1)
                throw new TimeoutException("main thread busy");

            return new WaitProbeResult
            {
                IsSatisfied = attempts >= 3,
                Message = attempts >= 3 ? "ready" : "waiting",
                Snapshot = attempts
            };
        }, new WaitOptions
        {
            TimeoutMs = 1000,
            PollIntervalMs = 1,
            HandleProbeException = ex => ex is TimeoutException
                ? new WaitProbeResult
                {
                    IsSatisfied = false,
                    Message = "retrying after timeout"
                }
                : null
        });

        Assert.True(outcome.Satisfied);
        Assert.Equal(3, outcome.Attempts);
        Assert.Equal(1, outcome.ProbeFailureCount);
        Assert.Equal(3, outcome.Snapshot);
    }
}
