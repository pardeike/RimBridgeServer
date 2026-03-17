using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lib.GAB.Tools;
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
            TimeoutMs = 10000,
            FailureCode = "capability.failed"
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

        if (nonNullableType.IsEnum)
        {
            if (value is string enumText)
                return Enum.Parse(nonNullableType, enumText, ignoreCase: true);

            return Enum.ToObject(nonNullableType, value);
        }

        return Convert.ChangeType(value, nonNullableType, CultureInfo.InvariantCulture);
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
            "rimbridge/compile_lua" => CapabilityExecutionKind.Immediate,
            "rimworld/go_to_main_menu" => CapabilityExecutionKind.LongEventBound,
            "rimworld/start_debug_game" => CapabilityExecutionKind.LongEventBound,
            "rimworld/load_game" => CapabilityExecutionKind.LongEventBound,
            "rimworld/take_screenshot" => CapabilityExecutionKind.BackgroundObserved,
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
