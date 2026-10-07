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
    // State fields that agents, skills and harnesses read (for example state.automationReady); the rest of the
    // snapshot (fade alpha, map count, intermediate readiness flags) is dropped. Names are kept unchanged.
    private static readonly string[] KeptStateFields =
    [
        "programState", "inEntryScene", "hasCurrentGame", "currentMapId", "longEventPending",
        "paused", "timeSpeed", "playable", "visualReady", "automationReady"
    ];

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
            shaped["state"] = CompactState(state, includeTick: true);
        else if (shaped["state"] == null)
            shaped["state"] = CurrentState();

        CompactNestedStates(shaped, isRoot: true);
        Prune(shaped);
        return shaped;
    }

    // Original envelope field names are kept for existing consumers; timestamps, Result, Metadata, HasResult and
    // empty Warnings/Error are dropped.
    private static object DescribeOperation(OperationEnvelope envelope)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["OperationId"] = envelope.OperationId,
            ["Status"] = envelope.Status.ToString(),
            ["Success"] = envelope.Success,
            ["DurationMs"] = envelope.DurationMs,
            ["ResultWasTruncated"] = envelope.ResultWasTruncated ? true : null,
            ["Warnings"] = envelope.Warnings?.Count > 0 ? envelope.Warnings : null,
            ["Error"] = envelope.Error
        };
    }

    private static JObject CompactState(JObject state, bool includeTick)
    {
        var compact = new JObject();
        foreach (var field in KeptStateFields)
        {
            if (state[field] is { } value && value.Type != JTokenType.Null)
                compact[field] = value;
        }

        if (includeTick && TryReadTick(out var tick))
            compact["tick"] = tick;
        return compact;
    }

    // Plain field reads only: this runs on the bridge thread, not RimWorld's main thread.
    private static JObject CurrentState()
    {
        var compact = new JObject();
        try
        {
            compact["programState"] = Current.ProgramState.ToString();
            compact["hasCurrentGame"] = Current.Game != null;
            var tickManager = Current.Game?.tickManager;
            if (tickManager != null)
            {
                compact["paused"] = tickManager.Paused;
                compact["timeSpeed"] = tickManager.CurTimeSpeed.ToString();
                compact["tick"] = tickManager.TicksGame;
            }
        }
        catch
        {
        }

        return compact;
    }

    private static bool TryReadTick(out int tick)
    {
        tick = 0;
        try
        {
            var tickManager = Current.Game?.tickManager;
            if (tickManager == null)
                return false;
            tick = tickManager.TicksGame;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Lifecycle and script results embed full state snapshots in nested objects (load, wait, step results).
    private static void CompactNestedStates(JToken token, bool isRoot = false)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToList())
            {
                if (!isRoot && property.Name == "state" && property.Value is JObject nested && nested["programState"] != null)
                    property.Value = CompactState(nested, includeTick: false);
                else if (!(isRoot && property.Name == "state"))
                    CompactNestedStates(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
                CompactNestedStates(item);
        }
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
