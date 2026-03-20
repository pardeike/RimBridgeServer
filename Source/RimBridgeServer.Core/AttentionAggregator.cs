using System;
using System.Collections.Generic;
using System.Linq;

namespace RimBridgeServer.Core;

public sealed class BridgeAttentionSample
{
    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int RepeatCount { get; set; }

    public long LatestSequence { get; set; }

    public BridgeAttentionSample Clone()
    {
        return new BridgeAttentionSample
        {
            Level = Level,
            Message = Message,
            RepeatCount = RepeatCount,
            LatestSequence = LatestSequence
        };
    }
}

public sealed class BridgeAttentionSnapshot
{
    public string AttentionId { get; set; } = string.Empty;

    public string State { get; set; } = "open";

    public string Severity { get; set; } = "error";

    public bool Blocking { get; set; } = true;

    public bool StateInvalidated { get; set; } = true;

    public string Summary { get; set; } = string.Empty;

    public string CausalOperationId { get; set; } = string.Empty;

    public string CausalMethod { get; set; } = string.Empty;

    public long OpenedAtSequence { get; set; }

    public long LatestSequence { get; set; }

    public long? DiagnosticsCursor { get; set; }

    public int TotalUrgentEntries { get; set; }

    public List<BridgeAttentionSample> Sample { get; set; } = [];

    public BridgeAttentionSnapshot Clone()
    {
        return new BridgeAttentionSnapshot
        {
            AttentionId = AttentionId,
            State = State,
            Severity = Severity,
            Blocking = Blocking,
            StateInvalidated = StateInvalidated,
            Summary = Summary,
            CausalOperationId = CausalOperationId,
            CausalMethod = CausalMethod,
            OpenedAtSequence = OpenedAtSequence,
            LatestSequence = LatestSequence,
            DiagnosticsCursor = DiagnosticsCursor,
            TotalUrgentEntries = TotalUrgentEntries,
            Sample = Sample.Select(entry => entry.Clone()).ToList()
        };
    }
}

public sealed class AttentionAggregator
{
    private sealed class SampleAccumulator
    {
        public string Level { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public int RepeatCount { get; set; }

        public long LatestSequence { get; set; }
    }

    private static readonly Dictionary<string, int> SeverityPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["warning"] = 1,
        ["error"] = 2,
        ["fatal"] = 3
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _logRepeatCountsByEntryId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SampleAccumulator> _samplesByKey = new(StringComparer.Ordinal);
    private BridgeAttentionSnapshot _current;

    public BridgeAttentionSnapshot GetCurrent()
    {
        lock (_gate)
        {
            return _current?.Clone();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _current = null;
            _logRepeatCountsByEntryId.Clear();
            _samplesByKey.Clear();
        }
    }

    public BridgeAttentionSnapshot RecordLog(BridgeLogEntry entry, long diagnosticsCursor = 0)
    {
        if (entry == null || ShouldTrackLogEntry(entry) == false)
            return null;

        lock (_gate)
        {
            EnsureCurrent(entry.Sequence, diagnosticsCursor);

            var delta = GetLogRepeatDelta(entry);
            if (delta <= 0)
                return _current?.Clone();

            _current.Severity = MaxSeverity(_current.Severity, NormalizeSeverity(entry.Level));
            _current.LatestSequence = entry.Sequence;
            _current.TotalUrgentEntries += delta;
            if (string.IsNullOrWhiteSpace(_current.CausalOperationId) && string.IsNullOrWhiteSpace(entry.OperationId) == false)
                _current.CausalOperationId = entry.OperationId;
            if (string.IsNullOrWhiteSpace(_current.CausalMethod) && string.IsNullOrWhiteSpace(entry.CapabilityId) == false)
                _current.CausalMethod = entry.CapabilityId;

            AddOrUpdateSample(NormalizeSeverity(entry.Level), SanitizeMessage(entry.Message), delta, entry.Sequence);
            _current.Summary = BuildSummary(_current.TotalUrgentEntries, BuildLogHeadline(entry));
            _current.Sample = BuildSamples();
            return _current.Clone();
        }
    }

    public BridgeAttentionSnapshot RecordOperationEvent(OperationEventRecord eventRecord, long diagnosticsCursor = 0)
    {
        if (eventRecord == null || ShouldTrackOperationEvent(eventRecord) == false)
            return null;

        lock (_gate)
        {
            EnsureCurrent(eventRecord.Sequence, diagnosticsCursor);

            _current.Severity = MaxSeverity(_current.Severity, NormalizeOperationSeverity(eventRecord));
            _current.LatestSequence = eventRecord.Sequence;
            _current.TotalUrgentEntries += 1;
            if (string.IsNullOrWhiteSpace(_current.CausalOperationId) && string.IsNullOrWhiteSpace(eventRecord.OperationId) == false)
                _current.CausalOperationId = eventRecord.OperationId;
            if (string.IsNullOrWhiteSpace(_current.CausalMethod) && string.IsNullOrWhiteSpace(eventRecord.CapabilityId) == false)
                _current.CausalMethod = eventRecord.CapabilityId;

            AddOrUpdateSample(NormalizeOperationSeverity(eventRecord), BuildOperationSampleMessage(eventRecord), 1, eventRecord.Sequence);
            _current.Summary = BuildSummary(_current.TotalUrgentEntries, BuildOperationHeadline(eventRecord));
            _current.Sample = BuildSamples();
            return _current.Clone();
        }
    }

