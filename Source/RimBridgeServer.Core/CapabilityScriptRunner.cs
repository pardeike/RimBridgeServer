using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using RimBridgeServer.Contracts;

namespace RimBridgeServer.Core;

public sealed class CapabilityScriptRunner
{
    private readonly CapabilityRegistry _registry;

    private sealed class ExecutedStepContext
    {
        public string BaseId { get; set; } = string.Empty;

        public CapabilityScriptStepReport Report { get; set; }

        public object RawResult { get; set; }
    }

    private sealed class ScriptExecutionState
    {
        private readonly Dictionary<string, CapabilityScriptStep> _callStatementsByBaseId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _executionCountsByBaseId = new(StringComparer.Ordinal);
        private readonly Dictionary<CapabilityScriptStep, string> _statementIdsByReference = [];
        private readonly List<Dictionary<string, object>> _scopes = [new(StringComparer.Ordinal)];
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _nextImplicitStatementId = 1;
        private int _nextReportIndex = 1;
        private int _nextOutputIndex = 1;
        private int _executedStatements;
        private int _currentControlDepth;

        public ScriptExecutionState(
            CapabilityScriptReport report,
            bool includeStepResults,
            bool continueOnError,
            int maxDurationMs,
            int maxExecutedStatements,
            int maxControlDepth)
        {
            Report = report ?? throw new ArgumentNullException(nameof(report));
            IncludeStepResults = includeStepResults;
            ContinueOnError = continueOnError;
            MaxDurationMs = maxDurationMs;
            MaxExecutedStatements = maxExecutedStatements;
            MaxControlDepth = maxControlDepth;
        }

        public CapabilityScriptReport Report { get; }

        public bool IncludeStepResults { get; }

        public bool ContinueOnError { get; }

        public int MaxDurationMs { get; }

        public int MaxExecutedStatements { get; }

        public int MaxControlDepth { get; }

        public bool ShouldStop => Report.Halted || Report.Returned;

        public Dictionary<string, ExecutedStepContext> ExecutedStepsByBaseId { get; } = new(StringComparer.Ordinal);

        public int AllocateReportIndex()
        {
            return _nextReportIndex++;
        }

        public int AllocateOutputIndex()
        {
            return _nextOutputIndex++;
        }

