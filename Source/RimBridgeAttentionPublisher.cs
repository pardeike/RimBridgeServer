using System;
using System.Linq;
using System.Threading.Tasks;
using Lib.GAB.Attention;
using RimBridgeServer.Core;

namespace RimBridgeServer;

internal sealed class RimBridgeAttentionPublisher
{
    private readonly IAttentionManager _attentionManager;
    private readonly AttentionAggregator _aggregator = new();
    private readonly LogJournal _logJournal;

    public RimBridgeAttentionPublisher(IAttentionManager attentionManager, OperationJournal operationJournal, LogJournal logJournal)
    {
        _attentionManager = attentionManager ?? throw new ArgumentNullException(nameof(attentionManager));
        if (operationJournal == null)
            throw new ArgumentNullException(nameof(operationJournal));
        _logJournal = logJournal ?? throw new ArgumentNullException(nameof(logJournal));

        if (_attentionManager.IsEnabled == false)
            return;

        operationJournal.EventPublished += OnOperationEventPublished;
        logJournal.EntryRecorded += OnLogEntryRecorded;
    }

    private void OnLogEntryRecorded(BridgeLogEntry entry)
    {
        if (entry == null)
            return;

        ResetAggregatorIfAttentionWasCleared();
        var snapshot = _aggregator.RecordLog(entry, diagnosticsCursor: entry.Sequence);
        if (snapshot == null)
            return;

        _ = PublishSafeAsync(snapshot, entry.TimestampUtc);
    }

    private void OnOperationEventPublished(OperationEventRecord eventRecord)
    {
        if (eventRecord == null)
            return;

        ResetAggregatorIfAttentionWasCleared();
        var snapshot = _aggregator.RecordOperationEvent(eventRecord, diagnosticsCursor: _logJournal.LatestSequence);
        if (snapshot == null)
            return;

        _ = PublishSafeAsync(snapshot, eventRecord.TimestampUtc);
    }

    private void ResetAggregatorIfAttentionWasCleared()
    {
        var managerCurrent = _attentionManager.GetCurrent();
        var aggregateCurrent = _aggregator.GetCurrent();
        if (aggregateCurrent == null)
            return;

        if (managerCurrent == null || string.Equals(managerCurrent.AttentionId, aggregateCurrent.AttentionId, StringComparison.Ordinal) == false)
            _aggregator.Reset();
    }

    private async Task PublishSafeAsync(BridgeAttentionSnapshot snapshot, DateTimeOffset timestampUtc)
    {
        try
        {
            await _attentionManager.PublishAsync(ToAttentionItem(snapshot), timestampUtc);
        }
        catch
        {
        }
    }

    private static AttentionItem ToAttentionItem(BridgeAttentionSnapshot snapshot)
    {
        return new AttentionItem
        {
            AttentionId = snapshot.AttentionId,
            State = snapshot.State,
            Severity = snapshot.Severity,
            Blocking = snapshot.Blocking,
            StateInvalidated = snapshot.StateInvalidated,
            Summary = snapshot.Summary,
            CausalOperationId = NullIfEmpty(snapshot.CausalOperationId),
            CausalMethod = NullIfEmpty(snapshot.CausalMethod),
            OpenedAtSequence = snapshot.OpenedAtSequence,
            LatestSequence = snapshot.LatestSequence,
            DiagnosticsCursor = snapshot.DiagnosticsCursor,
            TotalUrgentEntries = snapshot.TotalUrgentEntries,
            Sample = snapshot.Sample.Select(entry => new AttentionSample
            {
                Level = entry.Level,
                Message = entry.Message,
                RepeatCount = entry.RepeatCount,
                LatestSequence = entry.LatestSequence
            }).ToList()
        };
    }

    private static string NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
