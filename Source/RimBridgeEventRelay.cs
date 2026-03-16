using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lib.GAB.Events;
using RimBridgeServer.Core;

namespace RimBridgeServer;

internal static class RimBridgeEventRelay
{
    private const string OperationChannel = "rimbridge.operation";
    private const string LogChannel = "rimbridge.log";

    private static readonly object Sync = new();
    private static readonly Dictionary<string, int> PublishedRepeatCountsByEntryId = new(StringComparer.Ordinal);
    private static IEventManager _eventManager;

    public static void Initialize(IEventManager eventManager, OperationJournal operationJournal, LogJournal logJournal)
    {
        if (_eventManager != null)
            return;

        _eventManager = eventManager ?? throw new ArgumentNullException(nameof(eventManager));
        if (operationJournal == null)
            throw new ArgumentNullException(nameof(operationJournal));
        if (logJournal == null)
            throw new ArgumentNullException(nameof(logJournal));

        _eventManager.RegisterChannel(OperationChannel, "Terminal non-diagnostic bridge operation events.");
        _eventManager.RegisterChannel(LogChannel, "Deduplicated warning/error RimWorld and bridge log entries.");

        operationJournal.EventPublished += OnOperationEventPublished;
        logJournal.EntryRecorded += OnLogEntryRecorded;
    }

    private static void OnOperationEventPublished(OperationEventRecord eventRecord)
    {
        if (ShouldEmitOperationEvent(eventRecord) == false)
            return;

        _ = EmitSafeAsync(OperationChannel, new
        {
            type = eventRecord.EventType,
            operationEvent = eventRecord
        }, eventRecord.TimestampUtc);
    }

    private static void OnLogEntryRecorded(BridgeLogEntry entry)
    {
        if (ShouldEmitLogEntry(entry) == false)
            return;

        _ = EmitSafeAsync(LogChannel, new
        {
            type = "log",
            logEntry = entry
        }, entry.TimestampUtc);
    }

    private static bool ShouldEmitOperationEvent(OperationEventRecord eventRecord)
    {
        if (eventRecord == null)
            return false;

        return eventRecord.EventType switch
        {
            "operation.failed" => true,
            "operation.cancelled" => true,
            "operation.timed_out" => true,
            "operation.completed" => eventRecord.CapabilityId.StartsWith("rimbridge.core/diagnostics/", StringComparison.Ordinal) == false,
            _ => false
        };
    }

    private static bool ShouldEmitLogEntry(BridgeLogEntry entry)
    {
        if (entry == null)
            return false;
        if (IsWarningOrHigher(entry.Level) == false)
            return false;

        lock (Sync)
        {
            if (entry.RepeatCount <= 1)
            {
                PublishedRepeatCountsByEntryId[entry.EntryId] = 1;
                return true;
            }

            if (!PublishedRepeatCountsByEntryId.TryGetValue(entry.EntryId, out var lastPublishedRepeatCount))
                lastPublishedRepeatCount = 1;

            if (ShouldPublishRepeatCount(entry.RepeatCount, lastPublishedRepeatCount) == false)
                return false;

            PublishedRepeatCountsByEntryId[entry.EntryId] = entry.RepeatCount;
            return true;
        }
    }

    private static bool ShouldPublishRepeatCount(int repeatCount, int lastPublishedRepeatCount)
    {
        var thresholds = new[] { 2, 5, 10, 25, 50, 100 };
        foreach (var threshold in thresholds)
        {
            if (repeatCount >= threshold && lastPublishedRepeatCount < threshold)
                return true;
        }

        return repeatCount - lastPublishedRepeatCount >= 100;
    }

    private static bool IsWarningOrHigher(string level)
    {
        return string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(level, "error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(level, "fatal", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EmitSafeAsync(string channel, object payload, DateTimeOffset timestampUtc)
    {
        try
        {
            var eventManager = _eventManager;
            if (eventManager == null)
                return;

            await eventManager.EmitEventAsync(channel, payload, timestampUtc);
        }
        catch
        {
        }
    }
}
