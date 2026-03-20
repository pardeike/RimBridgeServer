using RimBridgeServer.Contracts;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class AttentionAggregatorTests
{
    [Fact]
    public void IgnoresNonBlockingLogLevels()
    {
        var aggregator = new AttentionAggregator();

        var info = aggregator.RecordLog(new BridgeLogEntry
        {
            Sequence = 1,
            EntryId = "log_info",
            Level = "info",
            Message = "Informational entry"
        }, diagnosticsCursor: 1);
        var warning = aggregator.RecordLog(new BridgeLogEntry
        {
            Sequence = 2,
            EntryId = "log_warning",
            Level = "warning",
            Message = "Warning entry"
        }, diagnosticsCursor: 2);

        Assert.Null(info);
        Assert.Null(warning);
        Assert.Null(aggregator.GetCurrent());
    }

    [Fact]
    public void OpensBlockingAttentionForErrorLogs()
    {
        var aggregator = new AttentionAggregator();

        var snapshot = aggregator.RecordLog(new BridgeLogEntry
        {
            Sequence = 42,
            EntryId = "log_error",
            Level = "error",
            Message = "Selection no longer exists.",
            OperationId = "op_7",
            CapabilityId = "rimworld/select_thing"
        }, diagnosticsCursor: 42);

        Assert.NotNull(snapshot);
        Assert.Equal("attn_42", snapshot.AttentionId);
        Assert.Equal("open", snapshot.State);
        Assert.Equal("error", snapshot.Severity);
        Assert.True(snapshot.Blocking);
        Assert.True(snapshot.StateInvalidated);
        Assert.Equal(41, snapshot.DiagnosticsCursor);
        Assert.Equal("op_7", snapshot.CausalOperationId);
        Assert.Equal("rimworld/select_thing", snapshot.CausalMethod);
        Assert.Equal(1, snapshot.TotalUrgentEntries);
        Assert.Single(snapshot.Sample);
        Assert.Contains("Selection no longer exists.", snapshot.Summary);
    }

    [Fact]
    public void RepeatedLogEntriesAccumulateByDelta()
    {
        var aggregator = new AttentionAggregator();

        aggregator.RecordLog(new BridgeLogEntry
        {
            Sequence = 10,
            EntryId = "log_repeat",
            Level = "error",
            Message = "Repeated failure",
            RepeatCount = 1
        }, diagnosticsCursor: 10);

        var snapshot = aggregator.RecordLog(new BridgeLogEntry
        {
            Sequence = 11,
            EntryId = "log_repeat",
            Level = "error",
            Message = "Repeated failure",
            RepeatCount = 5
        }, diagnosticsCursor: 11);

        Assert.NotNull(snapshot);
        Assert.Equal(5, snapshot.TotalUrgentEntries);
        Assert.Single(snapshot.Sample);
        Assert.Equal(5, snapshot.Sample[0].RepeatCount);
        Assert.Equal(11, snapshot.Sample[0].LatestSequence);
    }

    [Fact]
    public void ResetStartsANewAttentionEpoch()
    {
        var aggregator = new AttentionAggregator();

        var first = aggregator.RecordOperationEvent(new OperationEventRecord
        {
            Sequence = 7,
            EventType = "operation.failed",
            OperationId = "op_first",
            CapabilityId = "rimworld/select_pawn",
            ErrorMessage = "Pawn was despawned."
        }, diagnosticsCursor: 20);

        aggregator.Reset();

        var second = aggregator.RecordOperationEvent(new OperationEventRecord
        {
            Sequence = 9,
            EventType = "operation.timed_out",
            OperationId = "op_second",
            CapabilityId = "rimworld/select_pawn",
            ErrorMessage = "Timed out waiting for selector."
        }, diagnosticsCursor: 25);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("attn_7", first.AttentionId);
        Assert.Equal("attn_9", second.AttentionId);
        Assert.Equal("fatal", second.Severity);
        Assert.Equal(24, second.DiagnosticsCursor);
        Assert.Equal("op_second", second.CausalOperationId);
        Assert.Equal("rimworld/select_pawn", second.CausalMethod);
    }

    [Fact]
    public void IgnoresNonTerminalOperationEvents()
    {
        var aggregator = new AttentionAggregator();

        var snapshot = aggregator.RecordOperationEvent(new OperationEventRecord
        {
            Sequence = 12,
            EventType = "operation.completed",
            OperationId = "op_ok",
            CapabilityId = "rimworld/get_game_info"
        }, diagnosticsCursor: 12);

        Assert.Null(snapshot);
        Assert.Null(aggregator.GetCurrent());
    }
}
