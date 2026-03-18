using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;
using RimBridgeServer.Contracts;

namespace RimBridgeServer.Core;

public sealed class LuaScriptCompileException : Exception
{
    public LuaScriptCompileException(
        string code,
        string message,
        int? line = null,
        int? column = null,
        object details = null,
        Exception innerException = null)
        : base(message, innerException)
    {
        Code = string.IsNullOrWhiteSpace(code) ? "lua.compile_error" : code.Trim();
        Line = line;
        Column = column;
        Details = details;
    }

    public string Code { get; }

    public int? Line { get; }

    public int? Column { get; }

    public object Details { get; }
}

public sealed class LuaScriptCompiler
{
    private const string DefaultScriptName = "lua-script";
    private const int DefaultWhileMaxIterations = 100;
    private const int DefaultLoopMaxIterations = 1000;
    private const string ParamsVariableName = "params";

    private static readonly HashSet<string> AllowedGlobalNames = ["rb", "ipairs", "print"];
    private static readonly Assembly MoonSharpAssembly = typeof(Script).Assembly;
    private static readonly Type SourceCodeType = GetRequiredType("MoonSharp.Interpreter.Debugging.SourceCode");
    private static readonly Type LoaderFastType = GetRequiredType("MoonSharp.Interpreter.Tree.Fast_Interface.Loader_Fast");
    private static readonly Type ChunkStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.ChunkStatement");
    private static readonly Type CompositeStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.CompositeStatement");
    private static readonly Type FunctionCallStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.FunctionCallStatement");
    private static readonly Type AssignmentStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.AssignmentStatement");
    private static readonly Type IfStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.IfStatement");
    private static readonly Type WhileStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.WhileStatement");
    private static readonly Type ForLoopStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.ForLoopStatement");
    private static readonly Type ForEachLoopStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.ForEachLoopStatement");
    private static readonly Type ReturnStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.ReturnStatement");
    private static readonly Type EmptyStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.EmptyStatement");
    private static readonly Type BreakStatementType = GetRequiredType("MoonSharp.Interpreter.Tree.Statements.BreakStatement");
    private static readonly Type AdjustmentExpressionType = GetRequiredType("MoonSharp.Interpreter.Tree.Expressions.AdjustmentExpression");
    private static readonly Type LiteralExpressionType = GetRequiredType("MoonSharp.Interpreter.Tree.Expressions.LiteralExpression");
    private static readonly Type SymbolRefExpressionType = GetRequiredType("MoonSharp.Interpreter.Tree.Expressions.SymbolRefExpression");
    private static readonly Type IndexExpressionType = GetRequiredType("MoonSharp.Interpreter.Tree.Expressions.IndexExpression");
    private static readonly Type TableConstructorType = GetRequiredType("MoonSharp.Interpreter.Tree.Expressions.TableConstructor");
    private static readonly Type BinaryOperatorExpressionType = GetRequiredType("MoonSharp.Interpreter.Tree.Expressions.BinaryOperatorExpression");
    private static readonly Type UnaryOperatorExpressionType = GetRequiredType("MoonSharp.Interpreter.Tree.Expressions.UnaryOperatorExpression");
    private static readonly Type FunctionCallExpressionType = GetRequiredType("MoonSharp.Interpreter.Tree.Expressions.FunctionCallExpression");
    private static readonly Type ExprListExpressionType = GetRequiredType("MoonSharp.Interpreter.Tree.Expressions.ExprListExpression");
    private static readonly MethodInfo CreateLoadingContextMethod = GetRequiredMethod(LoaderFastType, "CreateLoadingContext");
    private static readonly MethodInfo ExprListGetExpressionsMethod = GetRequiredMethod(ExprListExpressionType, "GetExpressions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public CapabilityScriptDefinition Compile(string luaSource, IDictionary<string, object> parameters = null)
    {
        if (string.IsNullOrWhiteSpace(luaSource))
            throw new LuaScriptCompileException("lua.invalid_source", "A non-empty luaSource payload is required.");

        var chunk = ParseChunk(luaSource);
        var state = new CompilerState(luaSource);
        state.DeclareReadOnly(ParamsVariableName);
        var definition = new CapabilityScriptDefinition
        {
            Name = DefaultScriptName
        };
        definition.Steps.Add(new CapabilityScriptStep
        {
            Type = "let",
            Name = ParamsVariableName,
            Value = NormalizeParameterValue(parameters)
        });

        var rootBlock = GetRequiredFieldValue(chunk, "m_Block");
        definition.Steps.AddRange(LowerCompositeStatement(rootBlock, state));
        return definition;
    }

    private static object ParseChunk(string luaSource)
    {
        try
        {
            var script = new Script(CoreModules.None);
            var sourceCode = Activator.CreateInstance(
                SourceCodeType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: [DefaultScriptName, luaSource, 0, script],
                culture: CultureInfo.InvariantCulture);
            if (sourceCode == null)
                throw new LuaScriptCompileException("lua.compile_error", "MoonSharp did not create a source-code object.");

            var loadingContext = CreateLoadingContextMethod.Invoke(null, [script, sourceCode]);
            if (loadingContext == null)
                throw new LuaScriptCompileException("lua.compile_error", "MoonSharp did not create a loading context.");

            return Activator.CreateInstance(
                       ChunkStatementType,
                       BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                       binder: null,
                       args: [loadingContext],
                       culture: CultureInfo.InvariantCulture)
                   ?? throw new LuaScriptCompileException("lua.compile_error", "MoonSharp did not create a chunk statement.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw CreateCompileException(ex.InnerException, null);
        }
        catch (LuaScriptCompileException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateCompileException(ex, null);
        }
    }

    private static List<CapabilityScriptStep> LowerCompositeStatement(object compositeStatement, CompilerState state)
    {
        if (compositeStatement == null)
            return [];

        if (!CompositeStatementType.IsInstanceOfType(compositeStatement))
            throw CreateCompileException(new InvalidOperationException("MoonSharp did not provide a composite statement."), compositeStatement);

        var lowered = new List<CapabilityScriptStep>();
        foreach (var statement in ReadObjectList(GetRequiredFieldValue(compositeStatement, "m_Statements")))
            lowered.AddRange(LowerStatement(statement, state));

        return lowered;
    }

    private static List<CapabilityScriptStep> LowerStatement(object statement, CompilerState state)
    {
        if (statement == null)
            return [];

        var type = statement.GetType();
        try
        {
            if (FunctionCallStatementType.IsAssignableFrom(type))
                return LowerFunctionCallStatement(statement, state);

            if (AssignmentStatementType.IsAssignableFrom(type))
                return LowerAssignmentStatement(statement, state);

            if (IfStatementType.IsAssignableFrom(type))
                return LowerIfStatement(statement, state);

            if (WhileStatementType.IsAssignableFrom(type))
                return LowerWhileStatement(statement, state);

            if (ForLoopStatementType.IsAssignableFrom(type))
                return LowerNumericForStatement(statement, state);

            if (ForEachLoopStatementType.IsAssignableFrom(type))
                return LowerForEachStatement(statement, state);

            if (ReturnStatementType.IsAssignableFrom(type))
                return LowerReturnStatement(statement, state);

            if (EmptyStatementType.IsAssignableFrom(type))
                return [];

            if (BreakStatementType.IsAssignableFrom(type))
                throw CreateUnsupportedStatementException(statement, "Lua 'break' is not supported in rimbridge/run_lua v1.");

            throw CreateUnsupportedStatementException(statement, $"Lua statement '{type.Name}' is not supported in rimbridge/run_lua v1.");
        }
        catch (LuaScriptCompileException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateCompileException(ex, statement);
        }
    }

    private static List<CapabilityScriptStep> LowerFunctionCallStatement(object statement, CompilerState state)
    {
        var callExpression = GetRequiredFieldValue(statement, "m_FunctionCallExpression");
        if (!TryParseHostCall(callExpression, state, out var hostCall))
            throw CreateUnsupportedExpressionException(callExpression, "Only supported rb.* calls and print/ipairs helpers can be used in rimbridge/run_lua v1.");

        return LowerStandaloneHostCall(hostCall, state, statement);
    }

    private static List<CapabilityScriptStep> LowerAssignmentStatement(object statement, CompilerState state)
    {
        var leftValues = ReadObjectList(GetRequiredFieldValue(statement, "m_LValues"));
        var rightValues = ReadObjectList(GetRequiredFieldValue(statement, "m_RValues"));

        if (leftValues.Count != 1 || rightValues.Count > 1)
            throw CreateUnsupportedStatementException(statement, "Multiple-assignment Lua statements are not supported in rimbridge/run_lua v1.");

        var target = leftValues[0];
        if (!SymbolRefExpressionType.IsInstanceOfType(target))
            throw CreateUnsupportedStatementException(statement, "Table-field assignment is not supported in rimbridge/run_lua v1.");

        var variableName = GetSymbolExpressionName(target);
        var symbol = ReadSymbolRef(target);
        var alreadyDeclared = state.IsDeclared(variableName);
        if (state.IsReadOnly(variableName))
            throw CreateUnsupportedStatementException(statement, $"Lua binding '{variableName}' is read-only in rimbridge/run_lua v1.");

        if (!alreadyDeclared && symbol.Type == SymbolRefType.Global)
            throw CreateUnsupportedStatementException(statement, $"Global assignment to '{variableName}' is not supported in rimbridge/run_lua v1.");

        var declareVariable = IsLocalAssignmentStatement(statement, state);
        if (rightValues.Count == 0)
        {
            var declarationStep = new CapabilityScriptStep
            {
                Type = declareVariable ? "let" : "set",
                Name = variableName,
                Value = null
            };
            if (declareVariable)
                state.Declare(variableName);

            return [declarationStep];
        }

        var rightValue = rightValues[0];
        if (TryParseHostCall(rightValue, state, out var hostCall))
        {
            if (hostCall.Kind is not HostCallKind.Call and not HostCallKind.Poll)
                throw CreateUnsupportedStatementException(statement, $"Lua '{hostCall.DisplayName}' cannot be assigned to a variable.");

            var callId = state.CreateCallId(hostCall.Alias, variableName);
            var steps = LowerCallHostCall(hostCall, callId);
            steps.Add(new CapabilityScriptStep
            {
                Type = declareVariable ? "let" : "set",
                Name = variableName,
                Value = StepReference(callId, string.Empty)
            });

            if (declareVariable)
                state.Declare(variableName);

            return steps;
        }

        var compiledValue = CompilePureExpression(rightValue, state);
        if (declareVariable)
            state.Declare(variableName);

        return
        [
            new CapabilityScriptStep
            {
                Type = declareVariable ? "let" : "set",
                Name = variableName,
                Value = compiledValue
            }
        ];
    }

    private static List<CapabilityScriptStep> LowerIfStatement(object statement, CompilerState state)
    {
        var ifBlocks = ReadObjectList(GetRequiredFieldValue(statement, "m_Ifs"));
        if (ifBlocks.Count == 0)
            throw CreateUnsupportedStatementException(statement, "Lua 'if' did not contain any branches.");

        var elseBlock = GetFieldValue(statement, "m_Else");
        return LowerIfBlocks(ifBlocks, 0, elseBlock, state);
    }

    private static List<CapabilityScriptStep> LowerIfBlocks(IReadOnlyList<object> ifBlocks, int index, object elseBlock, CompilerState state)
    {
        var current = ifBlocks[index];
        var conditionExpression = GetRequiredFieldValue(current, "Exp");
        var conditionVariable = state.CreateTemp("if_condition");
        state.Declare(conditionVariable);

        var body = CompileScopedBlock(GetRequiredFieldValue(current, "Block"), state);
        var elseBody = index + 1 < ifBlocks.Count
            ? LowerIfBlocks(ifBlocks, index + 1, elseBlock, state)
            : elseBlock != null
                ? CompileScopedBlock(GetRequiredFieldValue(elseBlock, "Block"), state)
                : [];

        return
        [
            new CapabilityScriptStep
            {
                Type = "let",
                Name = conditionVariable,
                Value = CompilePureExpression(conditionExpression, state)
            },
            new CapabilityScriptStep
            {
                Id = state.CreateGeneratedId("if"),
                Type = "if",
                Condition = TruthyCondition(conditionVariable),
                Body = body,
                ElseBody = elseBody
            }
        ];
    }

    private static List<CapabilityScriptStep> LowerWhileStatement(object statement, CompilerState state)
    {
        var conditionExpression = GetRequiredFieldValue(statement, "m_Condition");
        var conditionVariable = state.CreateTemp("while_condition");
        state.Declare(conditionVariable);

        var bodyBlock = GetRequiredFieldValue(statement, "m_Block");
        var body = CompileScopedBlock(bodyBlock, state);
        body.Add(new CapabilityScriptStep
        {
            Type = "set",
            Name = conditionVariable,
            Value = CompilePureExpression(conditionExpression, state)
        });

        return
        [
            new CapabilityScriptStep
            {
                Type = "let",
                Name = conditionVariable,
                Value = CompilePureExpression(conditionExpression, state)
            },
            new CapabilityScriptStep
            {
                Id = state.CreateGeneratedId("while"),
                Type = "while",
                MaxIterations = DefaultWhileMaxIterations,
                Condition = TruthyCondition(conditionVariable),
                Body = body
            }
        ];
    }

    private static List<CapabilityScriptStep> LowerNumericForStatement(object statement, CompilerState state)
    {
        var loopVariableName = ReadSymbolName(GetRequiredFieldValue(statement, "m_VarName"));
        ValidateWritableBindingName(loopVariableName, statement, state);
        var startVariable = state.CreateTemp("for_start");
        var endVariable = state.CreateTemp("for_end");
        var stepVariable = state.CreateTemp("for_step");
        var currentVariable = state.CreateTemp("for_current");
        state.Declare(startVariable);
        state.Declare(endVariable);
        state.Declare(stepVariable);
        state.Declare(currentVariable);

        var startValue = CompilePureExpression(GetRequiredFieldValue(statement, "m_Start"), state);
        var endValue = CompilePureExpression(GetRequiredFieldValue(statement, "m_End"), state);
        var stepValue = CompilePureExpression(GetRequiredFieldValue(statement, "m_Step"), state);

        var originalBody = GetRequiredFieldValue(statement, "m_InnerBlock");
        var positiveBody = BuildNumericForBody(originalBody, state, loopVariableName, currentVariable, stepVariable);
        var negativeBody = BuildNumericForBody(originalBody, state, loopVariableName, currentVariable, stepVariable);

        return
        [
            new CapabilityScriptStep
            {
                Type = "let",
                Name = startVariable,
                Value = startValue
            },
            new CapabilityScriptStep
            {
                Type = "let",
                Name = endVariable,
                Value = endValue
            },
            new CapabilityScriptStep
            {
                Type = "let",
                Name = stepVariable,
                Value = stepValue
            },
            new CapabilityScriptStep
            {
                Type = "let",
                Name = currentVariable,
                Value = VariableReference(startVariable)
            },
            new CapabilityScriptStep
            {
                Id = state.CreateGeneratedId("for"),
                Type = "if",
                Condition = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["path"] = "vars." + stepVariable,
                    ["greaterThanOrEqual"] = 0
                },
                Body =
                [
                    new CapabilityScriptStep
                    {
                        Id = state.CreateGeneratedId("for_positive"),
                        Type = "while",
                        MaxIterations = DefaultLoopMaxIterations,
                        Condition = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["path"] = "vars." + currentVariable,
                            ["lessThanOrEqual"] = VariableReference(endVariable)
                        },
                        Body = positiveBody
                    }
                ],
                ElseBody =
                [
                    new CapabilityScriptStep
                    {
                        Id = state.CreateGeneratedId("for_negative"),
                        Type = "while",
                        MaxIterations = DefaultLoopMaxIterations,
                        Condition = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["path"] = "vars." + currentVariable,
                            ["greaterThanOrEqual"] = VariableReference(endVariable)
                        },
                        Body = negativeBody
                    }
                ]
            }
        ];
    }

    private static List<CapabilityScriptStep> BuildNumericForBody(object originalBody, CompilerState state, string loopVariableName, string currentVariable, string stepVariable)
    {
        var body = new List<CapabilityScriptStep>
        {
            new()
            {
                Type = "let",
                Name = loopVariableName,
                Value = VariableReference(currentVariable)
            }
        };

        state.PushScope();
        try
        {
            state.Declare(loopVariableName);
            body.AddRange(LowerCompositeStatement(originalBody, state));
        }
        finally
        {
            state.PopScope();
        }

        body.Add(new CapabilityScriptStep
        {
            Type = "set",
            Name = currentVariable,
            Value = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["$add"] = new List<object>
                {
                    VariableReference(currentVariable),
                    VariableReference(stepVariable)
                }
            }
        });

        return body;
    }

    private static List<CapabilityScriptStep> LowerForEachStatement(object statement, CompilerState state)
    {
        var names = ReadObjectList(GetRequiredFieldValue(statement, "m_Names"))
            .Select(ReadSymbolName)
            .ToList();
        if (names.Count == 0 || names.Count > 2)
            throw CreateUnsupportedStatementException(statement, "Lua ipairs loops in rimbridge/run_lua v1 support one or two loop variables.");

        foreach (var name in names)
            ValidateWritableBindingName(name, statement, state);

        var valuesExpression = GetRequiredFieldValue(statement, "m_RValues");
        var expressions = ReadExpressionArray(valuesExpression);
        if (expressions.Count != 1 || !TryParseHostCall(expressions[0], state, out var hostCall) || hostCall.Kind != HostCallKind.Ipairs)
            throw CreateUnsupportedStatementException(statement, "Only 'for ... in ipairs(...) do' is supported in rimbridge/run_lua v1.");

        var collection = hostCall.CollectionExpression
                         ?? throw CreateUnsupportedStatementException(statement, "ipairs must receive a collection expression.");
        var internalItemName = state.CreateTemp("foreach_item");
        var internalIndexName = state.CreateTemp("foreach_index");

        var body = new List<CapabilityScriptStep>();
        if (names.Count >= 1)
        {
            body.Add(new CapabilityScriptStep
            {
                Type = "let",
                Name = names[0],
                Value = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["$add"] = new List<object>
                    {
                        VariableReference(internalIndexName),
                        1
                    }
                }
            });
        }

        if (names.Count == 2)
        {
            body.Add(new CapabilityScriptStep
            {
                Type = "let",
                Name = names[1],
                Value = VariableReference(internalItemName)
            });
        }

        state.PushScope();
        try
        {
            foreach (var name in names)
                state.Declare(name);

            body.AddRange(LowerCompositeStatement(GetRequiredFieldValue(statement, "m_Block"), state));
        }
        finally
        {
            state.PopScope();
        }

        return
        [
            new CapabilityScriptStep
            {
                Id = state.CreateGeneratedId("foreach"),
                Type = "foreach",
                ItemName = internalItemName,
                IndexName = internalIndexName,
                Collection = CompilePureExpression(collection, state),
                Body = body
            }
        ];
    }

    private static List<CapabilityScriptStep> LowerReturnStatement(object statement, CompilerState state)
    {
        var expression = GetFieldValue(statement, "m_Expression");
        if (expression == null)
        {
            return
            [
                new CapabilityScriptStep
                {
                    Type = "return"
                }
            ];
        }

        var expressions = ReadExpressionArray(expression);
        if (expressions.Count == 1 && TryParseHostCall(expressions[0], state, out var hostCall))
        {
            if (hostCall.Kind is not HostCallKind.Call and not HostCallKind.Poll)
                throw CreateUnsupportedStatementException(statement, $"Lua '{hostCall.DisplayName}' cannot be returned as a value.");

            var callId = state.CreateCallId(hostCall.Alias, "return");
            var steps = LowerCallHostCall(hostCall, callId);
            steps.Add(new CapabilityScriptStep
            {
                Type = "return",
                Value = StepReference(callId, string.Empty)
            });
            return steps;
        }

        return
        [
            new CapabilityScriptStep
            {
                Type = "return",
                Value = expressions.Count == 1
                    ? CompilePureExpression(expressions[0], state)
                    : expressions.Select(expr => CompilePureExpression(expr, state)).ToList()
            }
        ];
    }

    private static List<CapabilityScriptStep> LowerStandaloneHostCall(HostCall hostCall, CompilerState state, object statement)
    {
        return hostCall.Kind switch
        {
            HostCallKind.Call => LowerCallHostCall(hostCall, state.CreateCallId(hostCall.Alias, "call")),
            HostCallKind.Poll => LowerCallHostCall(hostCall, state.CreateCallId(hostCall.Alias, "poll")),
            HostCallKind.Print => LowerPrintHostCall(hostCall),
            HostCallKind.Assert => LowerAssertHostCall(hostCall, state),
            HostCallKind.Fail => LowerFailHostCall(hostCall),
            _ => throw CreateUnsupportedStatementException(statement, $"Lua '{hostCall.DisplayName}' is not supported in rimbridge/run_lua v1.")
        };
    }

    private static List<CapabilityScriptStep> LowerCallHostCall(HostCall hostCall, string stepId)
    {
        return
        [
            new CapabilityScriptStep
            {
                Id = stepId,
                Call = hostCall.Alias,
                Arguments = hostCall.Arguments ?? [],
                ContinueUntil = hostCall.Policy
            }
        ];
    }

    private static List<CapabilityScriptStep> LowerPrintHostCall(HostCall hostCall)
    {
        return
        [
            new CapabilityScriptStep
            {
                Type = "print",
                Message = hostCall.Message ?? string.Empty,
                Value = hostCall.Value
            }
        ];
    }

    private static List<CapabilityScriptStep> LowerAssertHostCall(HostCall hostCall, CompilerState state)
    {
        var conditionVariable = state.CreateTemp("assert_condition");
        state.Declare(conditionVariable);
        return
        [
            new CapabilityScriptStep
            {
                Type = "let",
                Name = conditionVariable,
                Value = hostCall.Value
            },
            new CapabilityScriptStep
            {
                Type = "assert",
                Message = hostCall.Message ?? string.Empty,
                Condition = TruthyCondition(conditionVariable)
            }
        ];
    }

    private static List<CapabilityScriptStep> LowerFailHostCall(HostCall hostCall)
    {
        return
        [
            new CapabilityScriptStep
            {
                Type = "fail",
                Message = hostCall.Message ?? string.Empty,
                Value = hostCall.Value
            }
        ];
    }

    private static object CompilePureExpression(object expression, CompilerState state)
    {
        expression = UnwrapAdjustmentExpression(expression);
        if (expression == null)
            return null;

        if (LiteralExpressionType.IsInstanceOfType(expression))
            return ReadLiteralValue(expression);

        if (SymbolRefExpressionType.IsInstanceOfType(expression))
        {
            var name = GetSymbolExpressionName(expression);
            if (!state.IsDeclared(name))
                throw CreateUnsupportedExpressionException(expression, $"Lua global '{name}' cannot be used as a runtime value in rimbridge/run_lua v1.");

            return VariableReference(name);
        }

        if (TryCompilePathExpression(expression, state, out var pathReference))
            return pathReference;

        if (TableConstructorType.IsInstanceOfType(expression))
            return CompileTableConstructor(expression, state);

        if (BinaryOperatorExpressionType.IsInstanceOfType(expression))
            return CompileBinaryExpression(expression, state);

        if (UnaryOperatorExpressionType.IsInstanceOfType(expression))
            return CompileUnaryExpression(expression, state);

        if (FunctionCallExpressionType.IsInstanceOfType(expression))
        {
            if (TryParseHostCall(expression, state, out var hostCall) && hostCall.Kind == HostCallKind.Ipairs)
                throw CreateUnsupportedExpressionException(expression, "ipairs can only be used as the iterator in a Lua 'for ... in ipairs(...) do' loop.");

            throw CreateUnsupportedExpressionException(expression, "Host capability calls must appear as standalone statements or as the sole right-hand side of an assignment or return.");
        }

        if (ExprListExpressionType.IsInstanceOfType(expression))
        {
            var expressions = ReadExpressionArray(expression);
            if (expressions.Count == 1)
                return CompilePureExpression(expressions[0], state);

            return expressions.Select(expr => CompilePureExpression(expr, state)).ToList();
        }

        throw CreateUnsupportedExpressionException(expression, $"Lua expression '{expression.GetType().Name}' is not supported in rimbridge/run_lua v1.");
    }

    private static object CompileBinaryExpression(object expression, CompilerState state)
    {
        var operatorName = Convert.ToString(GetRequiredFieldValue(expression, "m_Operator"), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        var left = CompilePureExpression(GetRequiredFieldValue(expression, "m_Exp1"), state);
        var right = CompilePureExpression(GetRequiredFieldValue(expression, "m_Exp2"), state);

        return operatorName switch
        {
            "Add" => OperatorExpression("$add", [left, right]),
            "Sub" => OperatorExpression("$subtract", [left, right]),
            "Mul" => OperatorExpression("$multiply", [left, right]),
            "Div" => OperatorExpression("$divide", [left, right]),
            "Mod" => OperatorExpression("$mod", [left, right]),
            "Equal" => OperatorExpression("$equals", [left, right]),
            "NotEqual" => OperatorExpression("$notEquals", [left, right]),
            "Less" => OperatorExpression("$lessThan", [left, right]),
            "LessOrEqual" => OperatorExpression("$lessThanOrEqual", [left, right]),
            "Greater" => OperatorExpression("$greaterThan", [left, right]),
            "GreaterOrEqual" => OperatorExpression("$greaterThanOrEqual", [left, right]),
            "And" => OperatorExpression("$and", [left, right]),
            "Or" => OperatorExpression("$or", [left, right]),
            _ => throw CreateUnsupportedExpressionException(expression, $"Lua binary operator '{operatorName}' is not supported in rimbridge/run_lua v1.")
        };
    }

    private static object CompileUnaryExpression(object expression, CompilerState state)
    {
        var operatorText = Convert.ToString(GetRequiredFieldValue(expression, "m_OpText"), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        var operand = CompilePureExpression(GetRequiredFieldValue(expression, "m_Exp"), state);
        return operatorText switch
        {
            "not" => new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["$not"] = operand
            },
            "-" => new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["$negate"] = operand
            },
            _ => throw CreateUnsupportedExpressionException(expression, $"Lua unary operator '{operatorText}' is not supported in rimbridge/run_lua v1.")
        };
    }

    private static object CompileTableConstructor(object tableConstructor, CompilerState state)
    {
        var positionalValues = ReadObjectList(GetRequiredFieldValue(tableConstructor, "m_PositionalValues"));
        var keyedValues = ReadObjectList(GetRequiredFieldValue(tableConstructor, "m_CtorArgs"));
        if (positionalValues.Count > 0 && keyedValues.Count > 0)
            throw CreateUnsupportedExpressionException(tableConstructor, "Mixed array/object Lua tables are not supported in rimbridge/run_lua v1.");

        if (keyedValues.Count == 0)
            return positionalValues.Select(value => CompilePureExpression(value, state)).ToList();

        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var entry in keyedValues)
        {
            var key = GetPropertyValue(entry, "Key");
            var value = GetPropertyValue(entry, "Value");
            var keyText = ConvertTableKey(key);
            dictionary[keyText] = CompilePureExpression(value, state);
        }

        return dictionary;
    }

    private static string ConvertTableKey(object keyExpression)
    {
        keyExpression = UnwrapAdjustmentExpression(keyExpression);
        if (keyExpression != null && LiteralExpressionType.IsInstanceOfType(keyExpression))
        {
            var keyValue = ReadLiteralValue(keyExpression);
            return keyValue switch
            {
                string text => text,
                int intValue => intValue.ToString(CultureInfo.InvariantCulture),
                long longValue => longValue.ToString(CultureInfo.InvariantCulture),
                double doubleValue => doubleValue.ToString(CultureInfo.InvariantCulture),
                decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
                bool boolValue => boolValue ? "true" : "false",
                _ => throw CreateUnsupportedExpressionException(keyExpression, "Lua table keys must be string, numeric, or boolean literals in rimbridge/run_lua v1.")
            };
        }

        throw CreateUnsupportedExpressionException(keyExpression, "Dynamic Lua table keys are not supported in rimbridge/run_lua v1.");
    }

    private static bool TryCompilePathExpression(object expression, CompilerState state, out Dictionary<string, object> reference)
    {
        reference = null;
        expression = UnwrapAdjustmentExpression(expression);
        var segments = new Stack<string>();
        var current = expression;
        while (current != null && IndexExpressionType.IsInstanceOfType(current))
        {
            var memberName = Convert.ToString(GetFieldValue(current, "m_Name"), CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(memberName))
            {
                segments.Push("." + memberName.Trim());
                current = GetRequiredFieldValue(current, "m_BaseExp");
                continue;
            }

            var indexExpression = GetRequiredFieldValue(current, "m_IndexExp");
            if (!TryGetStaticLuaIndex(indexExpression, out var zeroBasedIndex))
                return false;

            segments.Push("[" + zeroBasedIndex.ToString(CultureInfo.InvariantCulture) + "]");
            current = GetRequiredFieldValue(current, "m_BaseExp");
        }

        current = UnwrapAdjustmentExpression(current);
        if (current == null || !SymbolRefExpressionType.IsInstanceOfType(current))
            return false;

        var rootName = GetSymbolExpressionName(current);
        if (!state.IsDeclared(rootName))
            return false;

        var path = string.Concat(segments);
        var required = rootName != ParamsVariableName || string.IsNullOrWhiteSpace(path);
        reference = VariableReference(rootName, path, required);
        return true;
    }

    private static bool TryGetStaticLuaIndex(object expression, out int zeroBasedIndex)
    {
        zeroBasedIndex = 0;
        expression = UnwrapAdjustmentExpression(expression);
        if (expression == null || !LiteralExpressionType.IsInstanceOfType(expression))
            return false;

        var literal = ReadLiteralValue(expression);
        if (!TryConvertToIntegralLiteral(literal, out var luaIndex) || luaIndex <= 0)
            return false;

        if (luaIndex > int.MaxValue)
            return false;

        zeroBasedIndex = (int)luaIndex - 1;
        return true;
    }

    private static bool TryConvertToIntegralLiteral(object value, out long result)
    {
        result = 0;
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
            case long longValue:
                result = longValue;
                return true;
            case decimal decimalValue when decimalValue == decimal.Truncate(decimalValue) && decimalValue >= long.MinValue && decimalValue <= long.MaxValue:
                result = decimal.ToInt64(decimalValue);
                return true;
            case double doubleValue when Math.Abs(doubleValue % 1d) < double.Epsilon && doubleValue >= long.MinValue && doubleValue <= long.MaxValue:
                result = (long)doubleValue;
                return true;
            default:
                return false;
        }
    }

    private static object ReadLiteralValue(object literalExpression)
    {
        var dynValue = GetPropertyValue(literalExpression, "Value") as DynValue
                       ?? throw CreateUnsupportedExpressionException(literalExpression, "MoonSharp did not expose the literal value.");

        return dynValue.Type switch
        {
            DataType.Nil or DataType.Void => null,
            DataType.Boolean => dynValue.Boolean,
            DataType.String => dynValue.String,
            DataType.Number => NormalizeNumericLiteral(dynValue.Number),
            _ => throw CreateUnsupportedExpressionException(literalExpression, $"Literal type '{dynValue.Type}' is not supported in rimbridge/run_lua v1.")
        };
    }

    private static object NormalizeNumericLiteral(double value)
    {
        if (Math.Abs(value % 1d) < double.Epsilon)
        {
            if (value >= int.MinValue && value <= int.MaxValue)
                return (int)value;

            if (value >= long.MinValue && value <= long.MaxValue)
                return (long)value;
        }

        return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private static bool TryParseHostCall(object expression, CompilerState state, out HostCall hostCall)
    {
        hostCall = default;
        expression = UnwrapAdjustmentExpression(expression);
        if (expression == null || !FunctionCallExpressionType.IsInstanceOfType(expression))
            return false;

        var thisCallName = Convert.ToString(GetFieldValue(expression, "m_Name"), CultureInfo.InvariantCulture)?.Trim();
        var functionExpression = GetRequiredFieldValue(expression, "m_Function");
        var arguments = ReadObjectList(GetRequiredFieldValue(expression, "m_Arguments"));

        string namespaceName = null;
        string memberName = null;

        if (!string.IsNullOrWhiteSpace(thisCallName))
        {
            namespaceName = TryReadAllowedGlobalName(functionExpression);
            memberName = thisCallName;
        }
        else if (TryResolveNamespacedFunction(functionExpression, out namespaceName, out memberName))
        {
        }
        else if (TryReadAllowedGlobalName(functionExpression) is { } globalName)
        {
            namespaceName = globalName;
        }
        else
        {
            return false;
        }

        if (string.Equals(namespaceName, "print", StringComparison.Ordinal))
        {
            hostCall = ParsePrintCall(arguments, state, expression);
            return true;
        }

        if (string.Equals(namespaceName, "ipairs", StringComparison.Ordinal))
        {
            hostCall = ParseIpairsCall(arguments, state, expression);
            return true;
        }

        if (!string.Equals(namespaceName, "rb", StringComparison.Ordinal))
            return false;

        hostCall = memberName switch
        {
            "call" => ParseCallLike(arguments, state, expression, poll: false),
            "poll" => ParseCallLike(arguments, state, expression, poll: true),
            "print" => ParsePrintCall(arguments, state, expression),
            "assert" => ParseAssertCall(arguments, state, expression),
            "fail" => ParseFailCall(arguments, state, expression),
            _ => throw CreateUnsupportedExpressionException(expression, $"rb.{memberName} is not supported in rimbridge/run_lua v1.")
        };

        return true;
    }

    private static HostCall ParseCallLike(IReadOnlyList<object> arguments, CompilerState state, object expression, bool poll)
    {
        if (arguments.Count == 0)
            throw CreateUnsupportedExpressionException(expression, $"rb.{(poll ? "poll" : "call")} requires a capability alias string.");

        var alias = ReadRequiredLiteralString(arguments[0], expression, $"rb.{(poll ? "poll" : "call")} requires a literal capability alias string.");
        Dictionary<string, object> compiledArguments = [];
        if (arguments.Count >= 2)
            compiledArguments = CompileObjectArgument(arguments[1], state, expression, $"rb.{(poll ? "poll" : "call")} arguments must be a table or nil.");

        CapabilityScriptContinuePolicy policy = null;
        if (poll)
        {
            if (arguments.Count < 3)
                throw CreateUnsupportedExpressionException(expression, "rb.poll requires a policy table as its third argument.");

            policy = CompileContinuePolicy(arguments[2], state, expression);
        }
        else if (arguments.Count > 2)
        {
            throw CreateUnsupportedExpressionException(expression, "rb.call accepts at most two arguments in rimbridge/run_lua v1.");
        }

        return new HostCall(
            poll ? HostCallKind.Poll : HostCallKind.Call,
            displayName: poll ? "rb.poll" : "rb.call",
            alias: alias,
            arguments: compiledArguments,
            policy: policy);
    }

    private static HostCall ParsePrintCall(IReadOnlyList<object> arguments, CompilerState state, object expression)
    {
        if (arguments.Count == 0)
            return new HostCall(HostCallKind.Print, "print");

        if (arguments.Count > 2)
            throw CreateUnsupportedExpressionException(expression, "print/rb.print accepts at most two arguments in rimbridge/run_lua v1.");

        if (arguments.Count == 1)
        {
            if (TryReadLiteralString(arguments[0], out var message))
                return new HostCall(HostCallKind.Print, "print", message: message);

            return new HostCall(HostCallKind.Print, "print", value: CompilePureExpression(arguments[0], state));
        }

        return new HostCall(
            HostCallKind.Print,
            "print",
            message: ReadRequiredLiteralString(arguments[0], expression, "print/rb.print requires a literal string as its first argument when two arguments are provided."),
            value: CompilePureExpression(arguments[1], state));
    }

    private static HostCall ParseAssertCall(IReadOnlyList<object> arguments, CompilerState state, object expression)
    {
        if (arguments.Count == 0 || arguments.Count > 2)
            throw CreateUnsupportedExpressionException(expression, "rb.assert requires one condition expression and an optional literal message.");

        var compiledCondition = CompilePureExpression(arguments[0], state);
        var message = arguments.Count == 2
            ? ReadRequiredLiteralString(arguments[1], expression, "rb.assert messages must be literal strings in rimbridge/run_lua v1.")
            : string.Empty;
        return new HostCall(HostCallKind.Assert, "rb.assert", message: message, value: compiledCondition);
    }

    private static HostCall ParseFailCall(IReadOnlyList<object> arguments, CompilerState state, object expression)
    {
        if (arguments.Count == 0 || arguments.Count > 2)
            throw CreateUnsupportedExpressionException(expression, "rb.fail requires a literal message and an optional detail value.");

        var message = ReadRequiredLiteralString(arguments[0], expression, "rb.fail requires a literal string message.");
        var detailValue = arguments.Count == 2 ? CompilePureExpression(arguments[1], state) : null;
        return new HostCall(HostCallKind.Fail, "rb.fail", message: message, value: detailValue);
    }

    private static HostCall ParseIpairsCall(IReadOnlyList<object> arguments, CompilerState state, object expression)
    {
        if (arguments.Count != 1)
            throw CreateUnsupportedExpressionException(expression, "ipairs requires exactly one collection expression.");

        return new HostCall(
            HostCallKind.Ipairs,
            "ipairs",
            collectionExpression: arguments[0]);
    }

    private static CapabilityScriptContinuePolicy CompileContinuePolicy(object expression, CompilerState state, object owner)
    {
        var table = CompilePureExpression(expression, state) as Dictionary<string, object>
                    ?? throw CreateUnsupportedExpressionException(owner, "rb.poll policy arguments must be tables.");

        var condition = table.TryGetValue("condition", out var conditionValue)
            ? conditionValue as Dictionary<string, object>
            : null;
        if (condition == null || condition.Count == 0)
            throw CreateUnsupportedExpressionException(owner, "rb.poll policy tables must include a non-empty condition object.");

        return new CapabilityScriptContinuePolicy
        {
            TimeoutMs = ReadOptionalInt(table, "timeoutMs", new CapabilityScriptContinuePolicy().TimeoutMs, owner),
            PollIntervalMs = ReadOptionalInt(table, "pollIntervalMs", new CapabilityScriptContinuePolicy().PollIntervalMs, owner),
            TimeoutMessage = ReadOptionalString(table, "timeoutMessage"),
            Condition = condition
        };
    }

    private static int ReadOptionalInt(Dictionary<string, object> table, string key, int fallback, object owner)
    {
        if (!table.TryGetValue(key, out var rawValue) || rawValue == null)
            return fallback;

        try
        {
            return Convert.ToInt32(rawValue, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw CreateCompileException(new InvalidOperationException($"Lua field '{key}' must be a numeric literal. {ex.Message}", ex), owner);
        }
    }

    private static string ReadOptionalString(Dictionary<string, object> table, string key)
    {
        if (!table.TryGetValue(key, out var rawValue) || rawValue == null)
            return string.Empty;

        return Convert.ToString(rawValue, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }

    private static Dictionary<string, object> CompileObjectArgument(object expression, CompilerState state, object owner, string errorMessage)
    {
        var compiled = CompilePureExpression(expression, state);
        return compiled switch
        {
            null => [],
            Dictionary<string, object> dictionary => dictionary,
            _ => throw CreateUnsupportedExpressionException(owner, errorMessage)
        };
    }

    private static string ReadRequiredLiteralString(object expression, object owner, string errorMessage)
    {
        if (TryReadLiteralString(expression, out var text))
            return text;

        throw CreateUnsupportedExpressionException(owner, errorMessage);
    }

    private static bool TryReadLiteralString(object expression, out string text)
    {
        text = null;
        expression = UnwrapAdjustmentExpression(expression);
        if (expression == null || !LiteralExpressionType.IsInstanceOfType(expression))
            return false;

        text = ReadLiteralValue(expression) as string;
        return text != null;
    }

    private static bool TryResolveNamespacedFunction(object expression, out string namespaceName, out string memberName)
    {
        namespaceName = null;
        memberName = null;
        expression = UnwrapAdjustmentExpression(expression);
        if (expression == null || !IndexExpressionType.IsInstanceOfType(expression))
            return false;

        memberName = Convert.ToString(GetFieldValue(expression, "m_Name"), CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(memberName))
            return false;

        namespaceName = TryReadAllowedGlobalName(GetRequiredFieldValue(expression, "m_BaseExp"));
        return !string.IsNullOrWhiteSpace(namespaceName);
    }

    private static string TryReadAllowedGlobalName(object expression)
    {
        expression = UnwrapAdjustmentExpression(expression);
        if (expression == null || !SymbolRefExpressionType.IsInstanceOfType(expression))
            return null;

        var symbol = ReadSymbolRef(expression);
        if (symbol.Type != SymbolRefType.Global)
            return null;

        var name = symbol.Name?.Trim() ?? string.Empty;
        return AllowedGlobalNames.Contains(name) ? name : null;
    }

    private static bool IsLocalAssignmentStatement(object statement, CompilerState state)
    {
        var sourceRef = GetFieldValue(statement, "m_Ref") as SourceRef;
        if (sourceRef == null)
            return false;

        var snippet = ReadSourceSnippet(sourceRef, state.SourceLines).TrimStart();
        return snippet.StartsWith("local", StringComparison.Ordinal);
    }

    private static string ReadSourceSnippet(SourceRef sourceRef, IReadOnlyList<string> sourceLines)
    {
        if (sourceRef == null || sourceLines == null || sourceLines.Count == 0)
            return string.Empty;

        var fromLine = GetIntValue(sourceRef, "FromLine") ?? 0;
        var toLine = GetIntValue(sourceRef, "ToLine") ?? fromLine;
        if (fromLine < 0 || fromLine >= sourceLines.Count || toLine < 0 || toLine >= sourceLines.Count)
            return string.Empty;

        var fromChar = GetIntValue(sourceRef, "FromChar") ?? 0;
        var toChar = GetIntValue(sourceRef, "ToChar") ?? fromChar;
        if (fromLine == toLine)
        {
            var line = sourceLines[fromLine] ?? string.Empty;
            var start = AdjustSourceIndex(line, fromChar);
            var end = AdjustSourceIndex(line, toChar);
            var length = Math.Max(0, end - start);
            return start < line.Length ? line.Substring(start, length) : string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        for (var lineIndex = fromLine; lineIndex <= toLine; lineIndex++)
        {
            var line = sourceLines[lineIndex] ?? string.Empty;
            if (lineIndex == fromLine)
            {
                var start = AdjustSourceIndex(line, fromChar);
                if (start < line.Length)
                    builder.Append(line.Substring(start));
            }
            else if (lineIndex == toLine)
            {
                var end = AdjustSourceIndex(line, toChar);
                builder.Append(line.Substring(0, Math.Min(line.Length, end + 1)));
            }
            else
            {
                builder.Append(line);
            }
        }

        return builder.ToString();
    }

    private static int AdjustSourceIndex(string line, int index)
    {
        return Math.Max(Math.Min(line?.Length ?? 0, index), 0);
    }

    private static object UnwrapAdjustmentExpression(object expression)
    {
        while (expression != null && AdjustmentExpressionType.IsInstanceOfType(expression))
            expression = GetRequiredFieldValue(expression, "expression");

        return expression;
    }

    private static List<CapabilityScriptStep> CompileScopedBlock(object blockStatement, CompilerState state)
    {
        state.PushScope();
        try
        {
            return LowerCompositeStatement(blockStatement, state);
        }
        finally
        {
            state.PopScope();
        }
    }

    private static string GetSymbolExpressionName(object symbolExpression)
    {
        return ReadSymbolName(ReadSymbolRef(symbolExpression));
    }

    private static SymbolRef ReadSymbolRef(object symbolExpression)
    {
        return GetRequiredFieldValue(symbolExpression, "m_Ref") as SymbolRef
               ?? throw CreateUnsupportedExpressionException(symbolExpression, "MoonSharp did not expose a symbol reference.");
    }

    private static string ReadSymbolName(object symbolRef)
    {
        if (symbolRef is not SymbolRef refValue)
            throw CreateCompileException(new InvalidOperationException("MoonSharp did not expose a symbol name."), symbolRef);

        return refValue.Name?.Trim() ?? string.Empty;
    }

    private static List<object> ReadExpressionArray(object exprListExpression)
    {
        exprListExpression = UnwrapAdjustmentExpression(exprListExpression);
        if (exprListExpression == null)
            return [];

        if (ExprListExpressionType.IsInstanceOfType(exprListExpression))
        {
            var expressions = ExprListGetExpressionsMethod.Invoke(exprListExpression, null) as Array;
            return expressions?.Cast<object>().ToList() ?? [];
        }

        return [exprListExpression];
    }

    private static List<object> ReadObjectList(object value)
    {
        if (value == null)
            return [];

        if (value is IEnumerable enumerable && value is not string)
            return enumerable.Cast<object>().ToList();

        throw new InvalidOperationException("MoonSharp did not expose an expected list value.");
    }

    private static object GetRequiredFieldValue(object instance, string fieldName)
    {
        var value = GetFieldValue(instance, fieldName);
        if (value == null)
            throw new InvalidOperationException($"MoonSharp field '{fieldName}' was not available on '{instance?.GetType().FullName}'.");

        return value;
    }

    private static object GetFieldValue(object instance, string fieldName)
    {
        if (instance == null)
            return null;

        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(instance);
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        if (instance == null)
            return null;

        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(instance);
    }

    private static Dictionary<string, object> VariableReference(string variableName, string path = "", bool required = true)
    {
        var reference = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["$var"] = variableName
        };

        if (!string.IsNullOrWhiteSpace(path))
            reference["path"] = path.TrimStart('.');

        if (!required)
            reference["required"] = false;

        return reference;
    }

    private static Dictionary<string, object> StepReference(string stepId, string path)
    {
        var reference = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["$ref"] = stepId
        };

        if (path != null)
            reference["path"] = path;

        return reference;
    }

    private static Dictionary<string, object> TruthyCondition(string variableName)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["path"] = "vars." + variableName,
            ["notIn"] = new List<object> { false, null }
        };
    }

    private static Dictionary<string, object> OperatorExpression(string operatorName, List<object> operands)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [operatorName] = operands
        };
    }

    private static LuaScriptCompileException CreateUnsupportedStatementException(object statement, string message)
    {
        return CreateCompileException(new InvalidOperationException(message), statement, "lua.unsupported_statement");
    }

    private static LuaScriptCompileException CreateUnsupportedExpressionException(object expression, string message)
    {
        return CreateCompileException(new InvalidOperationException(message), expression, "lua.unsupported_expression");
    }

    private static LuaScriptCompileException CreateCompileException(Exception ex, object node, string explicitCode = null)
    {
        var code = explicitCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            code = ex switch
            {
                SyntaxErrorException => "lua.syntax_error",
                LuaScriptCompileException compileException => compileException.Code,
                _ => "lua.compile_error"
            };
        }

        if (ex is LuaScriptCompileException existing)
            return existing;

        var details = new Dictionary<string, object>(StringComparer.Ordinal);
        var location = TryReadLocation(ex is SyntaxErrorException syntaxError ? ReadSyntaxToken(syntaxError) : node);
        if (location.Line.HasValue)
        {
            details["line"] = location.Line.Value;
            details["column"] = location.Column ?? 0;
        }

        if (node != null)
            details["nodeType"] = node.GetType().FullName ?? node.GetType().Name;

        return new LuaScriptCompileException(
            code,
            ex.Message,
            location.Line,
            location.Column,
            details.Count == 0 ? null : details,
            ex);
    }

    private static object ReadSyntaxToken(SyntaxErrorException syntaxError)
    {
        return syntaxError.GetType()
            .GetProperty("Token", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(syntaxError);
    }

    private static (int? Line, int? Column) TryReadLocation(object node)
    {
        if (node == null)
            return (null, null);

        var type = node.GetType();
        if (string.Equals(type.FullName, "MoonSharp.Interpreter.Tree.Token", StringComparison.Ordinal))
        {
            return (
                GetIntValue(node, "FromLine"),
                GetIntValue(node, "FromCol"));
        }

        if (string.Equals(type.FullName, "MoonSharp.Interpreter.Debugging.SourceRef", StringComparison.Ordinal))
        {
            return (
                GetIntValue(node, "FromLine"),
                GetIntValue(node, "FromChar"));
        }

        foreach (var candidate in new[] { "SourceRef", "m_Ref", "m_Start", "m_End", "Source" })
        {
            var propertyValue = GetPropertyValue(node, candidate);
            if (propertyValue != null)
                return TryReadLocation(propertyValue);

            var fieldValue = GetFieldValue(node, candidate);
            if (fieldValue != null)
                return TryReadLocation(fieldValue);
        }

        return (null, null);
    }

    private static int? GetIntValue(object instance, string memberName)
    {
        var field = instance.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.GetValue(instance) is int fieldValue)
            return fieldValue;

        var property = instance.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.GetValue(instance) is int propertyValue)
            return propertyValue;

        return null;
    }

    private static Type GetRequiredType(string fullName)
    {
        return MoonSharpAssembly.GetType(fullName, throwOnError: true)
               ?? throw new InvalidOperationException($"MoonSharp type '{fullName}' could not be loaded.");
    }

    private static MethodInfo GetRequiredMethod(Type type, string name, BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.NonPublic)
    {
        return type.GetMethod(name, bindingFlags)
               ?? throw new InvalidOperationException($"MoonSharp method '{type.FullName}.{name}' could not be loaded.");
    }

    private static void ValidateWritableBindingName(string name, object owner, CompilerState state)
    {
        if (state.IsReadOnly(name))
            throw CreateUnsupportedStatementException(owner, $"Lua binding '{name}' is read-only in rimbridge/run_lua v1.");
    }

    private static object NormalizeParameterValue(object value)
    {
        return value switch
        {
            null => new Dictionary<string, object>(StringComparer.Ordinal),
            IDictionary<string, object> dictionary => NormalizeParameterDictionary(dictionary),
            IDictionary dictionary => NormalizeLegacyParameterDictionary(dictionary),
            IEnumerable<object> items when value is not string => NormalizeParameterList(items),
            IEnumerable enumerable when value is not string => NormalizeParameterList(enumerable.Cast<object>()),
            _ => value
        };
    }

    private static Dictionary<string, object> NormalizeParameterDictionary(IDictionary<string, object> dictionary)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in dictionary)
            normalized[pair.Key] = NormalizeParameterValue(pair.Value);

        return normalized;
    }

    private static Dictionary<string, object> NormalizeLegacyParameterDictionary(IDictionary dictionary)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
                throw new LuaScriptCompileException("lua.invalid_parameters", "Lua parameters must use string keys.");

            normalized[key] = NormalizeParameterValue(entry.Value);
        }

        return normalized;
    }

    private static List<object> NormalizeParameterList(IEnumerable<object> items)
    {
        var normalized = new List<object>();
        foreach (var item in items)
            normalized.Add(NormalizeParameterValue(item));

        return normalized;
    }

    private enum HostCallKind
    {
        Call,
        Poll,
        Print,
        Assert,
        Fail,
        Ipairs
    }

    private sealed class HostCall
    {
        public HostCall(
            HostCallKind kind,
            string displayName,
            string alias = null,
            Dictionary<string, object> arguments = null,
            CapabilityScriptContinuePolicy policy = null,
            string message = null,
            object value = null,
            object collectionExpression = null)
        {
            Kind = kind;
            DisplayName = displayName ?? string.Empty;
            Alias = alias;
            Arguments = arguments;
            Policy = policy;
            Message = message;
            Value = value;
            CollectionExpression = collectionExpression;
        }

        public HostCallKind Kind { get; }

        public string DisplayName { get; }

        public string Alias { get; }

        public Dictionary<string, object> Arguments { get; }

        public CapabilityScriptContinuePolicy Policy { get; }

        public string Message { get; }

        public object Value { get; }

        public object CollectionExpression { get; }
    }

    private sealed class CompilerState
    {
        private readonly List<HashSet<string>> _scopes = [new(StringComparer.Ordinal)];
        private readonly HashSet<string> _readOnlyBindings = new(StringComparer.Ordinal);
        private int _nextGeneratedId = 1;
        private int _nextTempId = 1;

        public CompilerState(string sourceText)
        {
            SourceLines = BuildSourceLines(sourceText);
        }

        public IReadOnlyList<string> SourceLines { get; }

        public void PushScope()
        {
            _scopes.Add(new HashSet<string>(StringComparer.Ordinal));
        }

        public void PopScope()
        {
            if (_scopes.Count <= 1)
                throw new InvalidOperationException("Lua compiler scope underflow.");

            _scopes.RemoveAt(_scopes.Count - 1);
        }

        public void Declare(string name)
        {
            _scopes[_scopes.Count - 1].Add(name);
        }

        public void DeclareReadOnly(string name)
        {
            _readOnlyBindings.Add(name);
            Declare(name);
        }

        public bool IsDeclared(string name)
        {
            for (var index = _scopes.Count - 1; index >= 0; index--)
            {
                if (_scopes[index].Contains(name))
                    return true;
            }

            return false;
        }

        public bool IsReadOnly(string name)
        {
            return _readOnlyBindings.Contains(name ?? string.Empty);
        }

        public string CreateGeneratedId(string prefix)
        {
            return SanitizeIdentifier(prefix) + "_" + _nextGeneratedId++.ToString(CultureInfo.InvariantCulture);
        }

        public string CreateCallId(string alias, string preferredPrefix)
        {
            if (!string.IsNullOrWhiteSpace(preferredPrefix))
                return CreateGeneratedId(preferredPrefix);

            var suffix = alias?.Split('/').LastOrDefault() ?? "call";
            return CreateGeneratedId(suffix);
        }

        public string CreateTemp(string prefix)
        {
            return "__lua_" + SanitizeIdentifier(prefix) + "_" + _nextTempId++.ToString(CultureInfo.InvariantCulture);
        }

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "step";

            var chars = value
                .Trim()
                .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
                .ToArray();

            return new string(chars).Trim('_');
        }

        private static IReadOnlyList<string> BuildSourceLines(string sourceText)
        {
            var lines = new List<string> { string.Empty };
            lines.AddRange((sourceText ?? string.Empty).Split('\n'));
            return lines;
        }
    }
}
