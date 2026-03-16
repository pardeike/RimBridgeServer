using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lib.GAB.Tools;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;

namespace RimBridgeServer;

internal static class LegacyToolExecution
{
    private static readonly Dictionary<string, string> ToolIdsByMemberName = typeof(RimBridgeTools)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Select(method => new { Method = method, Attribute = method.GetCustomAttribute<ToolAttribute>() })
        .Where(entry => entry.Attribute != null)
        .ToDictionary(entry => entry.Method.Name, entry => entry.Attribute.Name, StringComparer.Ordinal);
    private static CapabilityRegistry _registry;

    public static void Initialize(CapabilityRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public static object InvokeAlias(string memberName, IDictionary<string, object> arguments)
    {
        var alias = ResolveCapabilityAlias(memberName);
        var envelope = (_registry ?? throw new InvalidOperationException("Capability registry has not been initialized."))
            .Invoke(alias, arguments);
        var payload = envelope.Success
            ? envelope.Result
            : new
            {
                success = false,
                message = envelope.Error?.Message ?? "The tool failed.",
                exception = envelope.Error?.Details?.ToString()
            };
        return ComposeLegacyResponse(payload, envelope);
    }

    private static string ResolveCapabilityAlias(string memberName)
    {
        return ToolIdsByMemberName.TryGetValue(memberName, out var toolId)
            ? toolId
            : memberName;
    }

    private static object ComposeLegacyResponse(object payload, OperationEnvelope envelope)
    {
        var values = ToDictionary(payload);
        values["operation"] = envelope.WithoutResult();
        return values;
    }

    private static Dictionary<string, object> ToDictionary(object payload)
    {
        if (payload == null)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["result"] = null
            };
        }

        if (payload is Dictionary<string, object> dictionary)
            return new Dictionary<string, object>(dictionary, StringComparer.Ordinal);

        var type = payload.GetType();
        if (type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid)
            || type == typeof(TimeSpan))
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["result"] = payload
            };
        }

        return type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead)
            .ToDictionary(property => property.Name, property => property.GetValue(payload), StringComparer.Ordinal);
    }
}
