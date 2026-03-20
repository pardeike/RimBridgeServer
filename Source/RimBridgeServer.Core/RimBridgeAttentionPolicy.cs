using System;

namespace RimBridgeServer.Core;

/// <summary>
/// Current built-in policy for deciding which async bridge signals should open blocking attention.
/// Centralizing the rules here keeps the policy explicit and gives future work one seam to extend.
/// </summary>
internal sealed class RimBridgeAttentionPolicy
{
    public bool ShouldTrackLogEntry(BridgeLogEntry entry)
    {
        if (entry == null)
            return false;

        var level = NormalizeSeverity(entry.Level);
        return string.Equals(level, "error", StringComparison.Ordinal)
            || string.Equals(level, "fatal", StringComparison.Ordinal);
    }

    public bool ShouldTrackOperationEvent(OperationEventRecord eventRecord)
    {
        if (eventRecord == null)
            return false;

        return string.Equals(eventRecord.EventType, "operation.failed", StringComparison.Ordinal)
            || string.Equals(eventRecord.EventType, "operation.cancelled", StringComparison.Ordinal)
            || string.Equals(eventRecord.EventType, "operation.timed_out", StringComparison.Ordinal);
    }

    private static string NormalizeSeverity(string level)
    {
        return string.IsNullOrWhiteSpace(level)
            ? "error"
            : level.Trim().ToLowerInvariant();
    }
}
