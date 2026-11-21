using System;
using System.Collections.Generic;

namespace RimBridgeServer.Core;

/// <summary>
/// Remembers the last seen repeat count of recent log entries. The log journal collapses repeats only into its
/// newest entry, but threaded log callbacks can arrive slightly out of order, so this keeps a short window of entry
/// ids instead of one, and forgets the oldest id beyond that window.
/// </summary>
public sealed class RecentRepeatCounts
{
    public const int Capacity = 256;

    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public int Count => _counts.Count;

    public bool TryGet(string entryId, out int repeatCount)
    {
        return _counts.TryGetValue(entryId ?? string.Empty, out repeatCount);
    }

    public void Set(string entryId, int repeatCount)
    {
        entryId ??= string.Empty;
        if (!_counts.ContainsKey(entryId))
        {
            _order.Enqueue(entryId);
            if (_order.Count > Capacity)
                _counts.Remove(_order.Dequeue());
        }

        _counts[entryId] = repeatCount;
    }

    public void Clear()
    {
        _counts.Clear();
        _order.Clear();
    }
}
