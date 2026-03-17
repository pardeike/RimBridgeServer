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
    public void GetOperationRetainsBoundedResultSnapshots()
    {
        var journal = new OperationJournal();
        var envelope = OperationEnvelope.Completed("op_2", "rimbridge.core/diagnostics/ping", DateTimeOffset.UtcNow, new { message = "pong" });

        journal.RecordCompleted(envelope);
        var tracked = journal.GetOperation("op_2");
        var listed = journal.GetRecentOperations(1);

        Assert.NotNull(tracked);
        Assert.True(tracked.HasResult);
        Assert.NotNull(tracked.Result);
        Assert.True(listed[0].HasResult);
        Assert.Null(listed[0].Result);
        Assert.Equal(OperationStatus.Completed, tracked.Status);
    }

    [Fact]
    public void FiltersEventsBySequenceAndOperationId()
    {
        var journal = new OperationJournal();

        journal.RecordStarted("op_1", "rimbridge.core/diagnostics/ping");
        var firstSequence = journal.LatestEventSequence;
        journal.RecordCompleted(OperationEnvelope.Completed("op_1", "rimbridge.core/diagnostics/ping", DateTimeOffset.UtcNow, new { message = "pong" }));
        journal.RecordStarted("op_2", "rimbridge.core/diagnostics/ping");
        journal.RecordCompleted(OperationEnvelope.Completed("op_2", "rimbridge.core/diagnostics/ping", DateTimeOffset.UtcNow, new { message = "pong 2" }));

        var recent = journal.GetRecentEvents(limit: 10, afterSequence: firstSequence, operationId: "op_1");

        Assert.Single(recent);
        Assert.True(recent[0].Sequence > firstSequence);
        Assert.Equal("operation.completed", recent[0].EventType);
        Assert.Equal("op_1", recent[0].OperationId);
        Assert.True(recent[0].HasResult);
    }
}
