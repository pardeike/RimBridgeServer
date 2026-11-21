using System;

namespace RimBridgeServer.Core;

/// <summary>
/// A lookup did not resolve to one target because a thing or pawn id is stale or a name matches nothing or several
/// targets. The call fails with <see cref="ErrorCode"/>, but it does not open blocking attention: the game state is
/// fine and the caller can simply look again.
/// </summary>
public sealed class TargetLookupException : InvalidOperationException
{
    public const string ErrorCode = "capability.target_unresolved";

    public TargetLookupException(string message)
        : base(message)
    {
    }

    public static bool IsIn(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is TargetLookupException)
                return true;
        }

        return false;
    }
}
