using System;
using System.Text.RegularExpressions;

namespace RimBridgeServer.Core;

public static class GabpToolNameValidator
{
    public const string CanonicalPattern = "^[a-z][a-z0-9_-]*(/[a-z][a-z0-9_-]*)+$";

    private static readonly Regex CanonicalToolNameRegex = new(CanonicalPattern, RegexOptions.CultureInvariant);

    public static bool IsCanonical(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && CanonicalToolNameRegex.IsMatch(name);
    }

    public static void EnsureCanonical(string name, string valueName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("GABP tool name cannot be null or empty.", valueName);

        if (!IsCanonical(name))
            throw new ArgumentException($"GABP tool name '{name}' must match {CanonicalPattern}. Use slash-delimited canonical names such as 'rimbridge/ping'; dotted MCP adapter names are not valid here.", valueName);
    }
}
