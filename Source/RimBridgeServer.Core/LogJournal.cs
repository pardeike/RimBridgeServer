using System;
using System.Collections.Generic;
using System.Linq;

namespace RimBridgeServer.Core;

public sealed class BridgeLogEntry
{
    public long Sequence { get; set; }

    public string EntryId { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string StackTrace { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string OperationId { get; set; } = string.Empty;

    public string CapabilityId { get; set; } = string.Empty;

    public string ParentOperationId { get; set; } = string.Empty;

    public string RootOperationId { get; set; } = string.Empty;

    public string ScriptStatementId { get; set; } = string.Empty;

    public string ScriptStepId { get; set; } = string.Empty;

    public string ScriptCall { get; set; } = string.Empty;

    public DateTimeOffset FirstSeenAtUtc { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public int RepeatCount { get; set; } = 1;
}

public sealed class LogJournal
{
    private static readonly Dictionary<string, int> LevelPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trace"] = 0,
        ["debug"] = 1,
        ["info"] = 2,
        ["warning"] = 3,
        ["error"] = 4,
        ["fatal"] = 5
    };

    private readonly object _gate = new();
    private readonly List<BridgeLogEntry> _entries = [];
    private readonly int _maxEntries;
    private readonly int _collapseWindowMs;
    private long _nextSequence = 1;

    public LogJournal(int maxEntries = 2000, int collapseWindowMs = 2000)
    {
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (collapseWindowMs < 0)
            throw new ArgumentOutOfRangeException(nameof(collapseWindowMs));

        _maxEntries = maxEntries;
        _collapseWindowMs = collapseWindowMs;
    }

    public event Action<BridgeLogEntry> EntryRecorded;

    public int MaxEntries => _maxEntries;

    public int EntryCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long LatestSequence
    {
        get
        {
            lock (_gate)
            {
                return _nextSequence - 1;
            }
        }
    }

    public BridgeLogEntry Record(
        string level,
        string message,
        string stackTrace = null,
        string source = null,
        DateTimeOffset? timestampUtc = null,
        OperationContextSnapshot operationContext = null)
    {
        var recordedAtUtc = timestampUtc ?? DateTimeOffset.UtcNow;
        var context = operationContext ?? OperationContext.Capture();
        var entry = new BridgeLogEntry
        {
            Sequence = _nextSequence,
            EntryId = "log_" + Guid.NewGuid().ToString("N"),
            Level = NormalizeLevel(level),
            Message = message ?? string.Empty,
            StackTrace = stackTrace ?? string.Empty,
            Source = source ?? string.Empty,
            OperationId = context?.OperationId ?? string.Empty,
            CapabilityId = context?.CapabilityId ?? string.Empty,
            ParentOperationId = context?.ParentOperationId ?? string.Empty,
            RootOperationId = context?.RootOperationId ?? string.Empty,
            ScriptStatementId = context?.ScriptStatementId ?? string.Empty,
            ScriptStepId = context?.ScriptStepId ?? string.Empty,
            ScriptCall = context?.ScriptCall ?? string.Empty,
            FirstSeenAtUtc = recordedAtUtc,
            TimestampUtc = recordedAtUtc
        };

        Action<BridgeLogEntry> handler;
        lock (_gate)
        {
            if (_entries.Count > 0 && ShouldCollapse(_entries[0], entry))
            {
                _entries[0].TimestampUtc = entry.TimestampUtc;
                _entries[0].RepeatCount++;
                _entries[0].Sequence = _nextSequence++;
                entry = Clone(_entries[0]);
            }
            else
            {
                entry.Sequence = _nextSequence++;
                _entries.Insert(0, entry);
                if (_entries.Count > _maxEntries)
                    _entries.RemoveRange(_maxEntries, _entries.Count - _maxEntries);
            }

            handler = EntryRecorded;
        }

        handler?.Invoke(Clone(entry));
        return Clone(entry);
    }

    public IReadOnlyList<BridgeLogEntry> GetEntries(
        int limit = 50,
        string minimumLevel = null,
        long afterSequence = 0,
        string operationId = null,
        string rootOperationId = null,
        string capabilityId = null)
    {
        if (limit <= 0)
            return [];

        lock (_gate)
        {
            IEnumerable<BridgeLogEntry> query = _entries;
            if (string.IsNullOrWhiteSpace(minimumLevel) == false)
            {
                var threshold = GetPriority(minimumLevel);
                query = query.Where(entry => GetPriority(entry.Level) >= threshold);
            }
            if (afterSequence > 0)
                query = query.Where(entry => entry.Sequence > afterSequence);
            if (string.IsNullOrWhiteSpace(operationId) == false)
                query = query.Where(entry => string.Equals(entry.OperationId, operationId, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(rootOperationId) == false)
                query = query.Where(entry => string.Equals(entry.RootOperationId, rootOperationId, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(capabilityId) == false)
                query = query.Where(entry => string.Equals(entry.CapabilityId, capabilityId, StringComparison.Ordinal));

            return query
                .Take(limit)
                .Select(Clone)
                .ToList();
        }
    }

    private static string NormalizeLevel(string level)
    {
        return string.IsNullOrWhiteSpace(level)
            ? "info"
            : level.Trim().ToLowerInvariant();
    }

    private static int GetPriority(string level)
    {
        return LevelPriority.TryGetValue(NormalizeLevel(level), out var priority)
            ? priority
            : LevelPriority["info"];
    }

    private bool ShouldCollapse(BridgeLogEntry existing, BridgeLogEntry candidate)
    {
        return string.Equals(existing.Level, candidate.Level, StringComparison.Ordinal)
            && string.Equals(existing.Message, candidate.Message, StringComparison.Ordinal)
            && string.Equals(existing.StackTrace, candidate.StackTrace, StringComparison.Ordinal)
            && string.Equals(existing.Source, candidate.Source, StringComparison.Ordinal)
            && string.Equals(existing.OperationId, candidate.OperationId, StringComparison.Ordinal)
            && string.Equals(existing.CapabilityId, candidate.CapabilityId, StringComparison.Ordinal)
            && string.Equals(existing.ParentOperationId, candidate.ParentOperationId, StringComparison.Ordinal)
            && string.Equals(existing.RootOperationId, candidate.RootOperationId, StringComparison.Ordinal)
            && string.Equals(existing.ScriptStatementId, candidate.ScriptStatementId, StringComparison.Ordinal)
            && string.Equals(existing.ScriptStepId, candidate.ScriptStepId, StringComparison.Ordinal)
            && string.Equals(existing.ScriptCall, candidate.ScriptCall, StringComparison.Ordinal)
            && (candidate.TimestampUtc - existing.TimestampUtc).TotalMilliseconds <= _collapseWindowMs;
    }

    private static BridgeLogEntry Clone(BridgeLogEntry entry)
    {
        return new BridgeLogEntry
        {
            Sequence = entry.Sequence,
            EntryId = entry.EntryId,
            Level = entry.Level,
            Message = entry.Message,
            StackTrace = entry.StackTrace,
            Source = entry.Source,
            OperationId = entry.OperationId,
            CapabilityId = entry.CapabilityId,
            ParentOperationId = entry.ParentOperationId,
            RootOperationId = entry.RootOperationId,
            ScriptStatementId = entry.ScriptStatementId,
            ScriptStepId = entry.ScriptStepId,
            ScriptCall = entry.ScriptCall,
            FirstSeenAtUtc = entry.FirstSeenAtUtc,
            TimestampUtc = entry.TimestampUtc,
            RepeatCount = entry.RepeatCount
        };
    }
}