        public string GetOrAssignStatementId(CapabilityScriptStep step, string defaultPrefix)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));

            if (!string.IsNullOrWhiteSpace(step.Id))
                return step.Id.Trim();

            if (_statementIdsByReference.TryGetValue(step, out var existing))
                return existing;

            var created = defaultPrefix + _nextImplicitStatementId.ToString(CultureInfo.InvariantCulture);
            _nextImplicitStatementId++;
            _statementIdsByReference[step] = created;
            return created;
        }

        public bool TryRegisterCallStatement(string baseId, CapabilityScriptStep step, out CapabilityScriptStep existing)
        {
            if (_callStatementsByBaseId.TryGetValue(baseId, out existing))
                return ReferenceEquals(existing, step);

            _callStatementsByBaseId[baseId] = step;
            existing = step;
            return true;
        }

        public string CreateExecutionReportId(string baseId)
        {
            var nextCount = _executionCountsByBaseId.TryGetValue(baseId, out var existingCount)
                ? existingCount + 1
                : 1;
            _executionCountsByBaseId[baseId] = nextCount;
            return nextCount == 1
                ? baseId
                : baseId + "#" + nextCount.ToString(CultureInfo.InvariantCulture);
        }

        public ScopeHandle PushScope()
        {
            _scopes.Add(new Dictionary<string, object>(StringComparer.Ordinal));
            return new ScopeHandle(this);
        }

        public void DeclareVariable(string name, object value)
        {
            _scopes[_scopes.Count - 1][name] = value;
        }

        public bool TryAssignVariable(string name, object value)
        {
            for (var index = _scopes.Count - 1; index >= 0; index--)
            {
                if (_scopes[index].ContainsKey(name))
                {
                    _scopes[index][name] = value;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetVariable(string name, out object value)
        {
            for (var index = _scopes.Count - 1; index >= 0; index--)
            {
                if (_scopes[index].TryGetValue(name, out value))
                    return true;
            }

            value = null;
            return false;
        }

        public Dictionary<string, object> CreateVariableSnapshot()
        {
            var merged = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var scope in _scopes)
            {
                foreach (var pair in scope)
                    merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        public void RecordExecutedStep(ExecutedStepContext executed, bool referenceable, bool haltOnFailure = false)
        {
            if (executed == null)
                throw new ArgumentNullException(nameof(executed));

            Report.Steps.Add(executed.Report);
            Report.ExecutedStepCount++;

            if (executed.Report.Success)
            {
                Report.SucceededStepCount++;
            }
            else
            {
                Report.FailedStepCount++;
                Report.Error ??= executed.Report.Error;
                if (haltOnFailure || !ContinueOnError)
                {
                    Report.Halted = true;
                    Report.HaltReason = $"Step {executed.Report.Index} ('{executed.Report.Id}') failed.";
                }
            }

            if (referenceable && !string.IsNullOrWhiteSpace(executed.BaseId))
                ExecutedStepsByBaseId[executed.BaseId] = executed;
        }

        public void AppendOutput(string statementId, string message, object value, string level = "info")
        {
            Report.Output.Add(new CapabilityScriptOutputEntry
            {
                Index = AllocateOutputIndex(),
                StatementId = statementId ?? string.Empty,
                Level = string.IsNullOrWhiteSpace(level) ? "info" : level.Trim(),
                Message = message ?? string.Empty,
                Value = value,
                TimestampUtc = DateTimeOffset.UtcNow
            });
        }

        public void Return(object value)
        {
            Report.Returned = true;
            Report.Result = value;
        }

        public bool TryReserveExecution(string statementId, string description, out OperationError error)
        {
            if (!TryCheckDuration(statementId, description, out error))
                return false;

            _executedStatements++;
            if (_executedStatements > MaxExecutedStatements)
            {
                error = CreateScriptError(
                    "script.statement_limit_exceeded",
                    $"Script exceeded maxExecutedStatements={MaxExecutedStatements} while executing {description}.",
                    CreateLimitDetails(
                        statementId,
                        description,
                        ("maxExecutedStatements", MaxExecutedStatements),
                        ("executedStatements", _executedStatements)));
                return false;
            }

            return true;
        }

        public bool TryCheckDuration(string statementId, string description, out OperationError error)
        {
            var elapsedMs = _stopwatch.ElapsedMilliseconds;
            if (elapsedMs <= MaxDurationMs)
            {
                error = null;
                return true;
            }

            error = CreateScriptError(
                "script.timeout",
                $"Script exceeded maxDurationMs={MaxDurationMs} while executing {description}.",
                CreateLimitDetails(
                    statementId,
                    description,
                    ("maxDurationMs", MaxDurationMs),
                    ("elapsedMs", elapsedMs)));
            return false;
        }

        public bool TryEnterControlScope(string statementId, string description, out ControlScopeHandle handle, out OperationError error)
        {
            handle = default;
            if (!TryCheckDuration(statementId, description, out error))
                return false;

            var nextDepth = _currentControlDepth + 1;
            if (nextDepth > MaxControlDepth)
            {
                error = CreateScriptError(
                    "script.max_depth_exceeded",
                    $"Script exceeded maxControlDepth={MaxControlDepth} while executing {description}.",
                    CreateLimitDetails(
                        statementId,
                        description,
                        ("maxControlDepth", MaxControlDepth),
                        ("attemptedControlDepth", nextDepth)));
                return false;
            }

            _currentControlDepth = nextDepth;
            handle = new ControlScopeHandle(this);
            return true;
        }

        private void PopScope()
        {
            if (_scopes.Count <= 1)
                throw new InvalidOperationException("Cannot pop the root script scope.");

            _scopes.RemoveAt(_scopes.Count - 1);
        }

        private void ExitControlScope()
        {
            if (_currentControlDepth <= 0)
                throw new InvalidOperationException("Cannot exit a control scope when no control scope is active.");

            _currentControlDepth--;
        }

        private Dictionary<string, object> CreateLimitDetails(string statementId, string description, params (string Key, object Value)[] extras)
        {
            var details = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["statementId"] = statementId ?? string.Empty,
                ["description"] = description ?? string.Empty,
                ["elapsedMs"] = _stopwatch.ElapsedMilliseconds,
                ["executedStatements"] = _executedStatements,
                ["currentControlDepth"] = _currentControlDepth
            };

            foreach (var extra in extras)
                details[extra.Key] = extra.Value;

            return details;
        }

        public readonly struct ScopeHandle : IDisposable
        {
            private readonly ScriptExecutionState _state;

            public ScopeHandle(ScriptExecutionState state)
            {
                _state = state;
            }

            public void Dispose()
            {
                _state?.PopScope();
            }
        }

        public readonly struct ControlScopeHandle : IDisposable
        {
            private readonly ScriptExecutionState _state;

            public ControlScopeHandle(ScriptExecutionState state)
            {
                _state = state;
            }

            public void Dispose()
            {
                _state?.ExitControlScope();
            }
        }
    }

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
            StepCount = CountStatements(definition.Steps),
            StartedAtUtc = startedAtUtc
        };
        var definitionError = ValidateDefinition(definition);
        if (definitionError != null)
        {
            report.Halted = true;
            report.HaltReason = definitionError.Message;
            report.Error = definitionError;
            report.CompletedAtUtc = DateTimeOffset.UtcNow;
            report.DurationMs = (long)(report.CompletedAtUtc - report.StartedAtUtc).TotalMilliseconds;
            report.Success = false;
            return report;
        }

        var state = new ScriptExecutionState(
            report,
            includeStepResults,
            definition.ContinueOnError,
            definition.MaxDurationMs,
            definition.MaxExecutedStatements,
            definition.MaxControlDepth);

        ExecuteStatements(definition.Steps, state);

        report.CompletedAtUtc = DateTimeOffset.UtcNow;
        report.DurationMs = (long)(report.CompletedAtUtc - report.StartedAtUtc).TotalMilliseconds;
        report.Success = report.FailedStepCount == 0 && report.Halted == false;
        return report;
    }

    private void ExecuteStatements(IReadOnlyList<CapabilityScriptStep> statements, ScriptExecutionState state)
    {
        if (statements == null || state.ShouldStop)
            return;

        foreach (var statement in statements)
        {
            if (state.ShouldStop)
                return;

            ExecuteStatement(statement, state);
        }
    }

    private void ExecuteStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        if (step == null)
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    "step-" + state.Report.ExecutedStepCount.ToString(CultureInfo.InvariantCulture),
                    null,
                    string.Empty,
                    "script.invalid_step",
                    "The script contained a null step."),
                referenceable: false);
            return;
        }

        var type = ResolveStepType(step);
        switch (type)
        {
            case "call":
                ExecuteCallStatement(step, state);
                return;
            case "let":
                ExecuteLetStatement(step, state);
                return;
            case "set":
                ExecuteSetStatement(step, state);
                return;
            case "if":
                ExecuteIfStatement(step, state);
                return;
            case "foreach":
                ExecuteForeachStatement(step, state);
                return;
            case "while":
                ExecuteWhileStatement(step, state);
                return;
            case "assert":
                ExecuteAssertStatement(step, state);
                return;
            case "fail":
                ExecuteFailStatement(step, state);
                return;
            case "print":
                ExecutePrintStatement(step, state);
                return;
            case "return":
                ExecuteReturnStatement(step, state);
                return;
            default:
                var statementId = state.GetOrAssignStatementId(step, "statement-");
                state.RecordExecutedStep(
                    CreateFailedStepContext(
                        state.AllocateReportIndex(),
                        statementId,
                        null,
                        "script/" + type,
                        "script.invalid_step",
                        $"The script step '{statementId}' declares unsupported type '{type}'."),
                    referenceable: false);
                return;
        }
    }

    private void ExecuteCallStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var baseId = state.GetOrAssignStatementId(step, "step-");
        var call = step.Call?.Trim() ?? string.Empty;
        var reportId = state.CreateExecutionReportId(baseId);
        var reportIndex = state.AllocateReportIndex();
        if (!state.TryReserveExecution(baseId, $"call step '{baseId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(reportIndex, reportId, baseId, call, guardError),
                referenceable: true);
            return;
        }

        if (!state.TryRegisterCallStatement(baseId, step, out _))
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    reportIndex,
                    reportId,
                    null,
                    call,
                    "script.invalid_step",
                    $"The script step id '{baseId}' is duplicated."),
                referenceable: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(call))
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    reportIndex,
                    reportId,
                    baseId,
                    call,
                    "script.invalid_step",
                    "Each script call step must declare a capability call."),
                referenceable: true);
            return;
        }

        if (string.Equals(call, "rimbridge/run_script", StringComparison.OrdinalIgnoreCase))
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    reportIndex,
                    reportId,
                    baseId,
                    call,
                    "script.invalid_step",
                    "Nested rimbridge/run_script calls are not supported in this version."),
                referenceable: true);
            return;
        }

        Dictionary<string, object> arguments;
        try
        {
            arguments = step.Arguments != null
                ? ResolveArguments(step.Arguments, baseId, state)
                : [];
        }
        catch (Exception ex)
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    reportIndex,
                    reportId,
                    baseId,
                    call,
                    "script.invalid_reference",
                    ex.Message),
                referenceable: true);
            return;
        }

        var executed = step.ContinueUntil == null
            ? InvokeStepOnce(reportId, baseId, call, reportIndex, arguments, state.IncludeStepResults, attempts: 1)
            : ExecuteStepWithContinue(step, reportId, baseId, call, reportIndex, arguments, state);

        if (step.ContinueUntil == null
            && !state.TryCheckDuration(baseId, $"call step '{baseId}'", out guardError))
        {
            executed = CreateConditionFailureContext(reportIndex, reportId, baseId, call, guardError, executed);
        }

        state.RecordExecutedStep(executed, referenceable: true);
    }

    private void ExecuteLetStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var statementId = state.GetOrAssignStatementId(step, "let-");
        if (!state.TryReserveExecution(statementId, $"let step '{statementId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/let",
                    guardError),
                referenceable: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(step.Name))
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/let",
                    "script.invalid_step",
                    $"Step '{statementId}' declares 'let' without a variable name."),
                referenceable: false);
            return;
        }

        try
        {
            state.DeclareVariable(step.Name, ResolveValue(step.Value, statementId, state));
        }
        catch (Exception ex)
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/let",
                    "script.invalid_expression",
                    ex.Message),
                referenceable: false);
        }
    }

    private void ExecuteSetStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var statementId = state.GetOrAssignStatementId(step, "set-");
        if (!state.TryReserveExecution(statementId, $"set step '{statementId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/set",
                    guardError),
                referenceable: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(step.Name))
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/set",
                    "script.invalid_step",
                    $"Step '{statementId}' declares 'set' without a variable name."),
                referenceable: false);
            return;
        }

        try
        {
            var value = ResolveValue(step.Value, statementId, state);
            if (!state.TryAssignVariable(step.Name, value))
            {
                state.RecordExecutedStep(
                    CreateInvalidStepContext(
                        state.AllocateReportIndex(),
                        statementId,
                        null,
                        "script/set",
                        "script.invalid_variable",
                        $"Step '{statementId}' cannot assign variable '{step.Name}' because it is not declared in any active scope."),
                    referenceable: false);
                return;
            }
        }
        catch (Exception ex)
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/set",
                    "script.invalid_expression",
                    ex.Message),
                referenceable: false);
        }
    }

    private void ExecuteIfStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var statementId = state.GetOrAssignStatementId(step, "if-");
        if (!state.TryReserveExecution(statementId, $"if step '{statementId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/if",
                    guardError),
                referenceable: false);
            return;
        }

        if (step.Condition == null || step.Condition.Count == 0)
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/if",
                    "script.invalid_step",
                    $"Step '{statementId}' declares 'if' without a condition."),
                referenceable: false);
            return;
        }

        bool matched;
        try
        {
            matched = EvaluateContinueCondition(step.Condition, CreateConditionRoot(state), statementId, state, out _);
        }
        catch (Exception ex)
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/if",
                    "script.invalid_condition",
                    ex.Message),
                referenceable: false);
            return;
        }

        var branch = matched ? step.Body : step.ElseBody;
        if (!state.TryEnterControlScope(statementId, $"if body '{statementId}'", out var controlHandle, out guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/if",
                    guardError),
                referenceable: false);
            return;
        }

        using (controlHandle)
        using (state.PushScope())
            ExecuteStatements(branch, state);
    }

    private void ExecuteForeachStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var statementId = state.GetOrAssignStatementId(step, "foreach-");
        if (!state.TryReserveExecution(statementId, $"foreach step '{statementId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/foreach",
                    guardError),
                referenceable: false);
            return;
        }

        if (string.IsNullOrWhiteSpace(step.ItemName))
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/foreach",
                    "script.invalid_step",
                    $"Step '{statementId}' declares 'foreach' without an itemName."),
                referenceable: false);
            return;
        }

        object collection;
        try
        {
            collection = ResolveValue(step.Collection, statementId, state);
        }
        catch (Exception ex)
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/foreach",
                    "script.invalid_expression",
                    ex.Message),
                referenceable: false);
            return;
        }

        if (!TryEnumerateCollection(collection, out var items))
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/foreach",
                    "script.invalid_step",
                    $"Step '{statementId}' did not resolve its foreach collection to an enumerable value."),
                referenceable: false);
            return;
        }

        if (!state.TryEnterControlScope(statementId, $"foreach body '{statementId}'", out var controlHandle, out guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/foreach",
                    guardError),
                referenceable: false);
            return;
        }

        var index = 0;
        using (controlHandle)
        foreach (var item in items.ToList())
        {
            if (state.ShouldStop)
                return;

            if (!state.TryReserveExecution(statementId, $"foreach iteration {index + 1} of '{statementId}'", out guardError))
            {
                state.RecordExecutedStep(
                    CreateFailedStepContext(
                        state.AllocateReportIndex(),
                        statementId,
                        null,
                        "script/foreach",
                        guardError),
                    referenceable: false);
                return;
            }

            using (state.PushScope())
            {
                state.DeclareVariable(step.ItemName, item);
                if (!string.IsNullOrWhiteSpace(step.IndexName))
                    state.DeclareVariable(step.IndexName, index);

                ExecuteStatements(step.Body, state);
            }

            index++;
        }
    }

    private void ExecuteWhileStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var statementId = state.GetOrAssignStatementId(step, "while-");
        if (!state.TryReserveExecution(statementId, $"while step '{statementId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/while",
                    guardError),
                referenceable: false);
            return;
        }

        if (step.Condition == null || step.Condition.Count == 0)
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/while",
                    "script.invalid_step",
                    $"Step '{statementId}' declares 'while' without a condition."),
                referenceable: false);
            return;
        }

        if (step.MaxIterations <= 0)
        {
            state.RecordExecutedStep(
                CreateInvalidStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/while",
                    "script.invalid_step",
                    $"Step '{statementId}' must declare a positive maxIterations value for 'while'."),
                referenceable: false);
            return;
        }

        if (!state.TryEnterControlScope(statementId, $"while body '{statementId}'", out var controlHandle, out guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/while",
                    guardError),
                referenceable: false);
            return;
        }

        var iteration = 0;
        using (controlHandle)
        while (true)
        {
            bool matched;
            try
            {
                matched = EvaluateContinueCondition(step.Condition, CreateConditionRoot(state), statementId, state, out _);
            }
            catch (Exception ex)
            {
                state.RecordExecutedStep(
                    CreateInvalidStepContext(
                        state.AllocateReportIndex(),
                        statementId,
                        null,
                        "script/while",
                        "script.invalid_condition",
                        ex.Message),
                    referenceable: false);
                return;
            }

            if (!matched)
                return;

            iteration++;
            if (!state.TryReserveExecution(statementId, $"while iteration {iteration} of '{statementId}'", out guardError))
            {
                state.RecordExecutedStep(
                    CreateFailedStepContext(
                        state.AllocateReportIndex(),
                        statementId,
                        null,
                        "script/while",
                        guardError),
                    referenceable: false);
                return;
            }

            if (iteration > step.MaxIterations)
            {
                state.RecordExecutedStep(
                    CreateInvalidStepContext(
                        state.AllocateReportIndex(),
                        statementId,
                        null,
                        "script/while",
                        "script.max_iterations",
                        $"Step '{statementId}' exceeded maxIterations={step.MaxIterations}."),
                    referenceable: false);
                return;
            }

            using (state.PushScope())
                ExecuteStatements(step.Body, state);

            if (state.ShouldStop)
                return;
        }
    }

    private void ExecuteAssertStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var statementId = state.GetOrAssignStatementId(step, "assert-");
        if (!state.TryReserveExecution(statementId, $"assert step '{statementId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/assert",
                    guardError),
                referenceable: false);
            return;
        }

        if (step.Condition == null || step.Condition.Count == 0)
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/assert",
                    "script.invalid_step",
                    $"Step '{statementId}' declares 'assert' without a condition."),
                referenceable: false);
            return;
        }

        bool matched;
        string conditionMessage;
        try
        {
            matched = EvaluateContinueCondition(step.Condition, CreateConditionRoot(state), statementId, state, out conditionMessage);
        }
        catch (Exception ex)
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/assert",
                    "script.invalid_condition",
                    ex.Message),
                referenceable: false);
            return;
        }

        if (matched)
            return;

        var message = string.IsNullOrWhiteSpace(step.Message)
            ? $"Assertion '{statementId}' failed."
            : step.Message.Trim();
        if (!string.IsNullOrWhiteSpace(conditionMessage))
            message += " " + conditionMessage;

        state.RecordExecutedStep(
            CreateFailedStepContext(
                state.AllocateReportIndex(),
                statementId,
                null,
                "script/assert",
                "script.assertion_failed",
                message,
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["condition"] = step.Condition,
                    ["reason"] = conditionMessage
                }),
            referenceable: false,
            haltOnFailure: true);
    }

    private void ExecuteFailStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var statementId = state.GetOrAssignStatementId(step, "fail-");
        if (!state.TryReserveExecution(statementId, $"fail step '{statementId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/fail",
                    guardError),
                referenceable: false);
            return;
        }

        object details = null;
        try
        {
            if (step.Value != null)
                details = ResolveValue(step.Value, statementId, state);
        }
        catch (Exception ex)
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/fail",
                    "script.invalid_expression",
                    ex.Message),
                referenceable: false);
            return;
        }

        var message = string.IsNullOrWhiteSpace(step.Message)
            ? $"Script requested failure at '{statementId}'."
            : step.Message.Trim();

        state.RecordExecutedStep(
            CreateFailedStepContext(
                state.AllocateReportIndex(),
                statementId,
                null,
                "script/fail",
                "script.failed",
                message,
                details),
            referenceable: false,
            haltOnFailure: true);
    }

    private void ExecutePrintStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var statementId = state.GetOrAssignStatementId(step, "print-");
        if (!state.TryReserveExecution(statementId, $"print step '{statementId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/print",
                    guardError),
                referenceable: false);
            return;
        }

        object value = null;
        try
        {
            if (step.Value != null)
                value = ResolveValue(step.Value, statementId, state);
        }
        catch (Exception ex)
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/print",
                    "script.invalid_expression",
                    ex.Message),
                referenceable: false);
            return;
        }

        state.AppendOutput(statementId, step.Message?.Trim() ?? string.Empty, value);
    }

    private void ExecuteReturnStatement(CapabilityScriptStep step, ScriptExecutionState state)
    {
        var statementId = state.GetOrAssignStatementId(step, "return-");
        if (!state.TryReserveExecution(statementId, $"return step '{statementId}'", out var guardError))
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/return",
                    guardError),
                referenceable: false);
            return;
        }

        try
        {
            var value = step.Value == null
                ? null
                : ResolveValue(step.Value, statementId, state);
            state.Return(value);
        }
        catch (Exception ex)
        {
            state.RecordExecutedStep(
                CreateFailedStepContext(
                    state.AllocateReportIndex(),
                    statementId,
                    null,
                    "script/return",
                    "script.invalid_expression",
                    ex.Message),
                referenceable: false);
        }
    }

    private ExecutedStepContext ExecuteStepWithContinue(
        CapabilityScriptStep step,
        string reportId,
        string baseId,
        string call,
        int reportIndex,
        Dictionary<string, object> arguments,
        ScriptExecutionState state)
    {
        var policy = step.ContinueUntil;
        if (policy.Condition == null || policy.Condition.Count == 0)
        {
            return CreateInvalidStepContext(
                reportIndex,
                reportId,
                baseId,
                call,
                "script.invalid_step",
                $"Step '{baseId}' declares continueUntil without a condition.");
        }

        var timeoutMs = Math.Max(0, policy.TimeoutMs);
        var pollIntervalMs = Math.Max(0, policy.PollIntervalMs);
        var stopwatch = Stopwatch.StartNew();
        ExecutedStepContext lastAttempt = null;
        string lastUnsatisfiedMessage = string.Empty;

        while (true)
        {
            var attemptNumber = (lastAttempt?.Report.Attempts ?? 0) + 1;
            if (attemptNumber > 1
                && !state.TryReserveExecution(baseId, $"continueUntil attempt {attemptNumber} for call step '{baseId}'", out var guardError))
            {
                return CreateConditionFailureContext(reportIndex, reportId, baseId, call, guardError, lastAttempt);
            }

            lastAttempt = InvokeStepOnce(reportId, baseId, call, reportIndex, arguments, state.IncludeStepResults, attemptNumber);
            if (!state.TryCheckDuration(baseId, $"call step '{baseId}'", out guardError))
                return CreateConditionFailureContext(reportIndex, reportId, baseId, call, guardError, lastAttempt);

            if (!lastAttempt.Report.Success)
                return lastAttempt;

            bool satisfied;
            try
            {
                satisfied = EvaluateContinueCondition(
                    policy.Condition,
                    CreateReferenceRoot(lastAttempt),
                    baseId,
                    state,
                    out lastUnsatisfiedMessage);
            }
            catch (Exception ex)
            {
                return CreateConditionFailureContext(reportIndex, reportId, baseId, call, "script.invalid_condition", ex.Message, lastAttempt);
            }

            if (satisfied)
                return lastAttempt;

            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
            {
                var timeoutMessage = string.IsNullOrWhiteSpace(policy.TimeoutMessage)
                    ? $"Step '{baseId}' did not satisfy continueUntil within {timeoutMs} ms."
                    : policy.TimeoutMessage;
                if (!string.IsNullOrWhiteSpace(lastUnsatisfiedMessage))
                    timeoutMessage += " " + lastUnsatisfiedMessage;

                return CreateConditionFailureContext(reportIndex, reportId, baseId, call, "script.continue_timeout", timeoutMessage, lastAttempt);
            }

            if (pollIntervalMs > 0)
                Thread.Sleep(pollIntervalMs);
        }
    }

    private ExecutedStepContext InvokeStepOnce(
        string reportId,
        string baseId,
        string call,
        int reportIndex,
        Dictionary<string, object> arguments,
        bool includeStepResults,
        int attempts)
    {
        var envelope = _registry.Invoke(call, arguments);
        return new ExecutedStepContext
        {
            BaseId = baseId,
            Report = new CapabilityScriptStepReport
            {
                Index = reportIndex,
                Id = reportId,
                Call = call,
                CapabilityId = envelope.CapabilityId,
                OperationId = envelope.OperationId,
                Status = envelope.Status,
                Success = envelope.Success,
                Attempts = attempts,
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
            },
            RawResult = envelope.Result
        };
    }

    private static ExecutedStepContext CreateInvalidStepContext(
        int reportIndex,
        string reportId,
        string baseId,
        string call,
        string errorCode,
        string message)
    {
        return CreateFailedStepContext(reportIndex, reportId, baseId, call, errorCode, message);
    }

    private static ExecutedStepContext CreateFailedStepContext(
        int reportIndex,
        string reportId,
        string baseId,
        string call,
        OperationError error)
    {
        if (error == null)
            throw new ArgumentNullException(nameof(error));

        return CreateFailedStepContext(reportIndex, reportId, baseId, call, error.Code, error.Message, error.Details);
    }

    private static ExecutedStepContext CreateFailedStepContext(
        int reportIndex,
        string reportId,
        string baseId,
        string call,
        string errorCode,
        string message,
        object details = null)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        return new ExecutedStepContext
        {
            BaseId = baseId ?? string.Empty,
            Report = new CapabilityScriptStepReport
            {
                Index = reportIndex,
                Id = reportId,
                Call = call ?? string.Empty,
                Status = OperationStatus.Failed,
                Success = false,
                Attempts = 1,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = startedAtUtc,
                DurationMs = 0,
                Error = new OperationError
                {
                    Code = errorCode,
                    Message = message,
                    ExceptionType = typeof(InvalidOperationException).FullName ?? nameof(InvalidOperationException),
                    Details = details
                }
            },
            RawResult = null
        };
    }

    private static ExecutedStepContext CreateConditionFailureContext(
        int reportIndex,
        string reportId,
        string baseId,
        string call,
        OperationError error,
        ExecutedStepContext lastAttempt)
    {
        if (error == null)
            throw new ArgumentNullException(nameof(error));

        return CreateConditionFailureContext(reportIndex, reportId, baseId, call, error.Code, error.Message, lastAttempt, error.Details);
    }

    private static ExecutedStepContext CreateConditionFailureContext(
        int reportIndex,
        string reportId,
        string baseId,
        string call,
        string errorCode,
        string message,
        ExecutedStepContext lastAttempt,
        object details = null)
    {
        var startedAtUtc = lastAttempt?.Report.StartedAtUtc ?? DateTimeOffset.UtcNow;
        var completedAtUtc = DateTimeOffset.UtcNow;
        var attempts = lastAttempt?.Report.Attempts ?? 1;
        return new ExecutedStepContext
        {
            BaseId = baseId,
            Report = new CapabilityScriptStepReport
            {
                Index = reportIndex,
                Id = reportId,
                Call = call ?? string.Empty,
                CapabilityId = lastAttempt?.Report.CapabilityId ?? string.Empty,
                OperationId = lastAttempt?.Report.OperationId ?? string.Empty,
                Status = OperationStatus.Failed,
                Success = false,
                Attempts = attempts,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                DurationMs = (long)(completedAtUtc - startedAtUtc).TotalMilliseconds,
                Result = lastAttempt?.Report.Result,
                Error = new OperationError
                {
                    Code = errorCode,
                    Message = message,
                    ExceptionType = typeof(InvalidOperationException).FullName ?? nameof(InvalidOperationException),
                    Details = details
                },
                Warnings = lastAttempt?.Report.Warnings != null ? [.. lastAttempt.Report.Warnings] : []
            },
            RawResult = lastAttempt?.RawResult
        };
    }

    private static OperationError ValidateDefinition(CapabilityScriptDefinition definition)
    {
        if (definition.MaxDurationMs <= 0)
        {
            return CreateScriptError(
                "script.invalid_definition",
                "The script must declare a positive maxDurationMs value.",
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["maxDurationMs"] = definition.MaxDurationMs
                });
        }

        if (definition.MaxExecutedStatements <= 0)
        {
            return CreateScriptError(
                "script.invalid_definition",
                "The script must declare a positive maxExecutedStatements value.",
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["maxExecutedStatements"] = definition.MaxExecutedStatements
                });
        }

        if (definition.MaxControlDepth <= 0)
        {
            return CreateScriptError(
                "script.invalid_definition",
                "The script must declare a positive maxControlDepth value.",
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["maxControlDepth"] = definition.MaxControlDepth
                });
        }

        return null;
    }

    private static OperationError CreateScriptError(string code, string message, object details = null)
    {
        return new OperationError
        {
            Code = code,
            Message = message,
            ExceptionType = typeof(InvalidOperationException).FullName ?? nameof(InvalidOperationException),
            Details = details
        };
    }

    private static Dictionary<string, object> ResolveArguments(
        IDictionary<string, object> arguments,
        string currentStepId,
        ScriptExecutionState state)
    {
        var resolved = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in arguments)
            resolved[pair.Key] = ResolveValue(pair.Value, currentStepId, state);

        return resolved;
    }

    private static object ResolveValue(
        object value,
        string currentStepId,
        ScriptExecutionState state)
    {
        if (value is IDictionary<string, object> dictionary)
        {
            if (IsReferenceExpression(dictionary))
                return ResolveReference(dictionary, currentStepId, state);

            if (IsVariableExpression(dictionary))
                return ResolveVariable(dictionary, currentStepId, state);

            if (TryResolveArithmeticExpression(dictionary, currentStepId, state, out var arithmeticValue))
                return arithmeticValue;

            if (TryResolveLogicalExpression(dictionary, currentStepId, state, out var logicalValue))
                return logicalValue;

            var resolved = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in dictionary)
                resolved[pair.Key] = ResolveValue(pair.Value, currentStepId, state);

            return resolved;
        }

        if (value is IEnumerable<object> items && value is not string)
            return items.Select(item => ResolveValue(item, currentStepId, state)).ToList();

        return value;
    }

    private static bool IsReferenceExpression(IDictionary<string, object> dictionary)
    {
        return dictionary.ContainsKey("$ref");
    }

    private static bool IsVariableExpression(IDictionary<string, object> dictionary)
    {
        return dictionary.ContainsKey("$var");
    }

    private static bool TryResolveArithmeticExpression(
        IDictionary<string, object> expression,
        string currentStepId,
        ScriptExecutionState state,
        out object value)
    {
        value = null;

        if (expression.Count == 1 && expression.TryGetValue("$add", out var addValue))
        {
            value = NormalizeArithmeticResult(
                ResolveArithmeticOperands(addValue, currentStepId, state)
                    .Aggregate(0m, (current, next) => current + next));
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$multiply", out var multiplyValue))
        {
            value = NormalizeArithmeticResult(
                ResolveArithmeticOperands(multiplyValue, currentStepId, state)
                    .Aggregate(1m, (current, next) => current * next));
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$subtract", out var subtractValue))
        {
            var operands = ResolveArithmeticOperands(subtractValue, currentStepId, state).ToList();
            if (operands.Count != 2)
                throw new InvalidOperationException("Arithmetic expression '$subtract' requires exactly two operands.");

            value = NormalizeArithmeticResult(operands[0] - operands[1]);
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$divide", out var divideValue))
        {
            var operands = ResolveArithmeticOperands(divideValue, currentStepId, state).ToList();
            if (operands.Count != 2)
                throw new InvalidOperationException("Arithmetic expression '$divide' requires exactly two operands.");

            value = NormalizeArithmeticResult(operands[0] / operands[1]);
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$mod", out var modValue))
        {
            var operands = ResolveArithmeticOperands(modValue, currentStepId, state).ToList();
            if (operands.Count != 2)
                throw new InvalidOperationException("Arithmetic expression '$mod' requires exactly two operands.");

            value = NormalizeArithmeticResult(operands[0] % operands[1]);
            return true;
        }

        return false;
    }

    private static bool TryResolveLogicalExpression(
        IDictionary<string, object> expression,
        string currentStepId,
        ScriptExecutionState state,
        out object value)
    {
        value = null;

        if (expression.Count == 1 && expression.TryGetValue("$negate", out var negateValue))
        {
            var operand = ResolveValue(negateValue, currentStepId, state);
            if (!TryConvertToDecimal(operand, out var number))
                throw new InvalidOperationException("Logical expression '$negate' requires a numeric operand.");

            value = NormalizeArithmeticResult(-number);
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$not", out var notValue))
        {
            value = !IsTruthy(ResolveValue(notValue, currentStepId, state));
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$and", out var andValue))
        {
            value = ResolveLogicalOperands(andValue, currentStepId, state).All(IsTruthy);
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$or", out var orValue))
        {
            value = ResolveLogicalOperands(orValue, currentStepId, state).Any(IsTruthy);
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$equals", out var equalsValue))
        {
            var operands = ResolveComparisonOperands(equalsValue, currentStepId, state);
            value = ObjectEquals(operands.Left, operands.Right);
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$notEquals", out var notEqualsValue))
        {
            var operands = ResolveComparisonOperands(notEqualsValue, currentStepId, state);
            value = !ObjectEquals(operands.Left, operands.Right);
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$greaterThan", out var greaterThanValue))
        {
            var operands = ResolveComparisonOperands(greaterThanValue, currentStepId, state);
            if (!TryCompareNumbers(operands.Left, operands.Right, out var comparison))
                throw new InvalidOperationException("Logical expression '$greaterThan' requires numeric operands.");

            value = comparison > 0;
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$greaterThanOrEqual", out var greaterThanOrEqualValue))
        {
            var operands = ResolveComparisonOperands(greaterThanOrEqualValue, currentStepId, state);
            if (!TryCompareNumbers(operands.Left, operands.Right, out var comparison))
                throw new InvalidOperationException("Logical expression '$greaterThanOrEqual' requires numeric operands.");

            value = comparison >= 0;
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$lessThan", out var lessThanValue))
        {
            var operands = ResolveComparisonOperands(lessThanValue, currentStepId, state);
            if (!TryCompareNumbers(operands.Left, operands.Right, out var comparison))
                throw new InvalidOperationException("Logical expression '$lessThan' requires numeric operands.");

            value = comparison < 0;
            return true;
        }

        if (expression.Count == 1 && expression.TryGetValue("$lessThanOrEqual", out var lessThanOrEqualValue))
        {
            var operands = ResolveComparisonOperands(lessThanOrEqualValue, currentStepId, state);
            if (!TryCompareNumbers(operands.Left, operands.Right, out var comparison))
                throw new InvalidOperationException("Logical expression '$lessThanOrEqual' requires numeric operands.");

            value = comparison <= 0;
            return true;
        }

        return false;
    }

    private static IEnumerable<decimal> ResolveArithmeticOperands(object operandValue, string currentStepId, ScriptExecutionState state)
    {
        IEnumerable<object> rawOperands = operandValue switch
        {
            IEnumerable<object> typedItems when operandValue is not string => typedItems,
            IEnumerable enumerable when operandValue is not string => enumerable.Cast<object>().ToList(),
            _ => throw new InvalidOperationException("Arithmetic expressions require an array of operands.")
        };

        var resolved = rawOperands
            .Select(item => ResolveValue(item, currentStepId, state))
            .Select(item =>
            {
                if (!TryConvertToDecimal(item, out var number))
                    throw new InvalidOperationException("Arithmetic expressions require numeric operands.");

                return number;
            })
            .ToList();

        if (resolved.Count == 0)
            throw new InvalidOperationException("Arithmetic expressions require at least one operand.");

        return resolved;
    }

    private static IEnumerable<object> ResolveLogicalOperands(object operandValue, string currentStepId, ScriptExecutionState state)
    {
        IEnumerable<object> rawOperands = operandValue switch
        {
            IEnumerable<object> typedItems when operandValue is not string => typedItems,
            IEnumerable enumerable when operandValue is not string => enumerable.Cast<object>().ToList(),
            _ => throw new InvalidOperationException("Logical expressions require an array of operands.")
        };

        var resolved = rawOperands
            .Select(item => ResolveValue(item, currentStepId, state))
            .ToList();

        if (resolved.Count == 0)
            throw new InvalidOperationException("Logical expressions require at least one operand.");

        return resolved;
    }

    private static (object Left, object Right) ResolveComparisonOperands(object operandValue, string currentStepId, ScriptExecutionState state)
    {
        IEnumerable<object> rawOperands = operandValue switch
        {
            IEnumerable<object> typedItems when operandValue is not string => typedItems,
            IEnumerable enumerable when operandValue is not string => enumerable.Cast<object>().ToList(),
            _ => throw new InvalidOperationException("Comparison expressions require an array of exactly two operands.")
        };

        var resolved = rawOperands
            .Select(item => ResolveValue(item, currentStepId, state))
            .ToList();
        if (resolved.Count != 2)
            throw new InvalidOperationException("Comparison expressions require an array of exactly two operands.");

        return (resolved[0], resolved[1]);
    }

    private static object NormalizeArithmeticResult(decimal value)
    {
        if (value == decimal.Truncate(value))
        {
            if (value >= int.MinValue && value <= int.MaxValue)
                return decimal.ToInt32(value);

            if (value >= long.MinValue && value <= long.MaxValue)
                return decimal.ToInt64(value);
        }

        return value;
    }

    private static object ResolveReference(
        IDictionary<string, object> reference,
        string currentStepId,
        ScriptExecutionState state)
    {
        var referencedStepId = Convert.ToString(reference["$ref"], CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(referencedStepId))
            throw new InvalidOperationException($"Step '{currentStepId}' contains a script reference without a valid '$ref' target.");

        if (!state.ExecutedStepsByBaseId.TryGetValue(referencedStepId, out var executed))
            throw new InvalidOperationException($"Step '{currentStepId}' cannot reference step '{referencedStepId}' because it has not executed yet.");

        var required = true;
        if (reference.TryGetValue("required", out var requiredValue) && requiredValue != null)
            required = Convert.ToBoolean(requiredValue, CultureInfo.InvariantCulture);

        var path = reference.TryGetValue("path", out var pathValue)
            ? Convert.ToString(pathValue, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
            : "result";
        var root = CreateReferenceRoot(executed);

        if (TryResolvePath(root, path, out var resolved))
            return resolved;

        if (!required)
            return null;

        var suffix = string.IsNullOrWhiteSpace(path) ? string.Empty : $" path '{path}'";
        throw new InvalidOperationException($"Step '{currentStepId}' could not resolve script reference '{referencedStepId}'{suffix}.");
    }

    private static object ResolveVariable(
        IDictionary<string, object> reference,
        string currentStepId,
        ScriptExecutionState state)
    {
        var variableName = Convert.ToString(reference["$var"], CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(variableName))
            throw new InvalidOperationException($"Step '{currentStepId}' contains a variable expression without a valid '$var' name.");

        if (!state.TryGetVariable(variableName, out var value))
            throw new InvalidOperationException($"Step '{currentStepId}' cannot resolve variable '{variableName}' because it is not declared in any active scope.");

        var required = true;
        if (reference.TryGetValue("required", out var requiredValue) && requiredValue != null)
            required = Convert.ToBoolean(requiredValue, CultureInfo.InvariantCulture);

        var path = reference.TryGetValue("path", out var pathValue)
            ? Convert.ToString(pathValue, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return value;

        if (TryResolvePath(value, path, out var resolved))
            return resolved;

        if (!required)
            return null;

        throw new InvalidOperationException($"Step '{currentStepId}' could not resolve variable '{variableName}' path '{path}'.");
    }

    private static Dictionary<string, object> CreateReferenceRoot(ExecutedStepContext executed)
    {
        var report = executed.Report;
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["index"] = report.Index,
            ["id"] = report.Id,
            ["call"] = report.Call,
            ["capabilityId"] = report.CapabilityId,
            ["operationId"] = report.OperationId,
            ["status"] = report.Status.ToString(),
            ["success"] = report.Success,
            ["attempts"] = report.Attempts,
            ["startedAtUtc"] = report.StartedAtUtc,
            ["completedAtUtc"] = report.CompletedAtUtc,
            ["durationMs"] = report.DurationMs,
            ["result"] = executed.RawResult,
            ["error"] = report.Error,
            ["warnings"] = report.Warnings
        };
    }

    private static Dictionary<string, object> CreateConditionRoot(ScriptExecutionState state)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["vars"] = state.CreateVariableSnapshot()
        };
    }

    private static bool EvaluateContinueCondition(
        IDictionary<string, object> condition,
        object currentRoot,
        string currentStepId,
        ScriptExecutionState state,
        out string message)
    {
        message = string.Empty;
        if (condition == null || condition.Count == 0)
            throw new InvalidOperationException($"Step '{currentStepId}' contains an empty continue condition.");

        if (TryGetConditionList(condition, "all", out var allConditions))
        {
            foreach (var child in allConditions)
            {
                if (!EvaluateConditionNode(child, currentRoot, currentStepId, state, out message))
                    return false;
            }

            message = "All continue conditions were satisfied.";
            return true;
        }

        if (TryGetConditionList(condition, "any", out var anyConditions))
        {
            string lastChildMessage = string.Empty;
            foreach (var child in anyConditions)
            {
                if (EvaluateConditionNode(child, currentRoot, currentStepId, state, out var childMessage))
                {
                    message = string.IsNullOrWhiteSpace(childMessage) ? "At least one continue condition was satisfied." : childMessage;
                    return true;
                }

                lastChildMessage = childMessage;
            }

            message = string.IsNullOrWhiteSpace(lastChildMessage)
                ? "No continue condition in the 'any' group was satisfied."
                : lastChildMessage;
            return false;
        }

        return EvaluateLeafCondition(condition, currentRoot, currentStepId, state, out message);
    }

    private static bool EvaluateConditionNode(
        object node,
        object currentRoot,
        string currentStepId,
        ScriptExecutionState state,
        out string message)
    {
        if (node is not IDictionary<string, object> condition)
            throw new InvalidOperationException($"Step '{currentStepId}' contains a continue condition node that is not an object.");

        return EvaluateContinueCondition(condition, currentRoot, currentStepId, state, out message);
    }

    private static bool EvaluateLeafCondition(
        IDictionary<string, object> condition,
        object currentRoot,
        string currentStepId,
        ScriptExecutionState state,
        out string message)
    {
        object subject = currentRoot;
        var path = condition.TryGetValue("path", out var pathValue)
            ? Convert.ToString(ResolveValue(pathValue, currentStepId, state), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
            : string.Empty;
        var exists = string.IsNullOrWhiteSpace(path) || TryResolvePath(currentRoot, path, out subject);

        if (condition.TryGetValue("exists", out var existsValue))
        {
            var expectedExists = Convert.ToBoolean(ResolveValue(existsValue, currentStepId, state), CultureInfo.InvariantCulture);
            if (exists != expectedExists)
            {
                message = string.IsNullOrWhiteSpace(path)
                    ? $"Continue condition expected existence={expectedExists}, but the current value existence was {exists}."
                    : $"Continue condition expected path '{path}' existence to be {expectedExists}, but it was {exists}.";
                return false;
            }
        }
        else if (!exists)
        {
            message = string.IsNullOrWhiteSpace(path)
                ? "Continue condition could not resolve the current value."
                : $"Continue condition could not resolve path '{path}'.";
            return false;
        }

        if (condition.TryGetValue("allItems", out var allItemsValue))
        {
            if (!TryEnumerateCollection(subject, out var items))
            {
                message = $"Continue condition path '{path}' did not resolve to a collection for allItems.";
                return false;
            }

            var itemList = items.ToList();
            if (itemList.Count == 0)
            {
                message = $"Continue condition path '{path}' resolved to an empty collection for allItems.";
                return false;
            }

            foreach (var item in itemList)
            {
                if (!EvaluateConditionNode(allItemsValue, item, currentStepId, state, out message))
                    return false;
            }
        }

        if (condition.TryGetValue("anyItem", out var anyItemValue))
        {
            if (!TryEnumerateCollection(subject, out var items))
            {
                message = $"Continue condition path '{path}' did not resolve to a collection for anyItem.";
                return false;
            }

            string lastItemMessage = string.Empty;
            var matched = false;
            foreach (var item in items)
            {
                if (EvaluateConditionNode(anyItemValue, item, currentStepId, state, out var itemMessage))
                {
                    matched = true;
                    break;
                }

                lastItemMessage = itemMessage;
            }

            if (!matched)
            {
                message = string.IsNullOrWhiteSpace(lastItemMessage)
                    ? $"Continue condition path '{path}' did not contain any matching items."
                    : lastItemMessage;
                return false;
            }
        }

        if (condition.TryGetValue("countEquals", out var countEqualsValue))
        {
            if (!TryGetCount(subject, out var count))
            {
                message = $"Continue condition path '{path}' did not resolve to a countable value.";
                return false;
            }

            var expected = Convert.ToInt32(ResolveValue(countEqualsValue, currentStepId, state), CultureInfo.InvariantCulture);
            if (count != expected)
            {
                message = $"Continue condition expected count {expected} at path '{path}', but found {count}.";
                return false;
            }
        }

        if (condition.TryGetValue("equals", out var equalsValue)
            && !ObjectEquals(subject, ResolveValue(equalsValue, currentStepId, state)))
        {
            message = $"Continue condition expected path '{path}' to equal the requested value.";
            return false;
        }

        if (condition.TryGetValue("notEquals", out var notEqualsValue)
            && ObjectEquals(subject, ResolveValue(notEqualsValue, currentStepId, state)))
        {
            message = $"Continue condition expected path '{path}' to differ from the requested value.";
            return false;
        }

        if (condition.TryGetValue("in", out var inValue)
            && !CollectionContains(inValue, subject, currentStepId, state))
        {
            message = $"Continue condition expected path '{path}' to be in the requested set.";
            return false;
        }

        if (condition.TryGetValue("notIn", out var notInValue)
            && CollectionContains(notInValue, subject, currentStepId, state))
        {
            message = $"Continue condition expected path '{path}' not to be in the requested set.";
            return false;
        }

        if (condition.TryGetValue("greaterThan", out var greaterThanValue))
        {
            if (!TryCompareNumbers(subject, ResolveValue(greaterThanValue, currentStepId, state), out var greaterThan) || greaterThan <= 0)
            {
                message = $"Continue condition expected path '{path}' to be greater than the requested value.";
                return false;
            }
        }

        if (condition.TryGetValue("greaterThanOrEqual", out var greaterThanOrEqualValue))
        {
            if (!TryCompareNumbers(subject, ResolveValue(greaterThanOrEqualValue, currentStepId, state), out var greaterThanOrEqual) || greaterThanOrEqual < 0)
            {
                message = $"Continue condition expected path '{path}' to be greater than or equal to the requested value.";
                return false;
            }
        }

        if (condition.TryGetValue("lessThan", out var lessThanValue))
        {
            if (!TryCompareNumbers(subject, ResolveValue(lessThanValue, currentStepId, state), out var lessThan) || lessThan >= 0)
            {
                message = $"Continue condition expected path '{path}' to be less than the requested value.";
                return false;
            }
        }

        if (condition.TryGetValue("lessThanOrEqual", out var lessThanOrEqualValue))
        {
            if (!TryCompareNumbers(subject, ResolveValue(lessThanOrEqualValue, currentStepId, state), out var lessThanOrEqual) || lessThanOrEqual > 0)
            {
                message = $"Continue condition expected path '{path}' to be less than or equal to the requested value.";
                return false;
            }
        }

        message = string.IsNullOrWhiteSpace(path)
            ? "Continue condition satisfied."
            : $"Continue condition satisfied for path '{path}'.";
        return true;
    }

    private static bool TryGetConditionList(IDictionary<string, object> condition, string key, out List<object> items)
    {
        items = null;
        if (!condition.TryGetValue(key, out var raw))
            return false;

        items = raw switch
        {
            IEnumerable<object> objectEnumerable when raw is not string => objectEnumerable.ToList(),
            IEnumerable enumerable when raw is not string => enumerable.Cast<object>().ToList(),
            _ => throw new InvalidOperationException($"Continue condition key '{key}' must contain an array of condition objects.")
        };

        return true;
    }

    private static bool TryEnumerateCollection(object value, out IEnumerable<object> items)
    {
        items = null;
        if (value == null || value is string)
            return false;

        if (value is IEnumerable<object> typedItems)
        {
            items = typedItems;
            return true;
        }

        if (value is IEnumerable enumerable)
        {
            items = enumerable.Cast<object>().ToList();
            return true;
        }

        return false;
    }

    private static bool TryGetCount(object value, out int count)
    {
        count = 0;
        if (!TryEnumerateCollection(value, out var items))
            return false;

        count = items.Count();
        return true;
    }

    private static bool ObjectEquals(object left, object right)
    {
        if (TryCompareNumbers(left, right, out var numericComparison))
            return numericComparison == 0;

        if (left is string || right is string)
            return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return Equals(left, right);
    }

    private static bool IsTruthy(object value)
    {
        return value switch
        {
            null => false,
            bool boolValue => boolValue,
            _ => true
        };
    }

    private static bool CollectionContains(object collectionValue, object subject, string currentStepId, ScriptExecutionState state)
    {
        if (!TryEnumerateCollection(ResolveValue(collectionValue, currentStepId, state), out var items))
            throw new InvalidOperationException("Continue condition 'in'/'notIn' requires a collection value.");

        return items.Any(item => ObjectEquals(item, subject));
    }

    private static bool TryCompareNumbers(object left, object right, out int comparison)
    {
        comparison = 0;
        if (!TryConvertToDecimal(left, out var leftDecimal) || !TryConvertToDecimal(right, out var rightDecimal))
            return false;

        comparison = decimal.Compare(leftDecimal, rightDecimal);
        return true;
    }

    private static bool TryConvertToDecimal(object value, out decimal result)
    {
        result = 0m;
        if (value == null)
            return false;

        switch (value)
        {
            case byte byteValue:
                result = byteValue;
                return true;
            case sbyte sbyteValue:
                result = sbyteValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case ushort ushortValue:
                result = ushortValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case uint uintValue:
                result = uintValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case ulong ulongValue:
                result = ulongValue;
                return true;
            case float floatValue:
                result = (decimal)floatValue;
                return true;
            case double doubleValue:
                result = (decimal)doubleValue;
                return true;
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result):
                return true;
            default:
                return false;
        }
    }

    private static bool TryResolvePath(object root, string path, out object resolved)
    {
        resolved = root;
        if (string.IsNullOrWhiteSpace(path))
            return true;

        foreach (var segment in path.Split('.'))
        {
            if (!TryResolveSegment(resolved, segment, out resolved))
                return false;
        }

        return true;
    }

    private static bool TryResolveSegment(object current, string segment, out object resolved)
    {
        resolved = current;
        if (current == null || string.IsNullOrWhiteSpace(segment))
            return false;

        var remaining = segment;
        var bracketIndex = remaining.IndexOf('[');
        var memberName = bracketIndex >= 0 ? remaining.Substring(0, bracketIndex) : remaining;
        if (!string.IsNullOrWhiteSpace(memberName) && !TryGetMemberValue(resolved, memberName, out resolved))
            return false;

        var cursor = bracketIndex >= 0 ? bracketIndex : remaining.Length;
        while (cursor < remaining.Length)
        {
            if (remaining[cursor] != '[')
                return false;

            var closeIndex = remaining.IndexOf(']', cursor + 1);
            if (closeIndex <= cursor + 1)
                return false;
            var indexText = remaining.Substring(cursor + 1, closeIndex - cursor - 1);
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                return false;
            if (!TryGetIndexValue(resolved, index, out resolved))
                return false;

            cursor = closeIndex + 1;
        }

        return true;
    }

    private static bool TryGetMemberValue(object source, string memberName, out object value)
    {
        value = null;
        if (source == null)
            return false;

        if (source is IDictionary<string, object> dictionary)
        {
            if (dictionary.TryGetValue(memberName, out value))
                return true;

            var match = dictionary.FirstOrDefault(pair => string.Equals(pair.Key, memberName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Key))
            {
                value = match.Value;
                return true;
            }

            return false;
        }

        if (source is IDictionary legacyDictionary)
        {
            foreach (DictionaryEntry entry in legacyDictionary)
            {
                if (entry.Key is string key && string.Equals(key, memberName, StringComparison.OrdinalIgnoreCase))
                {
                    value = entry.Value;
                    return true;
                }
            }

            return false;
        }

        var property = source.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property == null)
            return false;

        value = property.GetValue(source);
        return true;
    }

    private static bool TryGetIndexValue(object source, int index, out object value)
    {
        value = null;
        if (index < 0 || source == null || source is string)
            return false;

        if (source is Array array)
        {
            if (index >= array.Length)
                return false;

            value = array.GetValue(index);
            return true;
        }

        if (source is IList list)
        {
            if (index >= list.Count)
                return false;

            value = list[index];
            return true;
        }

        if (source is IEnumerable enumerable)
        {
            var items = enumerable.Cast<object>().ToList();
            if (index >= items.Count)
                return false;

            value = items[index];
            return true;
        }

        return false;
    }

    private static string ResolveStepType(CapabilityScriptStep step)
    {
        if (step == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(step.Type))
            return step.Type.Trim().ToLowerInvariant();

        return !string.IsNullOrWhiteSpace(step.Call) ? "call" : string.Empty;
    }

    private static int CountStatements(IReadOnlyList<CapabilityScriptStep> steps)
    {
        if (steps == null)
            return 0;

        var count = 0;
        foreach (var step in steps)
        {
            if (step == null)
            {
                count++;
                continue;
            }

            count++;
            count += CountStatements(step.Body);
            count += CountStatements(step.ElseBody);
        }

        return count;
    }
}
