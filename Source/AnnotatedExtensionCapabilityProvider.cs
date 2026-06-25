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
using RimBridgeServer.Sdk;
using SdkToolAttribute = RimBridgeServer.Sdk.ToolAttribute;
using SdkToolParameterAttribute = RimBridgeServer.Sdk.ToolParameterAttribute;
using SdkToolResponseAttribute = RimBridgeServer.Sdk.ToolResponseAttribute;

namespace RimBridgeServer;

internal sealed class AnnotatedExtensionCapabilityProvider : IRimBridgeCapabilityProvider
{
    private const int DefaultExtensionToolTimeoutMs = 60000;
    private const int MinimumExtensionToolTimeoutMs = 1000;
    private const int MaximumExtensionToolTimeoutMs = 600000;
    private const string ExecutionTimeoutArgumentName = "_rimBridgeTimeoutMs";

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
                var attribute = method.GetCustomAttribute<SdkToolAttribute>(inherit: false)
                    ?? throw new InvalidOperationException($"Annotated method '{method.Name}' on '{toolClass.Type.FullName ?? toolClass.Type.Name}' was missing a ToolAttribute.");

                var descriptor = new CapabilityDescriptor
                {
                    Id = CreateCapabilityId(typeSegment, method, usedIds),
                    ProviderId = ProviderId,
                    Category = _category,
                    Title = string.IsNullOrWhiteSpace(attribute.Title) ? attribute.Name : attribute.Title,
                    Summary = attribute.Description ?? string.Empty,
                    Source = CapabilitySourceKind.Extension,
                    ExecutionKind = CapabilityExecutionKind.FrameBound,
                    SupportedModes = CapabilityExecutionMode.Wait | CapabilityExecutionMode.Queue,
                    EmitsEvents = true,
                    ResultType = DescribeReturnType(method.ReturnType),
                    Aliases = [attribute.Name],
                    Parameters = method.GetParameters()
                        .Where(parameter => IsInjectedParameter(parameter) == false)
                        .Select(CreateParameterDescriptor)
                        .ToList()
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
                    var attribute = method.GetCustomAttribute<SdkToolAttribute>(inherit: false)
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
                            Tags = NormalizeTags(attribute.Tags),
                            RequiresAuth = attribute.RequiresAuth,
                            Parameters = method.GetParameters()
                                .Where(parameter => IsInjectedParameter(parameter) == false)
                                .Select(CreateToolParameterInfo)
                                .ToList(),
                            ResponseFields = method.GetCustomAttributes<SdkToolResponseAttribute>(inherit: false)
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

    private static List<string> NormalizeTags(IEnumerable<string> tags)
    {
        if (tags == null)
            return [];

        return tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Task<OperationEnvelope> InvokeAsync(
        ToolClass toolClass,
        MethodInfo method,
        CapabilityDescriptor descriptor,
        CapabilityInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var operationContext = OperationContext.Capture();
        var sdkContext = RimBridgeSdkHost.CreateContext(operationContext);
        var timeoutMs = ResolveExecutionTimeoutMs(invocation.Arguments);

        return _runner.RunAsync(
            () => RimBridgeAsyncScheduler.RunAsync(
                () => InvokeMethodAsync(toolClass, method, invocation.Arguments, sdkContext, cancellationToken),
                sdkContext,
                operationContext,
                cancellationToken),
            new OperationExecutionOptions
            {
                OperationId = invocation.OperationId,
                CapabilityId = descriptor.Id,
                StartedAtUtc = invocation.RequestedAtUtc,
                MarshalToMainThread = false,
                TimeoutMs = timeoutMs,
                FailureCode = "capability.failed",
                TimeoutCode = "capability.timed_out",
                CancellationCode = "capability.cancelled"
            });
    }

    private static async Task<object> InvokeMethodAsync(
        ToolClass toolClass,
        MethodInfo method,
        IDictionary<string, object> arguments,
        IRimBridgeContext sdkContext,
        CancellationToken cancellationToken)
    {
        var result = method.Invoke(
            method.IsStatic ? null : toolClass.Instance,
            BindArguments(method, arguments, sdkContext, cancellationToken));

        return await AwaitReturnValueAsync(result).ConfigureAwait(true);
    }

    private static async Task<object> AwaitReturnValueAsync(object result)
    {
        if (result == null)
            return null;

        if (result is Task task)
        {
            await task.ConfigureAwait(true);
            return TryGetTaskResult(task);
        }

        var resultType = result.GetType();
        if (resultType.FullName?.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal) == true)
        {
            var asTask = resultType.GetMethod("AsTask", BindingFlags.Instance | BindingFlags.Public, binder: null, Type.EmptyTypes, modifiers: null);
            if (asTask == null)
                return null;

            var taskResult = (Task)asTask.Invoke(result, Array.Empty<object>());
            await taskResult.ConfigureAwait(true);
            return TryGetTaskResult(taskResult);
        }

        return result;
    }

    private static object TryGetTaskResult(Task task)
    {
        var property = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
        return property?.GetValue(task);
    }

    private static int ResolveExecutionTimeoutMs(IDictionary<string, object> arguments)
    {
        if (arguments != null && arguments.TryGetValue(ExecutionTimeoutArgumentName, out var suppliedValue))
        {
            try
            {
                var requested = Convert.ToInt32(suppliedValue, CultureInfo.InvariantCulture);
                return Math.Max(MinimumExtensionToolTimeoutMs, Math.Min(MaximumExtensionToolTimeoutMs, requested));
            }
            catch
            {
                return DefaultExtensionToolTimeoutMs;
            }
        }

        return DefaultExtensionToolTimeoutMs;
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

    private static bool IsInjectedParameter(ParameterInfo parameter)
    {
        return typeof(IRimBridgeContext).IsAssignableFrom(parameter.ParameterType)
            || parameter.ParameterType == typeof(CancellationToken);
    }

    private static CapabilityParameterDescriptor CreateParameterDescriptor(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttribute<SdkToolParameterAttribute>(inherit: false);
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
        var attribute = parameter.GetCustomAttribute<SdkToolParameterAttribute>(inherit: false);
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

    private static string DescribeReturnType(Type returnType)
    {
        if (returnType == typeof(Task))
            return typeof(void).FullName;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            return returnType.GetGenericArguments()[0].FullName ?? returnType.GetGenericArguments()[0].Name;
        if (returnType.FullName == "System.Threading.Tasks.ValueTask")
            return typeof(void).FullName;
        if (returnType.IsGenericType && returnType.FullName?.StartsWith("System.Threading.Tasks.ValueTask`1", StringComparison.Ordinal) == true)
            return returnType.GetGenericArguments()[0].FullName ?? returnType.GetGenericArguments()[0].Name;

        return returnType.FullName ?? returnType.Name;
    }

    private static object[] BindArguments(
        MethodInfo method,
        IDictionary<string, object> arguments,
        IRimBridgeContext sdkContext,
        CancellationToken cancellationToken)
    {
        var parameters = method.GetParameters();
        var result = new object[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (typeof(IRimBridgeContext).IsAssignableFrom(parameter.ParameterType))
            {
                result[i] = sdkContext;
                continue;
            }

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                result[i] = cancellationToken;
                continue;
            }

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
