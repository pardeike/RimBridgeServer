using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RimBridgeServer.Core;

public static class NotificationIds
{
    private const string AlertPrefix = "alert";
    private const string AlertSnapshotPrefix = "alerts-";

    public static string CreateAlertSnapshotFingerprint(IEnumerable<string> alertTokens)
    {
        return AlertSnapshotPrefix + ComputeStableHash(alertTokens);
    }

    public static string CreateAlertId(string snapshotFingerprint, int ordinal, IEnumerable<string> signatureParts)
    {
        if (string.IsNullOrWhiteSpace(snapshotFingerprint))
            throw new ArgumentException("An alert snapshot fingerprint is required.", nameof(snapshotFingerprint));
        if (ordinal <= 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        return AlertPrefix + ":" + snapshotFingerprint.Trim() + ":" + ordinal + ":" + ComputeStableHash(signatureParts);
    }

    public static bool TryReadAlertSnapshotFingerprint(string alertId, out string snapshotFingerprint)
    {
        snapshotFingerprint = string.Empty;
        if (string.IsNullOrWhiteSpace(alertId))
            return false;

        var segments = alertId.Split(':');
        if (segments.Length != 4 || string.Equals(segments[0], AlertPrefix, StringComparison.Ordinal) == false)
            return false;

        snapshotFingerprint = segments[1];
        return snapshotFingerprint.Length > 0;
    }

    private static string ComputeStableHash(IEnumerable<string> parts)
    {
        var normalized = parts == null
            ? Array.Empty<string>()
            : parts.Select(part => part?.Trim() ?? string.Empty).ToArray();

        var payload = string.Join("\n", normalized);
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return string.Concat(hash.Take(8).Select(static value => value.ToString("x2")));
    }
}