    private static bool ShouldTrackLogEntry(BridgeLogEntry entry)
    {
        var level = NormalizeSeverity(entry.Level);
        return string.Equals(level, "error", StringComparison.Ordinal)
            || string.Equals(level, "fatal", StringComparison.Ordinal);
    }

    private static bool ShouldTrackOperationEvent(OperationEventRecord eventRecord)
    {
        return string.Equals(eventRecord.EventType, "operation.failed", StringComparison.Ordinal)
            || string.Equals(eventRecord.EventType, "operation.cancelled", StringComparison.Ordinal)
            || string.Equals(eventRecord.EventType, "operation.timed_out", StringComparison.Ordinal);
    }

    private void EnsureCurrent(long openedAtSequence, long diagnosticsCursor)
    {
        if (_current != null)
            return;

        _current = new BridgeAttentionSnapshot
        {
            AttentionId = $"attn_{openedAtSequence}",
            State = "open",
            Severity = "error",
            Blocking = true,
            StateInvalidated = true,
            OpenedAtSequence = openedAtSequence,
            LatestSequence = openedAtSequence,
            DiagnosticsCursor = diagnosticsCursor > 0 ? Math.Max(0, diagnosticsCursor - 1) : null
        };
    }

    private int GetLogRepeatDelta(BridgeLogEntry entry)
    {
        var currentRepeatCount = Math.Max(1, entry.RepeatCount);
        _logRepeatCountsByEntryId.TryGetValue(entry.EntryId ?? string.Empty, out var previousRepeatCount);
        _logRepeatCountsByEntryId[entry.EntryId ?? string.Empty] = currentRepeatCount;

        if (previousRepeatCount <= 0)
            return currentRepeatCount;

        return Math.Max(0, currentRepeatCount - previousRepeatCount);
    }

    private void AddOrUpdateSample(string level, string message, int repeatCountDelta, long latestSequence)
    {
        var key = $"{level}\n{message}";
        if (_samplesByKey.TryGetValue(key, out var existing) == false)
        {
            existing = new SampleAccumulator
            {
                Level = level,
                Message = message
            };
            _samplesByKey[key] = existing;
        }

        existing.RepeatCount += Math.Max(1, repeatCountDelta);
        existing.LatestSequence = Math.Max(existing.LatestSequence, latestSequence);
    }

    private List<BridgeAttentionSample> BuildSamples()
    {
        return _samplesByKey.Values
            .OrderByDescending(entry => SeverityPriority.TryGetValue(entry.Level, out var priority) ? priority : 0)
            .ThenByDescending(entry => entry.RepeatCount)
            .ThenByDescending(entry => entry.LatestSequence)
            .Take(5)
            .Select(entry => new BridgeAttentionSample
            {
                Level = entry.Level,
                Message = entry.Message,
                RepeatCount = entry.RepeatCount,
                LatestSequence = entry.LatestSequence
            })
            .ToList();
    }

    private static string MaxSeverity(string current, string candidate)
    {
        var currentPriority = SeverityPriority.TryGetValue(NormalizeSeverity(current), out var existingPriority)
            ? existingPriority
            : 0;
        var candidatePriority = SeverityPriority.TryGetValue(NormalizeSeverity(candidate), out var nextPriority)
            ? nextPriority
            : 0;

        return candidatePriority > currentPriority
            ? NormalizeSeverity(candidate)
            : NormalizeSeverity(current);
    }

    private static string NormalizeSeverity(string level)
    {
        return string.IsNullOrWhiteSpace(level)
            ? "error"
            : level.Trim().ToLowerInvariant();
    }

    private static string NormalizeOperationSeverity(OperationEventRecord eventRecord)
    {
        return eventRecord.EventType switch
        {
            "operation.timed_out" => "fatal",
            _ => "error"
        };
    }

    private static string BuildSummary(int totalUrgentEntries, string latestHeadline)
    {
        if (totalUrgentEntries <= 1)
            return latestHeadline;

        return $"RimBridge observed {totalUrgentEntries} blocking events. Latest: {latestHeadline}";
    }

    private static string BuildLogHeadline(BridgeLogEntry entry)
    {
        var message = SanitizeMessage(entry.Message);
        return $"RimWorld logged a {NormalizeSeverity(entry.Level)} message: {message}";
    }

    private static string BuildOperationHeadline(OperationEventRecord eventRecord)
    {
        var detail = BuildOperationDetail(eventRecord);
        if (string.IsNullOrWhiteSpace(eventRecord.CapabilityId))
            return detail;

        return $"{eventRecord.CapabilityId} reported {detail}";
    }

    private static string BuildOperationSampleMessage(OperationEventRecord eventRecord)
    {
        var detail = BuildOperationDetail(eventRecord);
        return string.IsNullOrWhiteSpace(eventRecord.CapabilityId)
            ? detail
            : $"{eventRecord.CapabilityId}: {detail}";
    }

    private static string BuildOperationDetail(OperationEventRecord eventRecord)
    {
        var detail = SanitizeMessage(eventRecord.ErrorMessage);
        if (string.IsNullOrWhiteSpace(detail))
            detail = eventRecord.EventType;

        return eventRecord.EventType switch
        {
            "operation.cancelled" => $"operation cancelled: {detail}",
            "operation.timed_out" => $"operation timed out: {detail}",
            _ => $"operation failed: {detail}"
        };
    }

    private static string SanitizeMessage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "No detail provided.";

        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (singleLine.Length <= 220)
            return singleLine;

        return singleLine.Substring(0, 217) + "...";
    }
}
