using System;
using System.Text;

namespace RimBridgeServer.Core;

public static class ArchitectDesignatorIds
{
    private const string CategoryPrefix = "architect-category:";
    private const string DesignatorPrefix = "architect-designator:";

    public static string CreateCategoryId(string categoryDefName)
    {
        return CategoryPrefix + NormalizeSegment(categoryDefName);
    }

    public static string CreateDesignatorId(string categoryDefName, string designatorKey)
    {
        return DesignatorPrefix + NormalizeSegment(categoryDefName) + ":" + NormalizeSegment(designatorKey);
    }

    public static string CreateDropdownChildDesignatorId(string categoryDefName, string parentDesignatorKey, string childDesignatorKey)
    {
        return DesignatorPrefix + NormalizeSegment(categoryDefName) + ":" + NormalizeSegment(parentDesignatorKey) + ":" + NormalizeSegment(childDesignatorKey);
    }

    public static bool TryGetCategoryKey(string categoryIdOrDefName, out string categoryKey)
    {
        categoryKey = string.Empty;
        if (string.IsNullOrWhiteSpace(categoryIdOrDefName))
            return false;

        var candidate = categoryIdOrDefName.Trim();
        if (candidate.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase))
            candidate = candidate.Substring(CategoryPrefix.Length);

        try
        {
            categoryKey = NormalizeSegment(candidate);
            return categoryKey.Length > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool CategoryMatches(string categoryIdOrDefName, string categoryDefName)
    {
        return TryGetCategoryKey(categoryIdOrDefName, out var requestedKey)
            && string.Equals(requestedKey, NormalizeSegment(categoryDefName), StringComparison.Ordinal);
    }

    public static string NormalizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty id segment is required.", nameof(value));

        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if (builder.Length == 0 || previousWasSeparator)
                continue;

            builder.Append('-');
            previousWasSeparator = true;
        }

        while (builder.Length > 0 && builder[builder.Length - 1] == '-')
            builder.Length--;

        if (builder.Length == 0)
            throw new ArgumentException("The id segment did not contain any usable characters.", nameof(value));

        return builder.ToString();
    }
}
