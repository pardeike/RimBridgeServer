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
        var bridgeStatePollingExample = Obj(
            ("name", "wait_for_entry_scene"),
            ("description", "Poll bridge state until RimWorld is back at the main menu entry scene."),
            ("luaSource", """
                local entry = rb.poll("rimbridge/get_bridge_status", {}, {
                  timeoutMs = 30000,
                  pollIntervalMs = 100,
                  condition = {
                    all = {
                      { path = "result.state.inEntryScene", equals = true },
                      { path = "result.state.hasCurrentGame", equals = false },
                      { path = "result.state.longEventPending", equals = false }
                    }
                  }
                })

                return entry.result.state
                """));
        var planningExample = Obj(
            ("name", "bounded_search_and_validate"),
            ("description", "Use a bounded while loop, generic map search, and dry-run validation to plan before acting."),
            ("luaSource", """
                local searchRadius = 4
                local planningAttempts = 0
                local chosen = nil

                while chosen == nil and planningAttempts < 6 do
                  planningAttempts = planningAttempts + 1

                  local candidate = rb.call("rimworld/find_random_cell_near", {
                    x = 120,
                    z = 120,
                    startingSearchRadius = searchRadius,
                    maxSearchRadius = searchRadius + 8,
                    width = 3,
                    height = 3,
                    footprintAnchor = "center",
                    requireWalkable = true,
                    requireStandable = true,
                    requireNoImpassableThings = true
                  })

                  if candidate.result.success == true then
                    chosen = candidate
                  end

                  searchRadius = searchRadius + 2
                end

                rb.assert(chosen ~= nil, "Expected to find a candidate cell.")
                rb.print("planning_attempts", planningAttempts)
                return chosen.result.cell
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
        var paramsExample = Obj(
            ("name", "params_binding"),
            ("description", "Read runtime parameters from the injected read-only params table."),
            ("luaSource", """
                rb.assert(params.screenshotFileName ~= nil, "params.screenshotFileName is required.")
                local retryLimit = params.retryLimit or 6

                rb.print("screenshot", params.screenshotFileName)
                return {
                  screenshotFileName = params.screenshotFileName,
                  retryLimit = retryLimit
                }
                """));

        return Obj(
            ("success", true),
            ("version", "lua-script-v1"),
            ("summary", "Machine-readable authoring reference for the lowered rimbridge/run_lua subset. This is a small Lua-shaped front-end over the shared script runner, not general-purpose Lua."),
            ("quickStart", Obj(
                ("summary", "Treat rimbridge/run_lua as a lowered DSL with Lua syntax, not as full Lua."),
                ("recommendedWorkflow", Arr(
                    "Call rimbridge/get_lua_reference before inventing a new script shape.",
                    "Use rimbridge/compile_lua first for fresh inline scripts so you can inspect the lowered JSON before executing mutations.",
                    "Start from local variables plus rb.call/rb.poll/rb.print/rb.assert and only add more control flow once that smaller shape compiles.")),
                ("firstRules", Arr(
                    "Declare variables with local before assigning to them. Assignment without local only updates an already-declared binding.",
                    "Use only the supported host helpers: rb.call, rb.poll, rb.print, rb.assert, rb.fail, print, and ipairs.",
                    "Use static field access such as snapshot.result.count and static one-based indexes such as names[1]. Dynamic indexing such as names[i] is rejected in v1.",
                    "Host calls may only appear as standalone statements or as the sole right-hand side of an assignment or return.")),
                ("starterTemplate", """
                    local snapshot = rb.call("rimworld/list_colonists", { currentMapOnly = true })
                    local colonists = snapshot.result.colonists

                    rb.assert(colonists ~= nil, "Expected colonists.")
                    return colonists[1]
                    """))),
            ("tool", Obj(
                ("name", "rimbridge/run_lua"),
                ("companionTool", "rimbridge/get_lua_reference"),
                ("compileTool", "rimbridge/compile_lua"),
                ("description", "Compile a small lowered Lua subset into the shared script runner and execute it through the normal capability registry. This is not general-purpose Lua."),
                ("arguments", Arr(
                    Field("luaSource", "string", required: true, defaultValue: null, description: "Lua source using the supported rimbridge/run_lua subset documented here. Start with local bindings, rb.call/rb.poll, static field access, and static one-based indexes such as names[1]."),
                    Field("parameters", "object", required: false, defaultValue: null, description: "Optional object-style parameters exposed to the script as the read-only global params table."),
                    Field("includeStepResults", "bool", required: false, defaultValue: true, description: "Include result payloads for successful call steps in the returned script report."))))),
            ("fileTool", Obj(
                ("name", "rimbridge/run_lua_file"),
                ("companionTool", "rimbridge/get_lua_reference"),
                ("compileTool", "rimbridge/compile_lua_file"),
                ("description", "Load a .lua file, treat it as the same lowered Lua subset used by rimbridge/run_lua, and execute it through the shared script runner."),
                ("arguments", Arr(
                    Field("scriptPath", "string", required: true, defaultValue: null, description: "Absolute path or current-working-directory-relative path to a .lua file."),
                    Field("parameters", "object", required: false, defaultValue: null, description: "Optional object-style parameters exposed to the script as the read-only global params table."),
                    Field("includeStepResults", "bool", required: false, defaultValue: true, description: "Include result payloads for successful call steps in the returned script report."))))),
            ("compileTool", Obj(
                ("name", "rimbridge/compile_lua"),
                ("description", "Compile the supported lowered Lua subset, not general-purpose Lua, into the JSON script model without executing capability calls. Use this first for new script shapes."),
                ("arguments", Arr(
                    Field("luaSource", "string", required: true, defaultValue: null, description: "Lua source using the supported rimbridge/run_lua subset documented here. Prefer local bindings, rb.call/rb.poll, static field access, and static one-based indexes."),
                    Field("parameters", "object", required: false, defaultValue: null, description: "Optional object-style parameters exposed to the script as the read-only global params table."))),
                ("returns", Arr(
                    Field("success", "bool", required: true, defaultValue: null, description: "Whether compilation succeeded."),
                    Field("message", "string", required: true, defaultValue: null, description: "Compilation summary or failure message."),
                    Field("script", "CapabilityScriptDefinition", required: false, defaultValue: null, description: "Raw lowered script contract object when compilation succeeds."),
                    Field("scriptJson", "string", required: false, defaultValue: null, description: "Indented JSON serialization of the lowered script."),
                    Field("error", "compileError", required: false, defaultValue: null, description: "Compile error object when compilation fails."))))),
            ("compileFileTool", Obj(
                ("name", "rimbridge/compile_lua_file"),
                ("description", "Load a .lua file and compile it as the same lowered Lua subset used by rimbridge/run_lua without executing capability calls."),
                ("arguments", Arr(
                    Field("scriptPath", "string", required: true, defaultValue: null, description: "Absolute path or current-working-directory-relative path to a .lua file."),
                    Field("parameters", "object", required: false, defaultValue: null, description: "Optional object-style parameters exposed to the script as the read-only global params table."))),
                ("returns", Arr(
                    Field("success", "bool", required: true, defaultValue: null, description: "Whether compilation succeeded."),
                    Field("message", "string", required: true, defaultValue: null, description: "Compilation summary or failure message."),
                    Field("scriptPath", "string", required: true, defaultValue: null, description: "Requested script path."),
                    Field("resolvedScriptPath", "string", required: true, defaultValue: null, description: "Resolved on-disk path used for compilation."),
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
                    "Use rimbridge/compile_lua or rimbridge/compile_lua_file to inspect the lowered JSON script when a Lua authoring attempt behaves unexpectedly.")))),
            ("parameterBinding", Obj(
                ("name", "params"),
                ("type", "read-only object"),
                ("availableInTools", Arr("rimbridge/run_lua", "rimbridge/compile_lua", "rimbridge/run_lua_file", "rimbridge/compile_lua_file")),
                ("description", "Object-style runtime parameters injected as a top-level read-only Lua binding."),
                ("notes", Arr(
                    "Use params.field and static one-based indexing such as params.names[1] to read values.",
                    "params is always present and defaults to an empty object when the caller omits parameters.",
                    "Missing params fields and static indexes resolve as nil so normal Lua defaulting patterns such as params.retryLimit or 6 work.",
                    "Reassigning or shadowing params is rejected at compile time.")))),
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
                    "boolean and / or with Lua-style operand return values")),
                ("hostFunctions", Arr("rb.call", "rb.poll", "rb.print", "rb.assert", "rb.fail", "print", "ipairs")),
                ("scopeRules", Arr(
                    "local creates a new scoped variable in the current block.",
                    "Assignment without local updates an existing scoped variable and fails when the variable does not already exist.",
                    "Nested local shadowing is supported and lowers into the shared script scope model.")))),
            ("commonPitfalls", Arr(
                Obj(
                    ("pattern", "count = 1"),
                    ("whyItFails", "run_lua v1 rejects arbitrary global assignment. A new binding must be declared with local."),
                    ("preferredForm", "local count = 1")),
                Obj(
                    ("pattern", "names[i]"),
                    ("whyItFails", "run_lua v1 only supports static one-based indexes such as names[1]. Dynamic indexing is not lowered."),
                    ("preferredForm", "Use 'for i, name in ipairs(names) do' when iterating, or use a fixed index such as names[1].")),
                Obj(
                    ("pattern", "rb.call(aliasVar, args)"),
                    ("whyItFails", "Capability aliases must be literal strings so the compiler can lower them into the shared script model."),
                    ("preferredForm", "rb.call(\"rimworld/list_colonists\", args)")),
                Obj(
                    ("pattern", "local count = rb.call(\"rimworld/list_colonists\", {}).result.count"),
                    ("whyItFails", "Host calls cannot be nested inside larger expressions. They must stand alone or be the sole right-hand side of an assignment or return."),
                    ("preferredForm", "local snapshot = rb.call(\"rimworld/list_colonists\", {})\nlocal count = snapshot.result.count")),
                Obj(
                    ("pattern", "rb.assert(condition, messageVar)"),
                    ("whyItFails", "rb.assert and rb.fail messages must be literal strings in v1."),
                    ("preferredForm", "rb.assert(condition, \"Expected condition to hold.\")")))),
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
                    FailureCode("lua.invalid_parameters", "The provided parameters payload could not be normalized into the read-only params binding."),
                    FailureCode("lua.compile_error", "Compilation failed before or during lowering."),
                    FailureCode("lua.syntax_error", "MoonSharp rejected the Lua source as invalid syntax."),
                    FailureCode("lua.unsupported_statement", "The Lua source used a statement not supported by rimbridge/run_lua v1."),
                    FailureCode("lua.unsupported_expression", "The Lua source used an expression not supported by rimbridge/run_lua v1."))))),
            ("runtimeResultShape", Obj(
                ("notes", Arr(
                    "run_lua returns the same top-level runtime shape as run_script: success, message, returned, result, error, output, and script.",
                    "run_lua_file returns that same shape and also includes scriptPath and resolvedScriptPath.",
                    "The nested script report follows the shared script runner model documented by rimbridge/get_script_reference.")),
                ("topLevelFields", Arr(
                    Field("success", "bool", required: true, defaultValue: null, description: "Overall Lua script success."),
                    Field("message", "string", required: true, defaultValue: null, description: "Success summary or top-level failure message."),
                    Field("returned", "bool", required: true, defaultValue: false, description: "True when the Lua script ended with return."),
                    Field("result", "object", required: false, defaultValue: null, description: "Value returned by the Lua script."),
                    Field("error", "object", required: false, defaultValue: null, description: "Top-level compile or runtime error."),
                    Field("scriptPath", "string", required: false, defaultValue: null, description: "Requested script path when using the file-backed tools."),
                    Field("resolvedScriptPath", "string", required: false, defaultValue: null, description: "Resolved on-disk path when using the file-backed tools."),
                    Field("output", "output[]", required: true, defaultValue: Arr(), description: "Structured trace rows emitted by print/rb.print."),
                    Field("script", "report", required: false, defaultValue: null, description: "Shared script runner report after successful compilation."))))),
            ("pollingGuidance", Obj(
                ("summary", "Preferred waiting model for run_lua v1: issue a mutating action once, then poll a read-only capability or explicit wait tool until bounded state conditions match."),
                ("bestPractices", Arr(
                    "Prefer rb.poll against a read-only capability that exposes the exact state you care about, such as rimbridge/get_bridge_status, rimworld/list_colonists, or rimworld/get_designator_state.",
                    "Use explicit wait tools such as rimbridge/wait_for_game_loaded or rimbridge/wait_for_long_event_idle when the bridge already exposes a bounded lifecycle wait.",
                    "Mutate first, then poll. Do not loop a mutating capability unless retries are part of the intended behavior.",
                    "Emit rb.print rows for the derived values or poll attempt counts that will matter when the script fails.",
                    "Keep conditions narrow and state-based. Prefer structured fields such as position, drafted, selectedDesignatorId, or inEntryScene over UI timing assumptions."
                )),
                ("patterns", Arr(
                    Obj(
                        ("name", "wait_for_entry_scene"),
                        ("description", "Poll bridge status until RimWorld is back at the main menu."),
                        ("preferredCapabilities", Arr("rimbridge/get_bridge_status")),
                        ("whenToUse", "Use when a script must confirm the entry scene before starting a new game or validating menu state."),
                        ("luaSource", bridgeStatePollingExample["luaSource"])
                    ),
                    Obj(
                        ("name", "wait_for_colonists_in_area"),
                        ("description", "Poll list_colonists until the expected pawns are inside a target area and have settled on compatible jobs."),
                        ("preferredCapabilities", Arr("rimworld/list_colonists")),
                        ("whenToUse", "Use after issuing movement or draft commands when the next step depends on pawn positions or jobs."),
                        ("luaSource", pollingExample["luaSource"])
                    ),
                    Obj(
                        ("name", "bounded_search_and_validate"),
                        ("description", "Use a bounded while loop for planning when one search call is not enough and each candidate must be validated."),
                        ("preferredCapabilities", Arr("rimworld/find_random_cell_near", "rimworld/apply_architect_designator")),
                        ("whenToUse", "Use for generic planning tasks such as finding enough space or validating a future footprint before mutating the map."),
                        ("luaSource", planningExample["luaSource"])
                    )
                ))
            )),
            ("authoringTips", Arr(
                "Call rimbridge/get_script_reference as well when you need the exact runtime condition model or lowered JSON result shape.",
                "Use rimbridge/compile_lua first when bringing up a new inline scenario so you can inspect the lowered JSON script.",
                "Use rimbridge/run_lua_file and rimbridge/compile_lua_file for reusable script fixtures that should live on disk instead of inside a tool call string.",
                "Prefer rb.print and rb.assert when building test-like scripts with explicit trace output and a clean failure boundary.",
                "Assign rb.poll(...) to a local when you need both the final result and step metadata such as attempts.",
                "If a first probe fails, simplify toward the starter template instead of widening the script shape. Most failures come from treating v1 as general-purpose Lua.",
                "Prefer state polling over event-dependent scripting in v1 unless a dedicated wait tool already exists for that lifecycle seam.")),
            ("examples", Arr(minimalExample, pollingExample, bridgeStatePollingExample, planningExample, foreachExample, paramsExample)));
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
