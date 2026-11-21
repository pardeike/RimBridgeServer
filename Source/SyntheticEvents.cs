using UnityEngine;

namespace RimBridgeServer;

/// <summary>
/// Waylith's <see cref="Event"/> stores <c>rawType</c>, <c>shift</c> and <c>alt</c> in their own fields, unlike Unity,
/// where they derive from the native event type and modifiers. RimWorld reads <c>rawType</c> in the selector's mouse-up
/// handling and <c>shift</c> for queued orders, so synthetic events must set them explicitly.
/// </summary>
internal static class SyntheticEvents
{
    public static void SetType(Event injectedEvent, EventType type)
    {
        injectedEvent.type = type;
        injectedEvent.rawType = type;
    }

    public static void SetModifiers(Event injectedEvent, EventModifiers modifiers)
    {
        injectedEvent.modifiers = modifiers;
        injectedEvent.shift = (modifiers & EventModifiers.Shift) != 0;
        injectedEvent.alt = (modifiers & EventModifiers.Alt) != 0;
    }
}
