using System;
using System.Collections.Generic;
using System.Linq;
using RimBridgeServer.Contracts;

namespace RimBridgeServer.Core;

public sealed class OperationEventRecord
{
    public long Sequence { get; set; }

    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string OperationId { get; set; } = string.Empty;

    public string CapabilityId { get; set; } = string.Empty;

    public OperationStatus Status { get; set; }

    public bool Success { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public string ErrorCode { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public Dictionary<string, object> Metadata { get; set; } = [];
}

public sealed class OperationJournal
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OperationEnvelope> _operationsById = new(StringComparer.Ordinal);
    private readonly List<string> _operationOrder = [];
    private readonly List<OperationEventRecord> _events = [];
    private readonly int _maxOperations;
    private readonly int _maxEvents;
    private long _nextEventSequence = 1;

    public OperationJournal(int maxOperations = 200, int maxEvents = 500)
    {
        if (maxOperations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxOperations));
        if (maxEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEvents));

        _maxOperations = maxOperations;
        _maxEvents = maxEvents;
    }

    public event Action<OperationEventRecord> EventPublished;

    public long LatestEventSequence
    {
        get
        {
            lock (_gate)
            {
                return _nextEventSequence - 1;
            }
        }
    }

    public OperationEnvelope RecordStarted(string operationId, string capabilityId, IDictionary<string, object> metadata = null, DateTimeOffset? startedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("An operation id is required.", nameof(operationId));
        if (string.IsNullOrWhiteSpace(capabilityId))
            throw new ArgumentException("A capability id is required.", nameof(capabilityId));

        var snapshot = new OperationEnvelope
        {
            OperationId = operationId,
            CapabilityId = capabilityId,
            Status = OperationStatus.Running,
            Success = false,
            StartedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow,
            Metadata = metadata != null
                ? new Dictionary<string, object>(metadata, StringComparer.Ordinal)
                : []
        };

        StoreSnapshot(snapshot);
        Publish(CreateEvent("operation.started", snapshot));
        return CloneEnvelope(snapshot);
    }

    public OperationEnvelope RecordCompleted(OperationEnvelope envelope)
    {
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));

        var snapshot = CloneEnvelope(envelope);
        StoreSnapshot(snapshot);
        Publish(CreateEvent(GetEventType(snapshot), snapshot));
        return CloneEnvelope(snapshot);
    }

    public OperationEnvelope GetOperation(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("An operation id is required.", nameof(operationId));

        lock (_gate)
        {
            return _operationsById.TryGetValue(operationId, out var snapshot)
                ? CloneEnvelope(snapshot)
                : null;
        }
    }

    public IReadOnlyList<OperationEnvelope> GetRecentOperations(int limit = 20)
    {
        if (limit <= 0)
            return [];

        lock (_gate)
        {
            return _operationOrder
                .Take(limit)
                .Select(id => CloneEnvelope(_operationsById[id]))
                .ToList();
        }
    }

    public IReadOnlyList<OperationEventRecord> GetRecentEvents(int limit = 50, string eventType = null, long afterSequence = 0)
    {
        if (limit <= 0)
            return [];

        lock (_gate)
        {
            IEnumerable<OperationEventRecord> query = _events;
            if (string.IsNullOrWhiteSpace(eventType) == false)
                query = query.Where(entry => string.Equals(entry.EventType, eventType, StringComparison.Ordinal));
            if (afterSequence > 0)
                query = query.Where(entry => entry.Sequence > afterSequence);

            return query
                .Take(limit)
                .Select(CloneEvent)
                .ToList();
        }
    }

    private void StoreSnapshot(OperationEnvelope envelope)
    {
        OperationEnvelope snapshot;

        lock (_gate)
        {
            snapshot = MergeWithExisting(envelope);
            _operationsById[snapshot.OperationId] = snapshot;
            PromoteOperation(snapshot.OperationId);
            TrimOperations();
        }
    }

    private OperationEnvelope MergeWithExisting(OperationEnvelope envelope)
    {
        if (!_operationsById.TryGetValue(envelope.OperationId, out var existing))
            return CloneEnvelope(envelope);

        var snapshot = CloneEnvelope(envelope);
        foreach (var pair in existing.Metadata)
        {
            if (!snapshot.Metadata.ContainsKey(pair.Key))
                snapshot.Metadata[pair.Key] = pair.Value;
        }

        return snapshot;
    }

    private void PromoteOperation(string operationId)
    {
        _operationOrder.Remove(operationId);
        _operationOrder.Insert(0, operationId);
    }

    private void TrimOperations()
    {
        while (_operationOrder.Count > _maxOperations)
        {
            var removeId = _operationOrder[_operationOrder.Count - 1];
            _operationOrder.RemoveAt(_operationOrder.Count - 1);
            _operationsById.Remove(removeId);
        }
    }

    private void Publish(OperationEventRecord eventRecord)
    {
        Action<OperationEventRecord> handler;

        lock (_gate)
        {
            eventRecord.Sequence = _nextEventSequence++;
            _events.Insert(0, eventRecord);
            if (_events.Count > _maxEvents)
                _events.RemoveRange(_maxEvents, _events.Count - _maxEvents);

            handler = EventPublished;
        }

        handler?.Invoke(CloneEvent(eventRecord));
    }

    private static string GetEventType(OperationEnvelope envelope)
    {
        return envelope.Status switch
        {
            OperationStatus.Completed => "operation.completed",
            OperationStatus.Failed => "operation.failed",
            OperationStatus.Cancelled => "operation.cancelled",
            OperationStatus.TimedOut => "operation.timed_out",
            _ => "operation.updated"
        };
    }

    private static OperationEventRecord CreateEvent(string eventType, OperationEnvelope envelope)
    {
        return new OperationEventRecord
        {
            EventId = "evt_" + Guid.NewGuid().ToString("N"),
            EventType = eventType,
            OperationId = envelope.OperationId,
            CapabilityId = envelope.CapabilityId,
            Status = envelope.Status,
            Success = envelope.Success,
            TimestampUtc = envelope.CompletedAtUtc ?? envelope.StartedAtUtc,
            ErrorCode = envelope.Error?.Code ?? string.Empty,
            ErrorMessage = envelope.Error?.Message ?? string.Empty,
            Metadata = new Dictionary<string, object>(envelope.Metadata, StringComparer.Ordinal)
        };
    }

    private static OperationEnvelope CloneEnvelope(OperationEnvelope envelope)
    {
        return new OperationEnvelope
        {
            OperationId = envelope.OperationId,
            CapabilityId = envelope.CapabilityId,
            Status = envelope.Status,
            Success = envelope.Success,
            StartedAtUtc = envelope.StartedAtUtc,
            CompletedAtUtc = envelope.CompletedAtUtc,
            DurationMs = envelope.DurationMs,
            Result = null,
            Error = envelope.Error == null
                ? null
                : new OperationError
                {
                    Code = envelope.Error.Code,
                    Message = envelope.Error.Message,
                    ExceptionType = envelope.Error.ExceptionType,
                    Details = envelope.Error.Details
                },
            Warnings = envelope.Warnings.Select(warning => new OperationWarning
            {
                Code = warning.Code,
                Message = warning.Message,
                Details = warning.Details
            }).ToList(),
            Metadata = new Dictionary<string, object>(envelope.Metadata, StringComparer.Ordinal)
        };
    }

    private static OperationEventRecord CloneEvent(OperationEventRecord eventRecord)
    {
        return new OperationEventRecord
        {
            Sequence = eventRecord.Sequence,
            EventId = eventRecord.EventId,
            EventType = eventRecord.EventType,
            OperationId = eventRecord.OperationId,
            CapabilityId = eventRecord.CapabilityId,
            Status = eventRecord.Status,
            Success = eventRecord.Success,
            TimestampUtc = eventRecord.TimestampUtc,
            ErrorCode = eventRecord.ErrorCode,
            ErrorMessage = eventRecord.ErrorMessage,
            Metadata = new Dictionary<string, object>(eventRecord.Metadata, StringComparer.Ordinal)
        };
    }
}
