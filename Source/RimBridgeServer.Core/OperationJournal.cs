using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
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

    public bool HasResult { get; set; }

    public bool ResultWasTruncated { get; set; }

    public int WarningCount { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = [];
}

public sealed class OperationJournal
{
    private sealed class OperationRecord
    {
        public OperationEnvelope Summary { get; set; }

        public object RetainedResult { get; set; }

        public bool ResultWasTruncated { get; set; }

        public DateTimeOffset? RetainedAtUtc { get; set; }
    }

    private sealed class SnapshotContext
    {
        public SnapshotContext(int maxDepth, int maxCollectionItems, int maxStringLength)
        {
            MaxDepth = maxDepth;
            MaxCollectionItems = maxCollectionItems;
            MaxStringLength = maxStringLength;
        }

        public int MaxDepth { get; }

        public int MaxCollectionItems { get; }

        public int MaxStringLength { get; }

        public bool Truncated { get; set; }

        public HashSet<object> Path { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, OperationRecord> _operationsById = new(StringComparer.Ordinal);
    private readonly List<string> _operationOrder = [];
    private readonly List<string> _retainedResultOrder = [];
    private readonly List<OperationEventRecord> _events = [];
    private readonly int _maxOperations;
    private readonly int _maxEvents;
    private readonly int _maxRetainedResults;
    private readonly int _maxSnapshotDepth;
    private readonly int _maxSnapshotCollectionItems;
    private readonly int _maxSnapshotStringLength;
    private long _nextEventSequence = 1;

    public OperationJournal(
        int maxOperations = 1000,
        int maxEvents = 5000,
        int maxRetainedResults = 50,
        int maxSnapshotDepth = 6,
        int maxSnapshotCollectionItems = 64,
        int maxSnapshotStringLength = 1024)
    {
        if (maxOperations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxOperations));
        if (maxEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEvents));
        if (maxRetainedResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetainedResults));
        if (maxSnapshotDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSnapshotDepth));
        if (maxSnapshotCollectionItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSnapshotCollectionItems));
        if (maxSnapshotStringLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSnapshotStringLength));

        _maxOperations = maxOperations;
        _maxEvents = maxEvents;
        _maxRetainedResults = maxRetainedResults;
        _maxSnapshotDepth = maxSnapshotDepth;
        _maxSnapshotCollectionItems = maxSnapshotCollectionItems;
        _maxSnapshotStringLength = maxSnapshotStringLength;
    }

    public event Action<OperationEventRecord> EventPublished;

    public int MaxOperations => _maxOperations;

    public int MaxEvents => _maxEvents;

    public int MaxRetainedResults => _maxRetainedResults;

    public int OperationCount
    {
        get
        {
            lock (_gate)
            {
                return _operationOrder.Count;
            }
        }
    }

    public int EventCount
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    public int RetainedResultCount
    {
        get
        {
            lock (_gate)
            {
                return _retainedResultOrder.Count;
            }
        }
    }

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

        var summary = new OperationEnvelope
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

        OperationEnvelope stored;
        lock (_gate)
        {
            var record = MergeWithExisting(summary);
            _operationsById[summary.OperationId] = record;
            PromoteOperation(summary.OperationId);
            TrimOperations();
            stored = HydrateEnvelope(record, includeResult: false);
        }

        Publish(CreateEvent("operation.started", stored));
        return stored;
    }

    public OperationEnvelope RecordCompleted(OperationEnvelope envelope)
    {
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));

        OperationEnvelope stored;
        lock (_gate)
        {
            var record = MergeWithExisting(envelope);
            _operationsById[envelope.OperationId] = record;
            PromoteOperation(envelope.OperationId);
            TrimOperations();
            stored = HydrateEnvelope(record, includeResult: true);
        }

        Publish(CreateEvent(GetEventType(stored), stored));
        return stored;
    }

    public OperationEnvelope GetOperation(string operationId, bool includeResult = true)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("An operation id is required.", nameof(operationId));

        lock (_gate)
        {
            return _operationsById.TryGetValue(operationId, out var record)
                ? HydrateEnvelope(record, includeResult)
                : null;
        }
    }

    public IReadOnlyList<OperationEnvelope> GetRecentOperations(int limit = 20, bool includeResults = false)
    {
        if (limit <= 0)
            return [];

        lock (_gate)
        {
            return _operationOrder
                .Take(limit)
                .Select(id => HydrateEnvelope(_operationsById[id], includeResults))
                .ToList();
        }
    }

    public IReadOnlyList<OperationEventRecord> GetRecentEvents(int limit = 50, string eventType = null, long afterSequence = 0, string operationId = null)
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
            if (string.IsNullOrWhiteSpace(operationId) == false)
                query = query.Where(entry => string.Equals(entry.OperationId, operationId, StringComparison.Ordinal));

            return query
                .Take(limit)
                .Select(CloneEvent)
                .ToList();
        }
    }

    private OperationRecord MergeWithExisting(OperationEnvelope envelope)
    {
        var previous = _operationsById.TryGetValue(envelope.OperationId, out var existing)
            ? existing
            : null;

        var summary = envelope.Clone(includeResult: false);
        if (previous?.Summary != null)
        {
            foreach (var pair in previous.Summary.Metadata)
            {
                if (!summary.Metadata.ContainsKey(pair.Key))
                    summary.Metadata[pair.Key] = pair.Value;
            }
        }

        var record = previous ?? new OperationRecord();
        record.Summary = summary;
        UpdateRetainedResult(envelope.OperationId, record, envelope.Result);
        record.Summary.HasResult = record.RetainedResult != null;
        record.Summary.ResultWasTruncated = record.ResultWasTruncated;
        return record;
    }

    private void UpdateRetainedResult(string operationId, OperationRecord record, object result)
    {
        if (result == null)
        {
            ClearRetainedResult(operationId, record);
            return;
        }

        var snapshotContext = new SnapshotContext(_maxSnapshotDepth, _maxSnapshotCollectionItems, _maxSnapshotStringLength);
        record.RetainedResult = SnapshotValue(result, depth: 0, snapshotContext);
        record.ResultWasTruncated = snapshotContext.Truncated;
        record.RetainedAtUtc = DateTimeOffset.UtcNow;
        PromoteRetainedResult(operationId);
        TrimRetainedResults();
    }

    private void ClearRetainedResult(string operationId, OperationRecord record)
    {
        record.RetainedResult = null;
        record.ResultWasTruncated = false;
        record.RetainedAtUtc = null;
        _retainedResultOrder.Remove(operationId);
    }

    private void PromoteOperation(string operationId)
    {
        _operationOrder.Remove(operationId);
        _operationOrder.Insert(0, operationId);
    }

    private void PromoteRetainedResult(string operationId)
    {
        _retainedResultOrder.Remove(operationId);
        _retainedResultOrder.Insert(0, operationId);
    }

    private void TrimOperations()
    {
        while (_operationOrder.Count > _maxOperations)
        {
            var removeId = _operationOrder[_operationOrder.Count - 1];
            _operationOrder.RemoveAt(_operationOrder.Count - 1);
            _retainedResultOrder.Remove(removeId);
            _operationsById.Remove(removeId);
        }
    }

    private void TrimRetainedResults()
    {
        while (_retainedResultOrder.Count > _maxRetainedResults)
        {
            var removeId = _retainedResultOrder[_retainedResultOrder.Count - 1];
            _retainedResultOrder.RemoveAt(_retainedResultOrder.Count - 1);
            if (_operationsById.TryGetValue(removeId, out var record))
            {
                record.RetainedResult = null;
                record.ResultWasTruncated = false;
                record.RetainedAtUtc = null;
                record.Summary.HasResult = false;
                record.Summary.ResultWasTruncated = false;
            }
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

    private OperationEnvelope HydrateEnvelope(OperationRecord record, bool includeResult)
    {
        var hydrated = record.Summary.Clone(includeResult: false);
        hydrated.HasResult = record.RetainedResult != null;
        hydrated.ResultWasTruncated = record.ResultWasTruncated;
        if (includeResult && record.RetainedResult != null)
            hydrated.Result = CloneSnapshotValue(record.RetainedResult);

        return hydrated;
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
            HasResult = envelope.HasResult,
            ResultWasTruncated = envelope.ResultWasTruncated,
            WarningCount = envelope.Warnings.Count,
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
            HasResult = eventRecord.HasResult,
            ResultWasTruncated = eventRecord.ResultWasTruncated,
            WarningCount = eventRecord.WarningCount,
            Metadata = new Dictionary<string, object>(eventRecord.Metadata, StringComparer.Ordinal)
        };
    }

    private static object CloneSnapshotValue(object value)
    {
        switch (value)
        {
            case null:
                return null;
            case string:
            case char:
            case bool:
            case byte:
            case sbyte:
            case short:
            case ushort:
            case int:
            case uint:
            case long:
            case ulong:
            case float:
            case double:
            case decimal:
            case DateTime:
            case DateTimeOffset:
            case TimeSpan:
            case Guid:
                return value;
        }

        if (value.GetType().IsEnum)
            return value;

        if (value is IDictionary<string, object> dictionary)
        {
            return dictionary.ToDictionary(
                pair => pair.Key,
                pair => CloneSnapshotValue(pair.Value),
                StringComparer.Ordinal);
        }

        if (value is IEnumerable<object> sequence)
            return sequence.Select(CloneSnapshotValue).ToList();

        return value;
    }

    private static object SnapshotValue(object value, int depth, SnapshotContext context)
    {
        if (value == null)
            return null;

        if (TrySnapshotScalar(value, context, out var scalar))
            return scalar;

        var type = value.GetType();
        if (depth >= context.MaxDepth)
        {
            context.Truncated = true;
            return CreateTruncationMarker("max_depth", type);
        }

        var requiresReferenceTracking = !type.IsValueType;
        if (requiresReferenceTracking && !context.Path.Add(value))
        {
            context.Truncated = true;
            return CreateTruncationMarker("cycle", type);
        }

        try
        {
            if (value is IDictionary dictionary)
                return SnapshotDictionary(dictionary, depth + 1, context);
            if (value is IEnumerable enumerable && value is not string)
                return SnapshotEnumerable(enumerable, depth + 1, context);

            return SnapshotObject(value, depth + 1, context);
        }
        finally
        {
            if (requiresReferenceTracking)
                context.Path.Remove(value);
        }
    }

    private static bool TrySnapshotScalar(object value, SnapshotContext context, out object scalar)
    {
        switch (value)
        {
            case null:
                scalar = null;
                return true;
            case string text:
                if (text.Length > context.MaxStringLength)
                {
                    context.Truncated = true;
                    scalar = text.Substring(0, context.MaxStringLength) + "...";
                    return true;
                }

                scalar = text;
                return true;
            case char:
            case bool:
            case byte:
            case sbyte:
            case short:
            case ushort:
            case int:
            case uint:
            case long:
            case ulong:
            case float:
            case double:
            case decimal:
            case DateTime:
            case DateTimeOffset:
            case TimeSpan:
            case Guid:
                scalar = value;
                return true;
        }

        if (value.GetType().IsEnum)
        {
            scalar = value.ToString();
            return true;
        }

        scalar = null;
        return false;
    }

    private static Dictionary<string, object> SnapshotDictionary(IDictionary dictionary, int depth, SnapshotContext context)
    {
        var snapshot = new Dictionary<string, object>(StringComparer.Ordinal);
        var count = 0;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (count >= context.MaxCollectionItems)
            {
                context.Truncated = true;
                snapshot["$truncated"] = CreateTruncationMarker("item_limit", dictionary.GetType());
                break;
            }

            var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
            snapshot[key] = SnapshotValue(entry.Value, depth, context);
            count++;
        }

        return snapshot;
    }

    private static List<object> SnapshotEnumerable(IEnumerable enumerable, int depth, SnapshotContext context)
    {
        var snapshot = new List<object>();
        var count = 0;
        foreach (var item in enumerable)
        {
            if (count >= context.MaxCollectionItems)
            {
                context.Truncated = true;
                snapshot.Add(CreateTruncationMarker("item_limit", enumerable.GetType()));
                break;
            }

            snapshot.Add(SnapshotValue(item, depth, context));
            count++;
        }

        return snapshot;
    }

    private static Dictionary<string, object> SnapshotObject(object value, int depth, SnapshotContext context)
    {
        var type = value.GetType();
        var snapshot = new Dictionary<string, object>(StringComparer.Ordinal);
        var properties = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

        var count = 0;
        foreach (var property in properties)
        {
            if (count >= context.MaxCollectionItems)
            {
                context.Truncated = true;
                snapshot["$truncated"] = CreateTruncationMarker("property_limit", type);
                break;
            }

            object propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (Exception ex)
            {
                context.Truncated = true;
                propertyValue = CreateTruncationMarker("property_error", ex.GetType());
            }

            snapshot[property.Name] = SnapshotValue(propertyValue, depth, context);
            count++;
        }

        return snapshot;
    }

    private static Dictionary<string, object> CreateTruncationMarker(string reason, Type type)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["reason"] = reason,
            ["type"] = type?.FullName ?? type?.Name ?? string.Empty
        };
    }
}
