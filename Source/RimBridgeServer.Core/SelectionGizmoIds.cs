using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RimBridgeServer.Core;

public static class SelectionGizmoIds
{
    private const string Prefix = "selection-gizmo";
    private const string SelectionPrefix = "sel-";

    public static string CreateSelectionFingerprint(IEnumerable<string> selectionTokens)
    {
        return SelectionPrefix + ComputeStableHash(selectionTokens);
    }

    public static string CreateGizmoId(string selectionFingerprint, int ordinal, IEnumerable<string> signatureParts)
    {
        if (string.IsNullOrWhiteSpace(selectionFingerprint))
            throw new ArgumentException("A selection fingerprint is required.", nameof(selectionFingerprint));
        if (ordinal <= 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        return Prefix + ":" + selectionFingerprint.Trim() + ":" + ordinal + ":" + ComputeStableHash(signatureParts);
    }

    public static bool TryReadSelectionFingerprint(string gizmoId, out string selectionFingerprint)
    {
        selectionFingerprint = string.Empty;
        if (string.IsNullOrWhiteSpace(gizmoId))
            return false;

        var segments = gizmoId.Split(':');
        if (segments.Length != 4 || string.Equals(segments[0], Prefix, StringComparison.Ordinal) == false)
            return false;

        selectionFingerprint = segments[1];
        return selectionFingerprint.Length > 0;
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
