using System;
using System.Collections.Generic;
using RimBridgeServer.Contracts;

namespace RimBridgeServer.Core;

public static class CapabilityScriptReferenceBuilder
{
    public static Dictionary<string, object> CreateDocument()
    {
        var definitionDefaults = new CapabilityScriptDefinition();
        var continueDefaults = new CapabilityScriptContinuePolicy();
        var whileDefaults = new CapabilityScriptStep();
        var minimalExample = Obj(
            ("name", "minimal"),
            ("continueOnError", false),
            ("steps", Arr(
                Obj(
                    ("id", "pause"),
                    ("call", "rimworld/pause_game"),
                    ("arguments", Obj(("pause", true)))),
                Obj(
                    ("id", "ping"),
                    ("call", "rimbridge/ping")))));
        var valuePassingExample = Obj(
            ("name", "value-passing"),
            ("continueOnError", false),
            ("steps", Arr(
                Obj(
                    ("id", "discover"),
                    ("call", "rimworld/list_architect_categories")),
                Obj(
                    ("id", "designators"),
                    ("call", "rimworld/list_architect_designators"),
                    ("arguments", Obj(
                        ("categoryId", Obj(
                            ("$ref", "discover"),
                            ("path", "result.categories[0].id")))))))));
        var boundedLoopExample = Obj(
            ("name", "bounded-loop"),
            ("continueOnError", false),
            ("steps", Arr(
                Obj(("type", "let"), ("name", "count"), ("value", 0)),
                Obj(
                    ("type", "while"),
                    ("maxIterations", 3),
                    ("condition", Obj(("path", "vars.count"), ("lessThan", 2))),
                    ("body", Arr(
                        Obj(
                            ("type", "set"),
                            ("name", "count"),
                            ("value", Obj(
                                ("$add", Arr(
                                    Obj(("$var", "count")),
                                    1))))),
                        Obj(("id", "ping"), ("call", "rimbridge/ping"))))),
                Obj(
                    ("type", "return"),
                    ("value", Obj(("count", Obj(("$var", "count")))))))));
        var testStyleExample = Obj(
            ("name", "test-style"),
            ("continueOnError", false),
            ("steps", Arr(
                Obj(("type", "let"), ("name", "expectedCount"), ("value", 3)),
                Obj(("id", "colonists"), ("call", "rimworld/list_colonists"), ("arguments", Obj(("currentMapOnly", true)))),
                Obj(("id", "trace_count"), ("type", "print"), ("message", "Observed colonist count"), ("value", Obj(("$ref", "colonists"), ("path", "result.count")))),
                Obj(
                    ("id", "assert_count"),
                    ("type", "assert"),
                    ("message", "Expected exactly three colonists."),
                    ("condition", Obj(
                        ("path", "vars.expectedCount"),
                        ("equals", Obj(("$ref", "colonists"), ("path", "result.count")))))),
                Obj(
                    ("type", "return"),
                    ("value", Obj(
                        ("colonistCount", Obj(("$ref", "colonists"), ("path", "result.count")))))))));

        return Obj(
            ("success", true),
            ("version", "json-script-v1"),
            ("summary", "Machine-readable authoring reference for rimbridge/run_script."),
            ("tool", Obj(
                ("name", "rimbridge/run_script"),
                ("companionTool", "rimbridge/get_script_reference"),
                ("description", "Execute a JSON script through the shared capability registry."),
                ("arguments", Arr(
                    Obj(
                        ("name", "scriptJson"),
                        ("type", "string"),
                        ("required", true),
                        ("description", "Serialized JSON script matching the root definition documented here.")),
                    Obj(
                        ("name", "includeStepResults"),
                        ("type", "bool"),
                        ("required", false),
                        ("defaultValue", true),
                        ("description", "Include result payloads for successful call steps in the returned report.")))))),
            ("definition", Obj(
                ("description", "Root JSON object accepted by rimbridge/run_script."),
                ("fields", Arr(
                    Field("name", "string", required: false, defaultValue: string.Empty, description: "Optional human-readable script name."),
                    Field("continueOnError", "bool", required: false, defaultValue: definitionDefaults.ContinueOnError, description: "Continue after failed call steps. Explicit 'assert' and 'fail' still halt immediately."),
                    Field("maxDurationMs", "int", required: false, defaultValue: definitionDefaults.MaxDurationMs, description: "Whole-script wall-clock limit in milliseconds."),
                    Field("maxExecutedStatements", "int", required: false, defaultValue: definitionDefaults.MaxExecutedStatements, description: "Global execution budget across statements, loop iterations, and continueUntil retry attempts."),
                    Field("maxControlDepth", "int", required: false, defaultValue: definitionDefaults.MaxControlDepth, description: "Maximum nested control-body depth across if/foreach/while."),
                    Field("steps", "statement[]", required: true, defaultValue: null, description: "Ordered statements. The script must contain at least one step."))))),
            ("statementTypes", Arr(
                StatementType(
                    "call",
                    "Invoke a registered capability.",
                    "Successful call steps add one report row. Repeated executions of the same call statement are reported as 'step', 'step#2', and so on.",
                    Field("id", "string", required: false, defaultValue: null, description: "Stable step id. Required when later expressions reference this step."),
                    Field("call", "string", required: true, defaultValue: null, description: "Capability alias such as rimworld/list_colonists."),
                    Field("arguments", "object", required: false, defaultValue: Obj(), description: "Capability arguments. Values can contain expressions such as $ref and $var."),
                    Field("continueUntil", "object", required: false, defaultValue: null, description: "Optional bounded polling policy that re-invokes this same call until the condition matches or the timeout expires.")),
                StatementType(
                    "let",
                    "Declare a scoped variable.",
                    "Successful control statements do not emit report rows.",
                    Field("type", "\"let\"", required: true, defaultValue: "let", description: "Statement type discriminator."),
                    Field("name", "string", required: true, defaultValue: null, description: "Variable name to declare in the current scope."),
                    Field("value", "expression", required: false, defaultValue: null, description: "Initial value expression.")),
                StatementType(
                    "set",
                    "Assign an existing variable in the active scope chain.",
                    "Fails when the variable does not exist in any active scope.",
                    Field("type", "\"set\"", required: true, defaultValue: "set", description: "Statement type discriminator."),
                    Field("name", "string", required: true, defaultValue: null, description: "Variable name to update."),
                    Field("value", "expression", required: false, defaultValue: null, description: "Assigned value expression.")),
                StatementType(
                    "if",
                    "Branch on a condition.",
                    "Condition evaluation for if/assert/while uses the condition root documented below.",
                    Field("type", "\"if\"", required: true, defaultValue: "if", description: "Statement type discriminator."),
                    Field("condition", "condition", required: true, defaultValue: null, description: "Condition object."),
                    Field("body", "statement[]", required: false, defaultValue: Arr(), description: "Statements executed when the condition matches."),
                    Field("elseBody", "statement[]", required: false, defaultValue: Arr(), description: "Statements executed when the condition does not match.")),
                StatementType(
                    "foreach",
                    "Iterate a resolved collection.",
                    "Each iteration creates a new variable scope and increments the global execution budget.",
                    Field("type", "\"foreach\"", required: true, defaultValue: "foreach", description: "Statement type discriminator."),
                    Field("itemName", "string", required: true, defaultValue: null, description: "Variable name bound to the current item."),
                    Field("indexName", "string", required: false, defaultValue: string.Empty, description: "Optional variable name bound to the zero-based loop index."),
                    Field("collection", "expression", required: true, defaultValue: null, description: "Expression that resolves to an enumerable value."),
                    Field("body", "statement[]", required: false, defaultValue: Arr(), description: "Statements executed once per item.")),
                StatementType(
                    "while",
                    "Execute a bounded loop while a condition matches.",
                    "The condition is re-evaluated before each iteration. maxIterations is mandatory and complements the global script limits.",
                    Field("type", "\"while\"", required: true, defaultValue: "while", description: "Statement type discriminator."),
                    Field("condition", "condition", required: true, defaultValue: null, description: "Condition object."),
                    Field("maxIterations", "int", required: true, defaultValue: whileDefaults.MaxIterations, description: "Maximum number of loop iterations before failing with script.max_iterations."),
                    Field("body", "statement[]", required: false, defaultValue: Arr(), description: "Statements executed for each iteration.")),
                StatementType(
                    "assert",
                    "Fail immediately when an assumption is not satisfied.",
                    "Failed assertions halt the whole script even when continueOnError is true.",
                    Field("type", "\"assert\"", required: true, defaultValue: "assert", description: "Statement type discriminator."),
                    Field("condition", "condition", required: true, defaultValue: null, description: "Condition object."),
                    Field("message", "string", required: false, defaultValue: string.Empty, description: "Optional custom assertion failure message.")),
                StatementType(
                    "fail",
                    "Stop the script immediately with an explicit failure.",
                    "Use this for script-controlled bail-outs after a check or probe step.",
                    Field("type", "\"fail\"", required: true, defaultValue: "fail", description: "Statement type discriminator."),
                    Field("message", "string", required: false, defaultValue: string.Empty, description: "Failure message surfaced at the top level."),
                    Field("value", "expression", required: false, defaultValue: null, description: "Optional structured details attached to the failure error.")),
                StatementType(
                    "print",
                    "Append a structured trace row to the script output.",
                    "Print output is returned at the top level and does not add a step report row.",
                    Field("type", "\"print\"", required: true, defaultValue: "print", description: "Statement type discriminator."),
                    Field("message", "string", required: false, defaultValue: string.Empty, description: "Human-readable output label."),
                    Field("value", "expression", required: false, defaultValue: null, description: "Optional structured value to include in the output row.")),
                StatementType(
                    "return",
                    "End the script successfully with a final result value.",
                    "Later statements are not executed after return.",
                    Field("type", "\"return\"", required: true, defaultValue: "return", description: "Statement type discriminator."),
                    Field("value", "expression", required: false, defaultValue: null, description: "Final structured result value.")))),
            ("expressionForms", Arr(
                Expr("literal", "Any plain JSON scalar, array, or object that does not use a reserved scripting operator key."),
                Expr("$ref", "Reference an earlier executed call step.", Obj(
                    ("$ref", "<step-id>"),
                    ("path", "result.someField"),
                    ("required", true))),
                Expr("$var", "Read a variable from the active scope chain.", Obj(
                    ("$var", "<variable-name>"),
                    ("path", "nested.path"),
                    ("required", true))),
                Expr("$add", "Add two or more numeric operands.", Arr(1, 2, 3)),
                Expr("$subtract", "Subtract the second numeric operand from the first.", Arr(10, 3)),
                Expr("$multiply", "Multiply two or more numeric operands.", Arr(2, 4)),
                Expr("$divide", "Divide the first numeric operand by the second.", Arr(8, 2)),
                Expr("$mod", "Take the remainder of the first numeric operand divided by the second.", Arr(7, 3)),
                Expr("$negate", "Negate one numeric operand.", 5),
                Expr("$not", "Apply scripting truthiness and invert the operand.", true),
                Expr("$and", "Return the logical conjunction of two or more operands using scripting truthiness.", Arr(true, false)),
                Expr("$or", "Return the logical disjunction of two or more operands using scripting truthiness.", Arr(false, "fallback")),
                Expr("$equals", "Compare two operands for equality and return a boolean.", Arr(3, 3)),
                Expr("$notEquals", "Compare two operands for inequality and return a boolean.", Arr(3, 4)),
                Expr("$greaterThan", "Compare two numeric operands and return a boolean.", Arr(5, 4)),
                Expr("$greaterThanOrEqual", "Compare two numeric operands and return a boolean.", Arr(5, 5)),
                Expr("$lessThan", "Compare two numeric operands and return a boolean.", Arr(4, 5)),
                Expr("$lessThanOrEqual", "Compare two numeric operands and return a boolean.", Arr(5, 5)))),
            ("references", Obj(
                ("stepReferenceRootFields", Arr("index", "id", "call", "capabilityId", "operationId", "status", "success", "attempts", "startedAtUtc", "completedAtUtc", "durationMs", "result", "error", "warnings")),
                ("notes", Arr(
                    "References are only allowed to already executed call steps.",
                    "Path defaults to 'result' when omitted.",
                    "Duplicate call-step ids are rejected because references require unique targets.",
                    "When a repeated call step executes multiple times, $ref resolves to the latest execution of that base step id.")))),
            ("continueUntil", Obj(
                ("description", "Optional bounded polling policy on a call step."),
                ("fields", Arr(
                    Field("timeoutMs", "int", required: false, defaultValue: continueDefaults.TimeoutMs, description: "Maximum total polling time in milliseconds."),
                    Field("pollIntervalMs", "int", required: false, defaultValue: continueDefaults.PollIntervalMs, description: "Delay between attempts in milliseconds."),
                    Field("timeoutMessage", "string", required: false, defaultValue: continueDefaults.TimeoutMessage, description: "Optional custom timeout message."),
                    Field("condition", "condition", required: true, defaultValue: null, description: "Condition evaluated against the latest call attempt."))))),
            ("conditionModel", Obj(
                ("description", "Conditions are used by continueUntil, if, while, and assert."),
                ("roots", Arr(
                    Obj(
                        ("context", "continueUntil"),
                        ("rootDescription", "The current root is the latest call-step reference root. Paths commonly start with result.<field>."),
                        ("examplePath", "result.colonists")),
                    Obj(
                        ("context", "if / while / assert"),
                        ("rootDescription", "The current root contains only script variables under vars."),
                        ("examplePath", "vars.count")))),
                ("operators", Arr(
                    ConditionOperator("all", "condition[]", "All child conditions must match."),
                    ConditionOperator("any", "condition[]", "At least one child condition must match."),
                    ConditionOperator("path", "string | expression", "Resolve a nested path from the current root before applying comparisons."),
                    ConditionOperator("exists", "bool | expression", "Assert whether the selected path exists."),
                    ConditionOperator("allItems", "condition", "Apply the child condition to every item in the selected collection."),
                    ConditionOperator("anyItem", "condition", "Apply the child condition until any selected collection item matches."),
                    ConditionOperator("countEquals", "int | expression", "Compare the selected collection count."),
                    ConditionOperator("equals", "expression", "Require equality with the selected value."),
                    ConditionOperator("notEquals", "expression", "Require inequality with the selected value."),
                    ConditionOperator("in", "collection expression", "Require the selected value to be in the provided collection."),
                    ConditionOperator("notIn", "collection expression", "Require the selected value not to be in the provided collection."),
                    ConditionOperator("greaterThan", "number | expression", "Require a numeric value greater than the provided operand."),
                    ConditionOperator("greaterThanOrEqual", "number | expression", "Require a numeric value greater than or equal to the provided operand."),
                    ConditionOperator("lessThan", "number | expression", "Require a numeric value less than the provided operand."),
                    ConditionOperator("lessThanOrEqual", "number | expression", "Require a numeric value less than or equal to the provided operand."))))),
            ("resultShape", Obj(
                ("topLevelFields", Arr(
                    Field("success", "bool", required: true, defaultValue: null, description: "Overall script success."),
                    Field("message", "string", required: true, defaultValue: null, description: "Success summary or top-level failure message."),
                    Field("returned", "bool", required: true, defaultValue: false, description: "True when a return statement ended the script successfully."),
                    Field("result", "object", required: false, defaultValue: null, description: "Final value produced by return."),
                    Field("error", "object", required: false, defaultValue: null, description: "Top-level script error when the script fails."),
                    Field("output", "output[]", required: true, defaultValue: Arr(), description: "Structured rows emitted by print."),
                    Field("script", "report", required: true, defaultValue: null, description: "Full operational script report."))),
                ("reportFields", Arr(
                    Field("name", "string", required: true, defaultValue: string.Empty, description: "Script name."),
                    Field("continueOnError", "bool", required: true, defaultValue: false, description: "Effective continueOnError flag."),
                    Field("success", "bool", required: true, defaultValue: null, description: "Report success."),
                    Field("halted", "bool", required: true, defaultValue: false, description: "Whether the script halted early."),
                    Field("haltReason", "string", required: true, defaultValue: string.Empty, description: "Human-readable halt reason."),
                    Field("stepCount", "int", required: true, defaultValue: null, description: "Total number of statements in the script tree."),
                    Field("executedStepCount", "int", required: true, defaultValue: null, description: "Number of call or failed-control report rows emitted."),
                    Field("steps", "stepReport[]", required: true, defaultValue: Arr(), description: "Per-step report rows for call steps and failed control statements."))),
                ("stepReportFields", Arr(
                    Field("index", "int", required: true, defaultValue: null, description: "One-based report row index."),
                    Field("id", "string", required: true, defaultValue: null, description: "Step report id."),
                    Field("call", "string", required: true, defaultValue: string.Empty, description: "Capability alias or synthetic script/<type> identifier for failed control statements."),
                    Field("success", "bool", required: true, defaultValue: null, description: "Whether the reported step succeeded."),
                    Field("attempts", "int", required: true, defaultValue: 1, description: "Number of call attempts when continueUntil was used."),
                    Field("result", "object", required: false, defaultValue: null, description: "Step result payload when includeStepResults is true."),
                    Field("error", "object", required: false, defaultValue: null, description: "Error object when the step failed."))),
                ("outputFields", Arr(
                    Field("index", "int", required: true, defaultValue: null, description: "One-based output row index."),
                    Field("statementId", "string", required: true, defaultValue: string.Empty, description: "Owning print statement id."),
                    Field("level", "string", required: true, defaultValue: "info", description: "Output level."),
                    Field("message", "string", required: true, defaultValue: string.Empty, description: "Output label."),
                    Field("value", "object", required: false, defaultValue: null, description: "Structured output payload."))))),
            ("limits", Obj(
                ("global", Arr(
                    Obj(("name", "maxDurationMs"), ("defaultValue", definitionDefaults.MaxDurationMs), ("failureCode", "script.timeout")),
                    Obj(("name", "maxExecutedStatements"), ("defaultValue", definitionDefaults.MaxExecutedStatements), ("failureCode", "script.statement_limit_exceeded")),
                    Obj(("name", "maxControlDepth"), ("defaultValue", definitionDefaults.MaxControlDepth), ("failureCode", "script.max_depth_exceeded")))),
                ("local", Arr(
                    Obj(("name", "while.maxIterations"), ("defaultValue", whileDefaults.MaxIterations), ("failureCode", "script.max_iterations")),
                    Obj(("name", "continueUntil.timeoutMs"), ("defaultValue", continueDefaults.TimeoutMs), ("failureCode", "script.continue_timeout")))))),
            ("failureCodes", Arr(
                FailureCode("script.invalid_definition", "The root script definition declared an invalid global limit."),
                FailureCode("script.invalid_step", "A statement was malformed or declared an unsupported type."),
                FailureCode("script.invalid_reference", "A $ref expression could not resolve an executed step or path."),
                FailureCode("script.invalid_variable", "A set or $var expression referenced a missing variable."),
                FailureCode("script.invalid_expression", "An expression such as arithmetic or fail/print/return value resolution was invalid."),
                FailureCode("script.invalid_condition", "A condition object was malformed or could not be evaluated."),
                FailureCode("script.continue_timeout", "A continueUntil policy did not match before its timeout."),
                FailureCode("script.max_iterations", "A while loop exceeded maxIterations."),
                FailureCode("script.statement_limit_exceeded", "The script exceeded maxExecutedStatements."),
                FailureCode("script.max_depth_exceeded", "The script exceeded maxControlDepth."),
                FailureCode("script.timeout", "The script exceeded maxDurationMs."),
                FailureCode("script.assertion_failed", "An assert statement failed."),
                FailureCode("script.failed", "A fail statement requested explicit bailout."))),
            ("authoringTips", Arr(
                "Give stable ids to call steps you plan to reference later with $ref.",
                "Prefer explicit wait tools and continueUntil instead of blind sleeps.",
                "Use assert, fail, print, and return so scripts behave like higher-level testable tool calls.",
                "For default pawn-selected map actions such as drafted goto, prefer rimworld/right_click_cell. Use open_context_menu plus execute_context_menu_option when you need to inspect or choose a non-default menu option.",
                "For mods that distinguish tap from hold on map clicks, set holdDurationMs on rimworld/right_click_cell or rimworld/open_context_menu so the injected click matches the live interaction pattern.")),
            ("examples", Arr(
                Obj(
                    ("name", "minimal"),
                    ("description", "Two ordered capability calls."),
                    ("script", minimalExample)),
                Obj(
                    ("name", "value_passing"),
                    ("description", "Use $ref to pass one step result into the next."),
                    ("script", valuePassingExample)),
                Obj(
                    ("name", "bounded_loop"),
                    ("description", "Use let, while, set, arithmetic, and return."),
                    ("script", boundedLoopExample)),
                Obj(
                    ("name", "test_style"),
                    ("description", "Use print, assert, and return for a test-like script."),
                    ("script", testStyleExample)))));
    }

    private static Dictionary<string, object> StatementType(string type, string description, string reportBehavior, params object[] fields)
    {
        return Obj(
            ("type", type),
            ("description", description),
            ("reportBehavior", reportBehavior),
            ("fields", Arr(fields)));
    }

    private static Dictionary<string, object> ConditionOperator(string name, string operandType, string description)
    {
        return Obj(
            ("name", name),
            ("operandType", operandType),
            ("description", description));
    }

    private static Dictionary<string, object> Expr(string name, string description, object shape = null)
    {
        return Obj(
            ("name", name),
            ("description", description),
            ("shape", shape));
    }

    private static Dictionary<string, object> FailureCode(string code, string description)
    {
        return Obj(
            ("code", code),
            ("description", description));
    }

    private static Dictionary<string, object> Field(string name, string type, bool required, object defaultValue, string description)
    {
        return Obj(
            ("name", name),
            ("type", type),
            ("required", required),
            ("defaultValue", defaultValue),
            ("description", description));
    }

    private static Dictionary<string, object> Obj(params (string Key, object Value)[] pairs)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
            result[key] = value;

        return result;
    }

    private static List<object> Arr(params object[] values)
    {
        return new List<object>(values ?? Array.Empty<object>());
    }
}
