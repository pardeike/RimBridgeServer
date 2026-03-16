using System;
using RimBridgeServer.Contracts;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class OperationJournalTests
{
    [Fact]
    public void PublishesLifecycleEventsWhenOperationsChangeState()
    {
        var journal = new OperationJournal();
        OperationEventRecord lastEvent = null;
        journal.EventPublished += eventRecord => lastEvent = eventRecord;

        journal.RecordStarted("op_1", "rimbridge.core/diagnostics/ping");

        Assert.NotNull(lastEvent);
        Assert.Equal("operation.started", lastEvent.EventType);

        journal.RecordCompleted(OperationEnvelope.Completed("op_1", "rimbridge.core/diagnostics/ping", DateTimeOffset.UtcNow, new { message = "pong" }));

        Assert.Equal("operation.completed", lastEvent.EventType);
    }

    [Fact]
    public void StoresSnapshotsWithoutKeepingResultPayloads()
    {
        var journal = new OperationJournal();
        var envelope = OperationEnvelope.Completed("op_2", "rimbridge.core/diagnostics/ping", DateTimeOffset.UtcNow, new { message = "pong" });

        journal.RecordCompleted(envelope);
        var tracked = journal.GetOperation("op_2");

        Assert.NotNull(tracked);
        Assert.Null(tracked.Result);
        Assert.Equal(OperationStatus.Completed, tracked.Status);
    }
}
