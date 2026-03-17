using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;

namespace RimBridgeServer;

internal sealed class ScriptingCapabilityModule
{
    private static readonly JsonSerializerSettings ScriptJsonSerializerSettings = new()
    {
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore
    };

    private readonly CapabilityScriptRunner _runner;
    private readonly LuaScriptCompiler _luaCompiler;

    public ScriptingCapabilityModule(CapabilityRegistry registry)
    {
        _runner = new CapabilityScriptRunner(registry ?? throw new ArgumentNullException(nameof(registry)));
        _luaCompiler = new LuaScriptCompiler();
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
            definition = JsonConvert.DeserializeObject<CapabilityScriptDefinition>(scriptJson, ScriptJsonSerializerSettings);
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
        if (!TryValidateDefinitionHasSteps(definition, out var validationFailure))
            return validationFailure;

        var report = _runner.Execute(definition, includeStepResults);
        return new
        {
            success = report.Success,
            message = report.Success
                ? report.Returned
                    ? "Script returned successfully."
                    : $"Executed {report.ExecutedStepCount} script steps successfully."
                : report.Error?.Message
                    ?? (report.Halted
                        ? report.HaltReason
                        : $"Executed {report.ExecutedStepCount} script steps with {report.FailedStepCount} failure(s)."),
            returned = report.Returned,
            result = report.Result,
            error = report.Error,
            output = report.Output.ConvertAll(ProjectOutput),
            script = ProjectReport(report)
        };
    }

    public object RunLua(string luaSource, bool includeStepResults = true)
    {
        if (string.IsNullOrWhiteSpace(luaSource))
        {
            return new
            {
                success = false,
                message = "A non-empty luaSource payload is required."
            };
        }

        CapabilityScriptDefinition definition;
        try
        {
            definition = _luaCompiler.Compile(luaSource);
        }
        catch (LuaScriptCompileException ex)
        {
            return new
            {
                success = false,
                message = ex.Message,
                error = ProjectLuaCompileError(ex)
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                message = "The luaSource payload could not be compiled.",
                error = new
                {
                    code = "lua.compile_error",
                    message = ex.Message,
                    details = (object)null
                }
            };
        }

        NormalizeDefinition(definition);
        if (!TryValidateDefinitionHasSteps(definition, out var validationFailure))
            return validationFailure;

        var report = _runner.Execute(definition, includeStepResults);
        return new
        {
            success = report.Success,
            message = report.Success
                ? report.Returned
                    ? "Lua script returned successfully."
                    : $"Executed {report.ExecutedStepCount} Lua script steps successfully."
                : report.Error?.Message
                    ?? (report.Halted
                        ? report.HaltReason
                        : $"Executed {report.ExecutedStepCount} Lua script steps with {report.FailedStepCount} failure(s)."),
            returned = report.Returned,
            result = report.Result,
            error = report.Error,
            output = report.Output.ConvertAll(ProjectOutput),
            script = ProjectReport(report)
        };
    }

    public object CompileLua(string luaSource)
    {
        if (string.IsNullOrWhiteSpace(luaSource))
        {
            return new
            {
                success = false,
                message = "A non-empty luaSource payload is required."
            };
        }

        CapabilityScriptDefinition definition;
        try
        {
            definition = _luaCompiler.Compile(luaSource);
        }
        catch (LuaScriptCompileException ex)
        {
            return new
            {
                success = false,
                message = ex.Message,
                error = ProjectLuaCompileError(ex)
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                message = "The luaSource payload could not be compiled.",
                error = new
                {
                    code = "lua.compile_error",
                    message = ex.Message,
                    details = (object)null
                }
            };
        }

        NormalizeDefinition(definition);
        if (!TryValidateDefinitionHasSteps(definition, out var validationFailure))
            return validationFailure;

        return new
        {
            success = true,
            message = $"Compiled Lua into {definition.Steps.Count} top-level script statement(s).",
            script = definition,
            scriptJson = JsonConvert.SerializeObject(definition, Formatting.Indented)
        };
    }

    public object GetScriptReference()
    {
        return CapabilityScriptReferenceBuilder.CreateDocument();
    }

    public object GetLuaReference()
    {
        return CapabilityLuaReferenceBuilder.CreateDocument();
    }

    private static object ProjectLuaCompileError(LuaScriptCompileException ex)
    {
        return new
        {
            code = ex.Code,
            message = ex.Message,
            line = ex.Line,
            column = ex.Column,
            details = ex.Details
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
            returned = report.Returned,
            haltReason = report.HaltReason,
            result = report.Result,
            error = report.Error,
            stepCount = report.StepCount,
            executedStepCount = report.ExecutedStepCount,
            succeededStepCount = report.SucceededStepCount,
            failedStepCount = report.FailedStepCount,
            startedAtUtc = report.StartedAtUtc,
            completedAtUtc = report.CompletedAtUtc,
            durationMs = report.DurationMs,
            output = report.Output.ConvertAll(ProjectOutput),
            steps = report.Steps.ConvertAll(ProjectStep)
        };
    }

    private static object ProjectOutput(CapabilityScriptOutputEntry entry)
    {
        return new
        {
            index = entry.Index,
            statementId = entry.StatementId,
            level = entry.Level,
            message = entry.Message,
            value = entry.Value,
            timestampUtc = entry.TimestampUtc
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
            attempts = step.Attempts,
            startedAtUtc = step.StartedAtUtc,
            completedAtUtc = step.CompletedAtUtc,
            durationMs = step.DurationMs,
            result = step.Result,
            error = step.Error,
            warnings = step.Warnings
        };
    }

    private static bool TryValidateDefinitionHasSteps(CapabilityScriptDefinition definition, out object failure)
    {
        if (definition.Steps.Count > 0)
        {
            failure = null;
            return true;
        }

        failure = new
        {
            success = false,
            message = "The script must contain at least one step."
        };
        return false;
    }

    private static void NormalizeDefinition(CapabilityScriptDefinition definition)
    {
        definition.Name = definition.Name?.Trim() ?? string.Empty;
        definition.Steps ??= [];
        NormalizeStatements(definition.Steps);
    }

    private static void NormalizeStatements(IList<CapabilityScriptStep> steps)
    {
        if (steps == null)
            return;

        foreach (var step in steps)
        {
            if (step == null)
                continue;

            step.Type = step.Type?.Trim() ?? string.Empty;
            step.Id = step.Id?.Trim() ?? string.Empty;
            step.Message = step.Message?.Trim() ?? string.Empty;
            step.Call = step.Call?.Trim() ?? string.Empty;
            step.Name = step.Name?.Trim() ?? string.Empty;
            step.ItemName = step.ItemName?.Trim() ?? string.Empty;
            step.IndexName = step.IndexName?.Trim() ?? string.Empty;
            step.Arguments = NormalizeArguments(step.Arguments);
            step.Value = NormalizeValue(step.Value);
            step.Condition = NormalizeArguments(step.Condition);
            step.Collection = NormalizeValue(step.Collection);
            step.Body ??= [];
            step.ElseBody ??= [];
            NormalizeStatements(step.Body);
            NormalizeStatements(step.ElseBody);
            if (step.ContinueUntil != null)
            {
                step.ContinueUntil.TimeoutMessage = step.ContinueUntil.TimeoutMessage?.Trim() ?? string.Empty;
                step.ContinueUntil.Condition = NormalizeArguments(step.ContinueUntil.Condition);
            }
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
