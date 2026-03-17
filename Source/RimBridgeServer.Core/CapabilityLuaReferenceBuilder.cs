using System;
using System.Collections.Generic;

namespace RimBridgeServer.Core;

public static class CapabilityLuaReferenceBuilder
{
    public static Dictionary<string, object> CreateDocument()
    {
        var minimalExample = Obj(
            ("name", "minimal"),
            ("description", "List colonists and return the current-map count."),
            ("luaSource", """
                local colonists = rb.call("rimworld/list_colonists", { currentMapOnly = true })
                return colonists.result.count
                """));
        var pollingExample = Obj(
            ("name", "poll_and_assert"),
            ("description", "Use rb.poll, read attempts, emit trace output, and assert the final condition."),
            ("luaSource", """
                local grouped = rb.poll("rimworld/list_colonists", { currentMapOnly = true }, {
                  timeoutMs = 10000,
                  pollIntervalMs = 100,
                  condition = {
                    all = {
                      { path = "result.colonists", countEquals = 3 },
                      {
                        path = "result.colonists",
                        allItems = {
                          path = "job",
                          ["in"] = { "Wait_Combat", "Wait_MaintainPosture" }
                        }
                      }
                    }
                  }
                })

                rb.print("grouped_attempts", grouped.attempts)
                rb.assert(grouped.attempts >= 1, "Expected at least one poll attempt.")
                return grouped.result
                """));
        var foreachExample = Obj(
            ("name", "foreach_selection"),
            ("description", "Use ipairs, static field access, and boolean expressions in call arguments."),
            ("luaSource", """
                local snapshot = rb.call("rimworld/list_colonists", { currentMapOnly = true })
                local colonists = snapshot.result.colonists

                for i, colonist in ipairs(colonists) do
                  rb.call("rimworld/select_pawn", {
                    pawnName = colonist.name,
                    append = i > 1
                  })
                end
                """));

        return Obj(
            ("success", true),
            ("version", "lua-script-v1"),
            ("summary", "Machine-readable authoring reference for rimbridge/run_lua."),
            ("tool", Obj(
                ("name", "rimbridge/run_lua"),
                ("companionTool", "rimbridge/get_lua_reference"),
                ("compileTool", "rimbridge/compile_lua"),
                ("description", "Compile a narrow Lua subset into the shared script runner and execute it through the normal capability registry."),
                ("arguments", Arr(
                    Field("luaSource", "string", required: true, defaultValue: null, description: "Lua source using the supported rimbridge/run_lua subset documented here."),
                    Field("includeStepResults", "bool", required: false, defaultValue: true, description: "Include result payloads for successful call steps in the returned script report."))))),
            ("compileTool", Obj(
                ("name", "rimbridge/compile_lua"),
                ("description", "Compile supported Lua source into the lowered JSON script model without executing capability calls."),
                ("arguments", Arr(
                    Field("luaSource", "string", required: true, defaultValue: null, description: "Lua source using the supported rimbridge/run_lua subset documented here."))),
                ("returns", Arr(
                    Field("success", "bool", required: true, defaultValue: null, description: "Whether compilation succeeded."),
                    Field("message", "string", required: true, defaultValue: null, description: "Compilation summary or failure message."),
                    Field("script", "CapabilityScriptDefinition", required: false, defaultValue: null, description: "Raw lowered script contract object when compilation succeeds."),
                    Field("scriptJson", "string", required: false, defaultValue: null, description: "Indented JSON serialization of the lowered script."),
                    Field("error", "compileError", required: false, defaultValue: null, description: "Compile error object when compilation fails."))))),
            ("runtimeModel", Obj(
                ("description", "Lua is a front-end over the existing JSON script runner, not a second automation runtime."),
                ("loweringTargetTool", "rimbridge/run_script"),
                ("runtimeReferenceTool", "rimbridge/get_script_reference"),
                ("notes", Arr(
                    "Compile first, then execute the lowered script through the shared capability registry.",
                    "After successful compilation, runtime result shape and runtime failure codes follow the shared script runner model documented by rimbridge/get_script_reference.",
                    "Use rimbridge/compile_lua to inspect the lowered JSON script when a Lua authoring attempt behaves unexpectedly.")))),
            ("supportedSubset", Obj(
                ("statements", Arr(
                    "local assignment",
                    "single-value reassignment to an existing local",
                    "if / elseif / else",
                    "while",
                    "numeric for",
                    "for ... in ipairs(...)",
                    "return",
                    "standalone print / rb.print",
                    "standalone rb.call / rb.poll / rb.assert / rb.fail")),
                ("expressions", Arr(
                    "nil, boolean, string, and number literals",
                    "local variable reads",
                    "table constructors in array-only or object-only form",
                    "static field access such as snapshot.result.count",
                    "static one-based index access such as names[1]",
                    "unary minus and unary not",
                    "binary arithmetic",
                    "binary comparisons",
                    "boolean and / or")),
                ("hostFunctions", Arr("rb.call", "rb.poll", "rb.print", "rb.assert", "rb.fail", "print", "ipairs")),
                ("scopeRules", Arr(
                    "local creates a new scoped variable in the current block.",
                    "Assignment without local updates an existing scoped variable and fails when the variable does not already exist.",
                    "Nested local shadowing is supported and lowers into the shared script scope model.")))),
            ("hostApi", Arr(
                HostFunction(
                    "rb.call",
                    "Invoke one capability once.",
                    "rb.call(alias, argsTable?)",
                    Field("alias", "string literal", required: true, defaultValue: null, description: "Capability alias such as rimworld/list_colonists."),
                    Field("argsTable", "table | nil", required: false, defaultValue: null, description: "Object-style table of capability arguments.")),
                HostFunction(
                    "rb.poll",
                    "Invoke one capability repeatedly until the shared continue condition model matches or times out.",
                    "rb.poll(alias, argsTable?, policyTable)",
                    Field("alias", "string literal", required: true, defaultValue: null, description: "Capability alias such as rimworld/list_colonists."),
                    Field("argsTable", "table | nil", required: false, defaultValue: null, description: "Object-style table of capability arguments."),
                    Field("policyTable", "table", required: true, defaultValue: null, description: "Table containing timeoutMs, pollIntervalMs, optional timeoutMessage, and a JSON-style condition object.")),
                HostFunction(
                    "rb.print",
                    "Append a structured output row to the script result without adding a step report row.",
                    "rb.print(message?, value?)",
                    Field("message", "string literal", required: false, defaultValue: null, description: "Human-readable output label."),
                    Field("value", "expression", required: false, defaultValue: null, description: "Optional structured value included in the output row.")),
                HostFunction(
                    "print",
                    "Alias of rb.print with the same lowering behavior.",
                    "print(message?, value?)",
                    Field("message", "string literal", required: false, defaultValue: null, description: "Human-readable output label."),
                    Field("value", "expression", required: false, defaultValue: null, description: "Optional structured value included in the output row.")),
                HostFunction(
                    "rb.assert",
                    "Fail the script immediately when the condition expression is falsey.",
                    "rb.assert(conditionExpression, message?)",
                    Field("conditionExpression", "expression", required: true, defaultValue: null, description: "Lua expression lowered into the shared script expression model."),
                    Field("message", "string literal", required: false, defaultValue: string.Empty, description: "Optional assertion failure message.")),
                HostFunction(
                    "rb.fail",
                    "Stop the script immediately with an explicit failure.",
                    "rb.fail(message, value?)",
                    Field("message", "string literal", required: true, defaultValue: null, description: "Failure message surfaced at the top level."),
                    Field("value", "expression", required: false, defaultValue: null, description: "Optional structured detail value attached to the failure.")),
                HostFunction(
                    "ipairs",
                    "Iterator helper supported only in 'for ... in ipairs(collection) do'.",
                    "ipairs(collection)",
                    Field("collection", "expression", required: true, defaultValue: null, description: "Array-like collection expression.")))),
            ("accessRules", Arr(
                "Only static field access is supported. Example: snapshot.result.count",
                "Only static one-based index access is supported. Example: names[1]",
                "Dynamic indexing such as names[i] is not supported in v1.",
                "Host capability calls can appear as standalone statements, as the sole right-hand side of an assignment, or as the sole returned expression.",
                "Multiple assignment and table-field assignment are not supported.")),
            ("unsupported", Arr(
                "break",
                "require",
                "metatables",
                "coroutines",
                "module loading",
                "direct CLR access",
                "arbitrary global assignment",
                "dynamic table keys",
                "dynamic indexing",
                "mixed array/object table constructors",
                "multiple-assignment statements")),
            ("compileErrors", Obj(
                ("fields", Arr(
                    Field("code", "string", required: true, defaultValue: null, description: "Compile error code such as lua.syntax_error or lua.unsupported_expression."),
                    Field("message", "string", required: true, defaultValue: null, description: "Human-readable compile failure message."),
                    Field("line", "int", required: false, defaultValue: null, description: "One-based line number when available."),
                    Field("column", "int", required: false, defaultValue: null, description: "One-based column number or source character position when available."),
                    Field("details", "object", required: false, defaultValue: null, description: "Optional structured details such as nodeType."))),
                ("failureCodes", Arr(
                    FailureCode("lua.invalid_source", "The luaSource payload was empty."),
                    FailureCode("lua.compile_error", "Compilation failed before or during lowering."),
                    FailureCode("lua.syntax_error", "MoonSharp rejected the Lua source as invalid syntax."),
                    FailureCode("lua.unsupported_statement", "The Lua source used a statement not supported by rimbridge/run_lua v1."),
                    FailureCode("lua.unsupported_expression", "The Lua source used an expression not supported by rimbridge/run_lua v1."))))),
            ("runtimeResultShape", Obj(
                ("notes", Arr(
                    "run_lua returns the same top-level runtime shape as run_script: success, message, returned, result, error, output, and script.",
                    "The nested script report follows the shared script runner model documented by rimbridge/get_script_reference.")),
                ("topLevelFields", Arr(
                    Field("success", "bool", required: true, defaultValue: null, description: "Overall Lua script success."),
                    Field("message", "string", required: true, defaultValue: null, description: "Success summary or top-level failure message."),
                    Field("returned", "bool", required: true, defaultValue: false, description: "True when the Lua script ended with return."),
                    Field("result", "object", required: false, defaultValue: null, description: "Value returned by the Lua script."),
                    Field("error", "object", required: false, defaultValue: null, description: "Top-level compile or runtime error."),
                    Field("output", "output[]", required: true, defaultValue: Arr(), description: "Structured trace rows emitted by print/rb.print."),
                    Field("script", "report", required: false, defaultValue: null, description: "Shared script runner report after successful compilation."))))),
            ("authoringTips", Arr(
                "Call rimbridge/get_script_reference as well when you need the exact runtime condition model or lowered JSON result shape.",
                "Use rimbridge/compile_lua first when bringing up a new scenario so you can inspect the lowered JSON script.",
                "Prefer rb.print and rb.assert when building test-like scripts with explicit trace output and a clean failure boundary.",
                "Assign rb.poll(...) to a local when you need both the final result and step metadata such as attempts.")),
            ("examples", Arr(minimalExample, pollingExample, foreachExample)));
    }

    private static Dictionary<string, object> HostFunction(string name, string description, string signature, params object[] parameters)
    {
        return Obj(
            ("name", name),
            ("description", description),
            ("signature", signature),
            ("parameters", Arr(parameters)));
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
