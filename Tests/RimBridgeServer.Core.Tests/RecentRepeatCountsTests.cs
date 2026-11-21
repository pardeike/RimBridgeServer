using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class RecentRepeatCountsTests
{
    [Fact]
    public void KeepsAWindowOfRecentEntriesAndForgetsOlderOnes()
    {
        var counts = new RecentRepeatCounts();
        for (var index = 0; index < RecentRepeatCounts.Capacity + 10; index++)
            counts.Set("log_" + index, 1);

        Assert.Equal(RecentRepeatCounts.Capacity, counts.Count);
        Assert.False(counts.TryGet("log_0", out _));
        Assert.True(counts.TryGet("log_" + (RecentRepeatCounts.Capacity + 9), out _));
    }

    [Fact]
    public void AnOlderEntryStillUpdatesAfterANewerOneArrived()
    {
        var counts = new RecentRepeatCounts();
        counts.Set("log_a", 2);
        counts.Set("log_b", 1);
        counts.Set("log_a", 3);

        Assert.True(counts.TryGet("log_a", out var repeatCount));
        Assert.Equal(3, repeatCount);
        Assert.Equal(2, counts.Count);
    }
}
