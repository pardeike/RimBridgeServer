using UnityEngine;

namespace RimBridgeServer;

/// <summary>
/// Waylith's <see cref="Event"/> stores <c>rawType</c> separately from <c>type</c>, unlike Unity, where both come from
/// one native field. RimWorld reads <c>rawType</c> in places such as the selector's mouse-up handling, so synthetic
/// events must set both.
/// </summary>
internal static class SyntheticEvents
{
    public static void SetType(Event injectedEvent, EventType type)
    {
        injectedEvent.type = type;
        injectedEvent.rawType = type;
    }
}
