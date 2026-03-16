using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lib.GAB.Tools;
using RimBridgeServer.Contracts;
using RimBridgeServer.Extensions.Abstractions;

namespace RimBridgeServer;

internal sealed class BuiltInToolCapabilityProvider : IRimBridgeCapabilityProvider
{
    public string ProviderId => "rimbridge.builtin";

    public IEnumerable<RimBridgeCapabilityRegistration> GetCapabilities()
    {
        return typeof(RimBridgeTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => new { Method = method, Attribute = method.GetCustomAttribute<ToolAttribute>() })
            .Where(entry => entry.Attribute != null)
            .OrderBy(entry => entry.Attribute.Name, StringComparer.Ordinal)
            .Select(entry => new RimBridgeCapabilityRegistration(CreateDescriptor(entry.Method, entry.Attribute)))
            .ToList();
    }

    private CapabilityDescriptor CreateDescriptor(MethodInfo method, ToolAttribute attribute)
    {
        return new CapabilityDescriptor
        {
            Id = attribute.Name,
            ProviderId = ProviderId,
            Category = ResolveCategory(attribute.Name),
            Title = string.IsNullOrWhiteSpace(attribute.Title) ? attribute.Name : attribute.Title,
            Summary = attribute.Description ?? string.Empty,
            Source = CapabilitySourceKind.Core,
            ExecutionKind = ResolveExecutionKind(attribute.Name),
            SupportedModes = CapabilityExecutionMode.Wait,
            EmitsEvents = false,
            ResultType = method.ReturnType.FullName ?? method.ReturnType.Name,
            Aliases = [attribute.Name],
            Parameters = method.GetParameters().Select(CreateParameterDescriptor).ToList()
        };
    }

    private static CapabilityParameterDescriptor CreateParameterDescriptor(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttribute<ToolParameterAttribute>();
        var hasDefaultValue = parameter.HasDefaultValue && parameter.DefaultValue != DBNull.Value;
        return new CapabilityParameterDescriptor
        {
            Name = parameter.Name ?? string.Empty,
            ParameterType = parameter.ParameterType.FullName ?? parameter.ParameterType.Name,
            Description = attribute?.Description ?? string.Empty,
            Required = attribute?.Required ?? !parameter.IsOptional,
            DefaultValue = hasDefaultValue ? parameter.DefaultValue : attribute?.DefaultValue
        };
    }

    private static string ResolveCategory(string toolName)
    {
        var separatorIndex = toolName.IndexOf('/');
        return separatorIndex > 0 ? toolName.Substring(0, separatorIndex) : "general";
    }

    private static CapabilityExecutionKind ResolveExecutionKind(string toolName)
    {
        return toolName switch
        {
            "rimbridge/ping" => CapabilityExecutionKind.Immediate,
            "rimworld/start_debug_game" => CapabilityExecutionKind.LongEventBound,
            "rimworld/load_game" => CapabilityExecutionKind.LongEventBound,
            "rimworld/take_screenshot" => CapabilityExecutionKind.BackgroundObserved,
            _ => CapabilityExecutionKind.MainThread
        };
    }
}
