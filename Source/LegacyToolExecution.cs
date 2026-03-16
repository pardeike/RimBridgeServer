using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Lib.GAB.Tools;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;

namespace RimBridgeServer;

internal static class LegacyToolExecution
{
    private static readonly OperationRunner Runner = new(new MainThreadDispatcher());
    private static readonly Dictionary<string, string> ToolIdsByMemberName = typeof(RimBridgeTools)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Select(method => new { Method = method, Attribute = method.GetCustomAttribute<ToolAttribute>() })
        .Where(entry => entry.Attribute != null)
        .ToDictionary(entry => entry.Method.Name, entry => entry.Attribute.Name, StringComparer.Ordinal);

    public static object Run(Func<object> func, bool marshalToMainThread, string memberName)
    {
        var capabilityId = ResolveCapabilityId(memberName);
        var envelope = Runner.Run(func, new OperationExecutionOptions
        {
            CapabilityId = capabilityId,
            MarshalToMainThread = marshalToMainThread,
            TimeoutMs = 10000,
            FailureCode = "tool.failed"
        });
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

    private static string ResolveCapabilityId(string memberName)
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
        if (IsSimpleType(type))
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

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid)
            || type == typeof(TimeSpan)
            || type == typeof(DateTimeKind)
            || type == typeof(CultureInfo);
    }
}
