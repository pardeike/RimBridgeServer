using System;

namespace RimBridgeServer.Core;

/// <summary>
/// A lookup did not resolve to one target. Either no current map is active (<see cref="NoCurrentMapCode"/>), or a
/// thing or pawn id is stale or a name matches nothing or several targets (<see cref="ErrorCode"/>). The call fails
/// with that code, but it does not open blocking attention: the game state is fine and the caller can look again.
/// </summary>
public sealed class TargetLookupException : InvalidOperationException
{
    public const string ErrorCode = "capability.target_unresolved";
    public const string NoCurrentMapCode = "capability.no_current_map";

    public TargetLookupException(string message, string code = ErrorCode)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    /// <summary>Returns the code of the first lookup failure in the exception chain, or null if there is none.</summary>
    public static string CodeIn(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is TargetLookupException lookup)
                return lookup.Code;
        }

        return null;
    }

    public static bool IsLookupCode(string code)
    {
        return string.Equals(code, ErrorCode, StringComparison.Ordinal)
            || string.Equals(code, NoCurrentMapCode, StringComparison.Ordinal);
    }
}
