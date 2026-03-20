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

namespace RimBridgeServer;

internal sealed class BuiltInCapabilityModuleProvider : IRimBridgeCapabilityProvider
{
    private readonly string _category;
    private readonly object _module;
    private readonly OperationRunner _runner;
    private readonly CapabilitySourceKind _source;
    private readonly List<RimBridgeCapabilityRegistration> _registrations;

    public BuiltInCapabilityModuleProvider(string providerId, string category, object module, Type aliasMetadataType, CapabilitySourceKind source)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        _category = category ?? throw new ArgumentNullException(nameof(category));
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _runner = new OperationRunner(new MainThreadDispatcher());
        _source = source;
        _registrations = BuildRegistrations(aliasMetadataType ?? throw new ArgumentNullException(nameof(aliasMetadataType)));
    }

    public string ProviderId { get; }

    public IEnumerable<RimBridgeCapabilityRegistration> GetCapabilities()
    {
        return _registrations;
    }

    private List<RimBridgeCapabilityRegistration> BuildRegistrations(Type aliasMetadataType)
    {
        var metadataByMethodName = aliasMetadataType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => new { Method = method, Attribute = method.GetCustomAttribute<ToolAttribute>() })
            .Where(entry => entry.Attribute != null)
            .ToDictionary(entry => entry.Method.Name, entry => entry, StringComparer.Ordinal);

        return _module.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => metadataByMethodName.ContainsKey(method.Name))
            .OrderBy(method => metadataByMethodName[method.Name].Attribute.Name, StringComparer.Ordinal)
            .Select(method =>
            {
                var metadata = metadataByMethodName[method.Name];
                var descriptor = CreateDescriptor(method, metadata.Method, metadata.Attribute);
                return new RimBridgeCapabilityRegistration(descriptor, (invocation, cancellationToken) => InvokeAsync(method, descriptor, invocation, cancellationToken));
            })
            .ToList();
    }

    private CapabilityDescriptor CreateDescriptor(MethodInfo implementationMethod, MethodInfo aliasMethod, ToolAttribute attribute)
    {
        return new CapabilityDescriptor
        {
            Id = ProviderId + "/" + ToKebabCase(implementationMethod.Name),
            ProviderId = ProviderId,
            Category = _category,
            Title = string.IsNullOrWhiteSpace(attribute.Title) ? attribute.Name : attribute.Title,
            Summary = attribute.Description ?? string.Empty,
            Source = _source,
            ExecutionKind = ResolveExecutionKind(attribute.Name),
            SupportedModes = CapabilityExecutionMode.Wait,
            EmitsEvents = true,
            ResultType = implementationMethod.ReturnType.FullName ?? implementationMethod.ReturnType.Name,
            Aliases = [attribute.Name],
            Parameters = aliasMethod.GetParameters().Select(CreateParameterDescriptor).ToList()
        };
    }

    private Task<OperationEnvelope> InvokeAsync(MethodInfo method, CapabilityDescriptor descriptor, CapabilityInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var envelope = _runner.Run(() => method.Invoke(_module, BindArguments(method, invocation.Arguments)), new OperationExecutionOptions
        {
            OperationId = invocation.OperationId,
            CapabilityId = descriptor.Id,
            StartedAtUtc = invocation.RequestedAtUtc,
            MarshalToMainThread = RequiresMainThread(descriptor.ExecutionKind),
            // Use caller-level timeouts instead of an internal hard cap that can misclassify slow frames or large saves.
            TimeoutMs = 0,
            FailureCode = "capability.failed",
            TimeoutCode = "capability.timed_out",
            CancellationCode = "capability.cancelled"
        });

        return Task.FromResult(envelope);
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

    private static bool RequiresMainThread(CapabilityExecutionKind executionKind)
    {
        return executionKind != CapabilityExecutionKind.Immediate
            && executionKind != CapabilityExecutionKind.BackgroundObserved;
    }

    private static CapabilityExecutionKind ResolveExecutionKind(string toolName)
    {
        return toolName switch
        {
            "rimbridge/ping" => CapabilityExecutionKind.Immediate,
            "rimbridge/get_bridge_status" => CapabilityExecutionKind.Immediate,
            "rimbridge/get_operation" => CapabilityExecutionKind.Immediate,
            "rimbridge/list_operations" => CapabilityExecutionKind.Immediate,
            "rimbridge/list_operation_events" => CapabilityExecutionKind.Immediate,
            "rimbridge/list_logs" => CapabilityExecutionKind.Immediate,
            "rimbridge/wait_for_operation" => CapabilityExecutionKind.Immediate,
            "rimbridge/wait_for_game_loaded" => CapabilityExecutionKind.Immediate,
            "rimbridge/wait_for_long_event_idle" => CapabilityExecutionKind.Immediate,
            "rimbridge/get_script_reference" => CapabilityExecutionKind.Immediate,
            "rimbridge/get_lua_reference" => CapabilityExecutionKind.Immediate,
            "rimbridge/run_script" => CapabilityExecutionKind.Immediate,
            "rimbridge/run_lua" => CapabilityExecutionKind.Immediate,
            "rimbridge/run_lua_file" => CapabilityExecutionKind.Immediate,
            "rimbridge/compile_lua" => CapabilityExecutionKind.Immediate,
            "rimbridge/compile_lua_file" => CapabilityExecutionKind.Immediate,
            "rimworld/go_to_main_menu" => CapabilityExecutionKind.LongEventBound,
            "rimworld/start_debug_game" => CapabilityExecutionKind.LongEventBound,
            "rimworld/load_game" => CapabilityExecutionKind.LongEventBound,
            "rimworld/switch_language" => CapabilityExecutionKind.LongEventBound,
            "rimworld/play_for" => CapabilityExecutionKind.BackgroundObserved,
            "rimworld/take_screenshot" => CapabilityExecutionKind.BackgroundObserved,
            "rimworld/frame_cell_rect" => CapabilityExecutionKind.BackgroundObserved,
            "rimworld/screenshot_cell_rect" => CapabilityExecutionKind.BackgroundObserved,
            "rimworld/get_ui_layout" => CapabilityExecutionKind.BackgroundObserved,
            "rimworld/click_ui_target" => CapabilityExecutionKind.BackgroundObserved,
            "rimworld/set_hover_target" => CapabilityExecutionKind.BackgroundObserved,
            "rimworld/clear_hover_target" => CapabilityExecutionKind.BackgroundObserved,
            "rimworld/open_context_menu" => CapabilityExecutionKind.BackgroundObserved,
            "rimworld/right_click_cell" => CapabilityExecutionKind.BackgroundObserved,
            _ => CapabilityExecutionKind.MainThread
        };
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = new List<char>(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch) && i > 0)
                chars.Add('-');

            chars.Add(char.ToLowerInvariant(ch));
        }

        return new string(chars.ToArray());
    }
}
