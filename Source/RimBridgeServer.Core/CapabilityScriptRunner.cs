using System;
using System.Collections.Generic;
using RimBridgeServer.Contracts;

namespace RimBridgeServer.Core;

public sealed class CapabilityScriptRunner
{
    private readonly CapabilityRegistry _registry;

    public CapabilityScriptRunner(CapabilityRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public CapabilityScriptReport Execute(CapabilityScriptDefinition definition, bool includeStepResults = true)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        var startedAtUtc = DateTimeOffset.UtcNow;
        var report = new CapabilityScriptReport
        {
            Name = definition.Name ?? string.Empty,
            ContinueOnError = definition.ContinueOnError,
            StepCount = definition.Steps?.Count ?? 0,
            StartedAtUtc = startedAtUtc
        };

        foreach (var (step, index) in EnumerateSteps(definition))
        {
            var stepReport = ExecuteStep(step, index, includeStepResults);
            report.Steps.Add(stepReport);
            report.ExecutedStepCount++;

            if (stepReport.Success)
            {
                report.SucceededStepCount++;
            }
            else
            {
                report.FailedStepCount++;
                if (!definition.ContinueOnError)
                {
                    report.Halted = true;
                    report.HaltReason = $"Step {stepReport.Index} ('{stepReport.Id}') failed.";
                    break;
                }
            }
        }

        report.CompletedAtUtc = DateTimeOffset.UtcNow;
        report.DurationMs = (long)(report.CompletedAtUtc - report.StartedAtUtc).TotalMilliseconds;
        report.Success = report.FailedStepCount == 0 && report.Halted == false;
        return report;
    }

    private CapabilityScriptStepReport ExecuteStep(CapabilityScriptStep step, int index, bool includeStepResults)
    {
        if (step == null)
        {
            return CreateInvalidStepReport(index, $"step-{index}", string.Empty, "The script contained a null step.");
        }

        var stepId = string.IsNullOrWhiteSpace(step.Id) ? $"step-{index}" : step.Id.Trim();
        var call = step.Call?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(call))
            return CreateInvalidStepReport(index, stepId, call, "Each script step must declare a capability call.");

        if (string.Equals(call, "rimbridge/run_script", StringComparison.OrdinalIgnoreCase))
            return CreateInvalidStepReport(index, stepId, call, "Nested rimbridge/run_script calls are not supported in this version.");

        var arguments = step.Arguments != null
            ? new Dictionary<string, object>(step.Arguments, StringComparer.Ordinal)
            : [];
        var envelope = _registry.Invoke(call, arguments);
        return new CapabilityScriptStepReport
        {
            Index = index,
            Id = stepId,
            Call = call,
            CapabilityId = envelope.CapabilityId,
            OperationId = envelope.OperationId,
            Status = envelope.Status,
            Success = envelope.Success,
            StartedAtUtc = envelope.StartedAtUtc,
            CompletedAtUtc = envelope.CompletedAtUtc,
            DurationMs = envelope.DurationMs,
            Result = includeStepResults ? envelope.Result : null,
            Error = envelope.Error == null
                ? null
                : new OperationError
                {
                    Code = envelope.Error.Code,
                    Message = envelope.Error.Message,
                    ExceptionType = envelope.Error.ExceptionType,
                    Details = envelope.Error.Details
                },
            Warnings = [.. envelope.Warnings]
        };
    }

    private static CapabilityScriptStepReport CreateInvalidStepReport(int index, string stepId, string call, string message)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        return new CapabilityScriptStepReport
        {
            Index = index,
            Id = stepId,
            Call = call ?? string.Empty,
            Status = OperationStatus.Failed,
            Success = false,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = startedAtUtc,
            DurationMs = 0,
            Error = new OperationError
            {
                Code = "script.invalid_step",
                Message = message,
                ExceptionType = typeof(InvalidOperationException).FullName ?? nameof(InvalidOperationException)
            }
        };
    }

    private static IEnumerable<(CapabilityScriptStep Step, int Index)> EnumerateSteps(CapabilityScriptDefinition definition)
    {
        if (definition.Steps == null)
            yield break;

        for (var index = 0; index < definition.Steps.Count; index++)
            yield return (definition.Steps[index], index + 1);
    }
}
