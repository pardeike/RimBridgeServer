using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridgeServer.Contracts;
using Verse;

namespace RimBridgeServer;

/// <summary>
/// Shapes the agent-facing tool response. Capability payloads stay unchanged for scripts and
/// companion tools; only the merged top-level response returned through GABP is compacted.
/// </summary>
internal static class AgentResponseShaper
{
    private static readonly string[] ReadinessLevels = ["visualReady", "playable", "currentMapReady", "mapDataReady", "gameDataReady"];
    private static readonly string[] ReadinessNames = ["visual", "playable", "currentMap", "mapData", "gameData"];

    public static object Shape(Dictionary<string, object> values, OperationEnvelope envelope)
    {
        values["operation"] = DescribeOperation(envelope);
        JObject shaped;
        try
        {
            shaped = JObject.FromObject(values);
        }
        catch
        {
            return values;
        }

        if (shaped["state"] is JObject state && state["programState"] != null)
            shaped["state"] = CompactState(state);
        else if (shaped["state"] == null)
            shaped["state"] = CurrentState();

        CompactNestedStates(shaped);
        Prune(shaped);
        return shaped;
    }

    private static object DescribeOperation(OperationEnvelope envelope)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["operationId"] = envelope.OperationId,
            ["status"] = envelope.Status.ToString(),
            ["durationMs"] = envelope.DurationMs,
            ["warnings"] = envelope.Warnings?.Count > 0 ? envelope.Warnings : null,
            ["error"] = envelope.Error
        };
    }

    private static JObject CompactState(JObject state)
    {
        var compact = CurrentState();
        compact["program"] = state["programState"];
        if (state["currentMapId"] is JValue { Value: not null } map)
            compact["map"] = map;
        if (state.Value<bool?>("longEventPending") == true)
            compact["longEventPending"] = true;
        for (var index = 0; index < ReadinessLevels.Length; index++)
        {
            if (state.Value<bool?>(ReadinessLevels[index]) == true)
            {
                compact["ready"] = ReadinessNames[index];
                break;
            }
        }

        compact["paused"] = state["paused"] ?? compact["paused"];
        compact["speed"] = state["timeSpeed"] ?? compact["speed"];
        return compact;
    }

    // Plain field reads only: this runs on the bridge thread, not RimWorld's main thread.
    private static JObject CurrentState()
    {
        var compact = new JObject();
        try
        {
            compact["program"] = Current.ProgramState.ToString();
            var tickManager = Current.Game?.tickManager;
            if (tickManager != null)
            {
                compact["tick"] = tickManager.TicksGame;
                compact["paused"] = tickManager.Paused;
                compact["speed"] = tickManager.CurTimeSpeed.ToString();
            }
        }
        catch
        {
        }

        return compact;
    }

    // Lifecycle and script results embed full state snapshots in nested objects (load, wait, step results).
    private static void CompactNestedStates(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToList())
            {
                if (property.Name == "state" && property.Value is JObject nested && nested["programState"] != null && !ReferenceEquals(obj.Parent, null))
                    property.Value = CompactNestedState(nested);
                else
                    CompactNestedStates(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
                CompactNestedStates(item);
        }
    }

    private static JObject CompactNestedState(JObject state)
    {
        var compact = new JObject { ["program"] = state["programState"] };
        if (state["currentMapId"] is JValue { Value: not null } map)
            compact["map"] = map;
        if (state.Value<bool?>("longEventPending") == true)
            compact["longEventPending"] = true;
        for (var index = 0; index < ReadinessLevels.Length; index++)
        {
            if (state.Value<bool?>(ReadinessLevels[index]) == true)
            {
                compact["ready"] = ReadinessNames[index];
                break;
            }
        }

        if (state["paused"] != null)
            compact["paused"] = state["paused"];
        if (state["timeSpeed"] != null)
            compact["speed"] = state["timeSpeed"];
        return compact;
    }

    private static void Prune(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToList())
            {
                if (IsEmpty(property.Value))
                    property.Remove();
                else
                    Prune(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
                Prune(item);
        }
    }

    private static bool IsEmpty(JToken value)
    {
        return value.Type == JTokenType.Null
            || value.Type == JTokenType.Undefined
            || (value.Type == JTokenType.String && value.Value<string>().Length == 0);
    }
}
