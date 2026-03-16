using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;

namespace RimBridgeServer;

internal sealed class ScriptingCapabilityModule
{
    private readonly CapabilityScriptRunner _runner;

    public ScriptingCapabilityModule(CapabilityRegistry registry)
    {
        _runner = new CapabilityScriptRunner(registry ?? throw new ArgumentNullException(nameof(registry)));
    }

    public object RunScript(string scriptJson, bool includeStepResults = true)
    {
        if (string.IsNullOrWhiteSpace(scriptJson))
        {
            return new
            {
                success = false,
                message = "A non-empty scriptJson payload is required."
            };
        }

        CapabilityScriptDefinition definition;
        try
        {
            definition = JsonConvert.DeserializeObject<CapabilityScriptDefinition>(scriptJson);
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                message = "The scriptJson payload could not be parsed.",
                exception = ex.Message
            };
        }

        if (definition == null)
        {
            return new
            {
                success = false,
                message = "The scriptJson payload did not produce a script definition."
            };
        }

        NormalizeDefinition(definition);
        if (definition.Steps.Count == 0)
        {
            return new
            {
                success = false,
                message = "The script must contain at least one step."
            };
        }

        var report = _runner.Execute(definition, includeStepResults);
        return new
        {
            success = report.Success,
            message = report.Success
                ? $"Executed {report.ExecutedStepCount} script steps successfully."
                : report.Halted
                    ? report.HaltReason
                    : $"Executed {report.ExecutedStepCount} script steps with {report.FailedStepCount} failure(s).",
            script = ProjectReport(report)
        };
    }

    private static object ProjectReport(CapabilityScriptReport report)
    {
        return new
        {
            name = report.Name,
            continueOnError = report.ContinueOnError,
            success = report.Success,
            halted = report.Halted,
            haltReason = report.HaltReason,
            stepCount = report.StepCount,
            executedStepCount = report.ExecutedStepCount,
            succeededStepCount = report.SucceededStepCount,
            failedStepCount = report.FailedStepCount,
            startedAtUtc = report.StartedAtUtc,
            completedAtUtc = report.CompletedAtUtc,
            durationMs = report.DurationMs,
            steps = report.Steps.ConvertAll(ProjectStep)
        };
    }

    private static object ProjectStep(CapabilityScriptStepReport step)
    {
        return new
        {
            index = step.Index,
            id = step.Id,
            call = step.Call,
            capabilityId = step.CapabilityId,
            operationId = step.OperationId,
            status = step.Status,
            success = step.Success,
            startedAtUtc = step.StartedAtUtc,
            completedAtUtc = step.CompletedAtUtc,
            durationMs = step.DurationMs,
            result = step.Result,
            error = step.Error,
            warnings = step.Warnings
        };
    }

    private static void NormalizeDefinition(CapabilityScriptDefinition definition)
    {
        definition.Name = definition.Name?.Trim() ?? string.Empty;
        definition.Steps ??= [];

        foreach (var step in definition.Steps)
        {
            if (step == null)
                continue;

            step.Id = step.Id?.Trim() ?? string.Empty;
            step.Call = step.Call?.Trim() ?? string.Empty;
            step.Arguments = NormalizeArguments(step.Arguments);
        }
    }

    private static Dictionary<string, object> NormalizeArguments(IDictionary<string, object> arguments)
    {
        if (arguments == null)
            return [];

        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in arguments)
            normalized[pair.Key] = NormalizeValue(pair.Value);

        return normalized;
    }

    private static object NormalizeValue(object value)
    {
        return value switch
        {
            null => null,
            JObject jobject => NormalizeObject(jobject),
            JArray jarray => NormalizeArray(jarray),
            JValue jvalue => jvalue.Value,
            IDictionary<string, object> dictionary => NormalizeArguments(dictionary),
            IEnumerable<object> items when value is not string => NormalizeEnumerable(items),
            _ => value
        };
    }

    private static Dictionary<string, object> NormalizeObject(JObject jobject)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in jobject.Properties())
            normalized[property.Name] = NormalizeValue(property.Value);

        return normalized;
    }

    private static List<object> NormalizeArray(JArray jarray)
    {
        var normalized = new List<object>(jarray.Count);
        foreach (var item in jarray)
            normalized.Add(NormalizeValue(item));

        return normalized;
    }

    private static List<object> NormalizeEnumerable(IEnumerable<object> items)
    {
        var normalized = new List<object>();
        foreach (var item in items)
            normalized.Add(NormalizeValue(item));

        return normalized;
    }
}
