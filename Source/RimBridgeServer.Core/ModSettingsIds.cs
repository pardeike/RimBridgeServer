using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RimBridgeServer.Core;

public static class ModSettingsIds
{
    private const string Prefix = "mod-settings";

    public static string CreateId(string packageId, string handleTypeName)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("A package id is required.", nameof(packageId));

        var normalizedPackageId = packageId.Trim();
        var normalizedHandleTypeName = string.IsNullOrWhiteSpace(handleTypeName)
            ? string.Empty
            : handleTypeName.Trim();

        return Prefix + ":" + normalizedPackageId + ":" + ComputeStableHash([normalizedHandleTypeName]);
    }

    private static string ComputeStableHash(string[] parts)
    {
        var payload = string.Join("\n", parts.Select(part => part?.Trim() ?? string.Empty));
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return string.Concat(hash.Take(8).Select(static value => value.ToString("x2")));
    }
}
