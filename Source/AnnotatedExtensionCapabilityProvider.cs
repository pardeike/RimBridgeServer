using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lib.GAB.Tools;
using Newtonsoft.Json.Linq;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;
using RimBridgeServer.Extensions.Abstractions;
using AnnotationToolAttribute = RimBridgeServer.Annotations.ToolAttribute;
using AnnotationToolParameterAttribute = RimBridgeServer.Annotations.ToolParameterAttribute;
using AnnotationToolResponseAttribute = RimBridgeServer.Annotations.ToolResponseAttribute;

namespace RimBridgeServer;

internal sealed class AnnotatedExtensionCapabilityProvider : IRimBridgeCapabilityProvider
{
    internal sealed class DiscoveredTool
    {
        public string Alias { get; set; } = string.Empty;

        public ToolInfo ToolInfo { get; set; } = new();
    }

    internal sealed class ToolClass
    {
        public Type Type { get; set; }

        public object Instance { get; set; }

        public IReadOnlyList<MethodInfo> Methods { get; set; } = [];
    }

    private readonly string _category;
    private readonly OperationRunner _runner;
    private readonly IReadOnlyList<ToolClass> _toolClasses;
    private readonly List<RimBridgeCapabilityRegistration> _registrations;

    public AnnotatedExtensionCapabilityProvider(string providerId, string category, IReadOnlyList<ToolClass> toolClasses)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        _category = category ?? throw new ArgumentNullException(nameof(category));
        _toolClasses = toolClasses ?? throw new ArgumentNullException(nameof(toolClasses));
        _runner = new OperationRunner(new MainThreadDispatcher());
        _registrations = BuildRegistrations();
        Tools = BuildTools();
    }

    public string ProviderId { get; }

    public IReadOnlyList<DiscoveredTool> Tools { get; }

    public IEnumerable<RimBridgeCapabilityRegistration> GetCapabilities()
    {
        return _registrations;
    }

    private List<RimBridgeCapabilityRegistration> BuildRegistrations()
    {
        var registrations = new List<RimBridgeCapabilityRegistration>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var toolClass in _toolClasses.OrderBy(entry => entry.Type.FullName ?? entry.Type.Name, StringComparer.Ordinal))
        {
            var typeSegment = ReflectedCapabilityBinding.ToKebabCase(toolClass.Type.Name);
            if (string.IsNullOrWhiteSpace(typeSegment))
                typeSegment = "tools";

            foreach (var method in toolClass.Methods.OrderBy(method => method.Name, StringComparer.Ordinal))
            {
                var attribute = method.GetCustomAttribute<AnnotationToolAttribute>(inherit: false)
                    ?? throw new InvalidOperationException($"Annotated method '{method.Name}' on '{toolClass.Type.FullName ?? toolClass.Type.Name}' was missing a ToolAttribute.");

                var descriptor = new CapabilityDescriptor
                {
                    Id = CreateCapabilityId(typeSegment, method, usedIds),
                    ProviderId = ProviderId,
                    Category = _category,
                    Title = string.IsNullOrWhiteSpace(attribute.Title) ? attribute.Name : attribute.Title,
                    Summary = attribute.Description ?? string.Empty,
                    Source = CapabilitySourceKind.Extension,
                    ExecutionKind = CapabilityExecutionKind.MainThread,
                    SupportedModes = CapabilityExecutionMode.Wait,
                    EmitsEvents = true,
                    ResultType = method.ReturnType.FullName ?? method.ReturnType.Name,
                    Aliases = [attribute.Name],
                    Parameters = method.GetParameters().Select(CreateParameterDescriptor).ToList()
                };

                registrations.Add(new RimBridgeCapabilityRegistration(
                    descriptor,
                    (invocation, cancellationToken) => InvokeAsync(toolClass, method, descriptor, invocation, cancellationToken)));
            }
        }

        return registrations;
    }

    private IReadOnlyList<DiscoveredTool> BuildTools()
    {
        return _toolClasses
            .OrderBy(entry => entry.Type.FullName ?? entry.Type.Name, StringComparer.Ordinal)
            .SelectMany(toolClass => toolClass.Methods
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .Select(method =>
                {
                    var attribute = method.GetCustomAttribute<AnnotationToolAttribute>(inherit: false)
                        ?? throw new InvalidOperationException($"Annotated method '{method.Name}' on '{toolClass.Type.FullName ?? toolClass.Type.Name}' was missing a ToolAttribute.");

                    return new DiscoveredTool
                    {
                        Alias = attribute.Name,
                        ToolInfo = new ToolInfo
                        {
                            Name = attribute.Name,
                            Title = attribute.Title,
                            Description = attribute.Description,
                            ResultDescription = attribute.ResultDescription,
                            RequiresAuth = attribute.RequiresAuth,
                            Parameters = method.GetParameters().Select(CreateToolParameterInfo).ToList(),
                            ResponseFields = method.GetCustomAttributes<AnnotationToolResponseAttribute>(inherit: false)
                                .Select(response => new ToolResponseFieldInfo
                                {
                                    Name = response.Name,
                                    Type = response.Type,
                                    Description = response.Description,
                                    Always = response.Always,
                                    Nullable = response.Nullable
                                })
                                .ToList()
                        }
                    };
                }))
            .ToList();
    }

    private Task<OperationEnvelope> InvokeAsync(ToolClass toolClass, MethodInfo method, CapabilityDescriptor descriptor, CapabilityInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var envelope = _runner.Run(() => method.Invoke(method.IsStatic ? null : toolClass.Instance, BindArguments(method, invocation.Arguments)), new OperationExecutionOptions
        {
            OperationId = invocation.OperationId,
            CapabilityId = descriptor.Id,
            StartedAtUtc = invocation.RequestedAtUtc,
            MarshalToMainThread = true,
            TimeoutMs = 0,
            FailureCode = "capability.failed",
            TimeoutCode = "capability.timed_out",
            CancellationCode = "capability.cancelled"
        });

        return Task.FromResult(envelope);
    }

    private string CreateCapabilityId(string typeSegment, MethodInfo method, ISet<string> usedIds)
    {
        var baseId = $"{ProviderId}/{typeSegment}/{ReflectedCapabilityBinding.ToKebabCase(method.Name)}";
        var candidate = baseId;
        var suffix = 2;

        while (!usedIds.Add(candidate))
        {
            candidate = baseId + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return candidate;
    }

    private static CapabilityParameterDescriptor CreateParameterDescriptor(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttribute<AnnotationToolParameterAttribute>(inherit: false);
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

    private static ToolParameterInfo CreateToolParameterInfo(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttribute<AnnotationToolParameterAttribute>(inherit: false);
        var hasDefaultValue = parameter.HasDefaultValue && parameter.DefaultValue != DBNull.Value;
        return new ToolParameterInfo
        {
            Name = parameter.Name ?? string.Empty,
            Type = parameter.ParameterType,
            Description = attribute?.Description,
            Required = attribute?.Required ?? !parameter.IsOptional,
            DefaultValue = hasDefaultValue ? parameter.DefaultValue : attribute?.DefaultValue
        };
    }

    private static object[] BindArguments(MethodInfo method, IDictionary<string, object> arguments)
    {
        var parameters = method.GetParameters();
        var result = new object[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (arguments != null && arguments.TryGetValue(parameter.Name ?? string.Empty, out var suppliedValue))
            {
                result[i] = ConvertArgument(suppliedValue, parameter.ParameterType);
                continue;
            }

            if (parameter.HasDefaultValue)
            {
                result[i] = parameter.DefaultValue;
                continue;
            }

            result[i] = parameter.ParameterType.IsValueType
                ? Activator.CreateInstance(parameter.ParameterType)
                : null;
        }

        return result;
    }

    private static object ConvertArgument(object value, Type targetType)
    {
        if (value == null)
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullableType.IsInstanceOfType(value))
            return value;

        if (TryConvertStructuredArgument(value, nonNullableType, out var structured))
            return structured;

        if (nonNullableType.IsEnum)
        {
            if (value is string enumText)
                return Enum.Parse(nonNullableType, enumText, ignoreCase: true);

            return Enum.ToObject(nonNullableType, value);
        }

        return Convert.ChangeType(value, nonNullableType, CultureInfo.InvariantCulture);
    }

    private static bool TryConvertStructuredArgument(object value, Type targetType, out object converted)
    {
        converted = null;

        if (targetType == typeof(Dictionary<string, object>) || targetType == typeof(IDictionary<string, object>))
        {
            converted = NormalizeStructuredDictionary(value);
            return true;
        }

        if (targetType == typeof(List<object>) || targetType == typeof(IList<object>) || targetType == typeof(IEnumerable<object>))
        {
            converted = NormalizeStructuredList(value);
            return true;
        }

        return false;
    }

    private static Dictionary<string, object> NormalizeStructuredDictionary(object value)
    {
        return value switch
        {
            null => new Dictionary<string, object>(StringComparer.Ordinal),
            JObject jobject => NormalizeJObject(jobject),
            IDictionary<string, object> dictionary => NormalizeDictionary(dictionary),
            IDictionary legacyDictionary => NormalizeLegacyDictionary(legacyDictionary),
            _ => throw new InvalidOperationException($"Expected an object-style argument but received '{value.GetType().FullName ?? value.GetType().Name}'.")
        };
    }

    private static List<object> NormalizeStructuredList(object value)
    {
        return value switch
        {
            null => [],
            JArray jarray => NormalizeJArray(jarray),
            IEnumerable<object> items when value is not string => NormalizeEnumerable(items),
            IEnumerable enumerable when value is not string => NormalizeEnumerable(enumerable.Cast<object>()),
            _ => throw new InvalidOperationException($"Expected an array-style argument but received '{value.GetType().FullName ?? value.GetType().Name}'.")
        };
    }

    private static Dictionary<string, object> NormalizeDictionary(IDictionary<string, object> dictionary)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in dictionary)
            normalized[pair.Key] = NormalizeStructuredValue(pair.Value);

        return normalized;
    }

    private static Dictionary<string, object> NormalizeLegacyDictionary(IDictionary dictionary)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
                throw new InvalidOperationException("Structured object arguments must use string keys.");

            normalized[key] = NormalizeStructuredValue(entry.Value);
        }

        return normalized;
    }

    private static Dictionary<string, object> NormalizeJObject(JObject jobject)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in jobject.Properties())
            normalized[property.Name] = NormalizeStructuredValue(property.Value);

        return normalized;
    }

    private static List<object> NormalizeJArray(JArray jarray)
    {
        var normalized = new List<object>(jarray.Count);
        foreach (var item in jarray)
            normalized.Add(NormalizeStructuredValue(item));

        return normalized;
    }

    private static List<object> NormalizeEnumerable(IEnumerable<object> items)
    {
        var normalized = new List<object>();
        foreach (var item in items)
            normalized.Add(NormalizeStructuredValue(item));

        return normalized;
    }

    private static object NormalizeStructuredValue(object value)
    {
        return value switch
        {
            null => null,
            JObject jobject => NormalizeJObject(jobject),
            JArray jarray => NormalizeJArray(jarray),
            JValue jvalue => jvalue.Value,
            IDictionary<string, object> dictionary => NormalizeDictionary(dictionary),
            IDictionary legacyDictionary => NormalizeLegacyDictionary(legacyDictionary),
            IEnumerable<object> items when value is not string => NormalizeEnumerable(items),
            IEnumerable enumerable when value is not string => NormalizeEnumerable(enumerable.Cast<object>()),
            _ => value
        };
    }
}
