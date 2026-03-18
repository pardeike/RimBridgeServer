using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lib.GAB.Tools;

namespace RimBridgeServer;

public class RimBridgeTools
{
    [ReadmeTool("Bridge Diagnostics", "Connectivity test. Returns 'pong'.")]
    [Tool("rimbridge/ping", Description = "Connectivity test. Returns 'pong'.")]
    public object Ping()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Bridge Diagnostics", "Get basic information about the current RimWorld game")]
    [Tool("rimworld/get_game_info", Description = "Get basic information about the current RimWorld game")]
    public object GetGameInfo()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Bridge Diagnostics", "Get the latest retained journal snapshot for a specific operation id, including any bounded retained result payload")]
    [Tool("rimbridge/get_operation", Description = "Get the latest retained journal snapshot for a specific operation id, including any bounded retained result payload")]
    public object GetOperation([ToolParameter(Description = "Operation id returned in tool metadata")] string operationId)
    {
        return InvokeAlias(Arguments((nameof(operationId), operationId)));
    }

    [ReadmeTool("Bridge Diagnostics", "Get the current bridge and RimWorld state snapshot without mutating game state")]
    [Tool("rimbridge/get_bridge_status", Description = "Get the current bridge and RimWorld state snapshot without mutating game state")]
    public object GetBridgeStatus()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Bridge Diagnostics", "List registered bridge capabilities so an agent can discover the live bridge surface instead of relying on hardcoded tool knowledge")]
    [Tool("rimbridge/list_capabilities", Description = "List registered bridge capabilities so an agent can discover the live bridge surface instead of relying on hardcoded tool knowledge")]
    public object ListCapabilities(
        [ToolParameter(Description = "Maximum number of capabilities to return")] int limit = 200,
        [ToolParameter(Description = "Optional exact provider id filter")] string providerId = null,
        [ToolParameter(Description = "Optional category filter")] string category = null,
        [ToolParameter(Description = "Optional source filter: core, optional, or extension")] string source = null,
        [ToolParameter(Description = "Optional case-insensitive free-text query across ids, aliases, titles, summaries, and parameter names")] string query = null,
        [ToolParameter(Description = "Include parameter descriptors for each capability")] bool includeParameters = true)
    {
        return InvokeAlias(Arguments((nameof(limit), limit), (nameof(providerId), providerId), (nameof(category), category), (nameof(source), source), (nameof(query), query), (nameof(includeParameters), includeParameters)));
    }

    [ReadmeTool("Bridge Diagnostics", "Get one registered bridge capability descriptor by capability id or alias")]
    [Tool("rimbridge/get_capability", Description = "Get one registered bridge capability descriptor by capability id or alias")]
    public object GetCapability([ToolParameter(Description = "Capability id or alias such as rimworld/select_pawn")] string capabilityIdOrAlias)
    {
        return InvokeAlias(Arguments((nameof(capabilityIdOrAlias), capabilityIdOrAlias)));
    }

    [ReadmeTool("Bridge Diagnostics", "List recent bridge operations from the in-memory operation journal, optionally expanding retained result payloads")]
    [Tool("rimbridge/list_operations", Description = "List recent bridge operations from the in-memory operation journal, optionally expanding retained result payloads")]
    public object ListOperations(
        [ToolParameter(Description = "Maximum number of operations to return")] int limit = 20,
        [ToolParameter(Description = "Include bounded retained result payloads instead of summary-only operation envelopes")] bool includeResults = false)
    {
        return InvokeAlias(Arguments((nameof(limit), limit), (nameof(includeResults), includeResults)));
    }

    [ReadmeTool("Bridge Diagnostics", "List recent bridge operation lifecycle events from the in-memory event journal")]
    [Tool("rimbridge/list_operation_events", Description = "List recent bridge operation lifecycle events from the in-memory event journal")]
    public object ListOperationEvents(
        [ToolParameter(Description = "Maximum number of events to return")] int limit = 50,
        [ToolParameter(Description = "Optional event type filter, such as operation.failed")] string eventType = null,
        [ToolParameter(Description = "Only include events with a sequence greater than this cursor")] long afterSequence = 0,
        [ToolParameter(Description = "Optional exact operation id filter")] string operationId = null,
        [ToolParameter(Description = "Include diagnostic bridge operations such as status and journal reads")] bool includeDiagnostics = false)
    {
        return InvokeAlias(Arguments((nameof(limit), limit), (nameof(eventType), eventType), (nameof(afterSequence), afterSequence), (nameof(operationId), operationId), (nameof(includeDiagnostics), includeDiagnostics)));
    }

    [ReadmeTool("Bridge Diagnostics", "List recent captured RimWorld and bridge log entries from the in-memory log journal, including operation correlation when available")]
    [Tool("rimbridge/list_logs", Description = "List recent captured RimWorld and bridge log entries from the in-memory log journal, including operation correlation when available")]
    public object ListLogs(
        [ToolParameter(Description = "Maximum number of log entries to return")] int limit = 50,
        [ToolParameter(Description = "Minimum level to include: info, warning, error, or fatal")] string minimumLevel = "info",
        [ToolParameter(Description = "Only include log entries with a sequence greater than this cursor")] long afterSequence = 0,
        [ToolParameter(Description = "Optional direct operation id filter")] string operationId = null,
        [ToolParameter(Description = "Optional root operation id filter for grouped script runs or nested flows")] string rootOperationId = null,
        [ToolParameter(Description = "Optional exact capability id filter")] string capabilityId = null)
    {
        return InvokeAlias(Arguments((nameof(limit), limit), (nameof(minimumLevel), minimumLevel), (nameof(afterSequence), afterSequence), (nameof(operationId), operationId), (nameof(rootOperationId), rootOperationId), (nameof(capabilityId), capabilityId)));
    }

    [ReadmeTool("Bridge Diagnostics", "Wait for an operation in the journal to reach a terminal status")]
    [Tool("rimbridge/wait_for_operation", Description = "Wait for an operation in the journal to reach a terminal status")]
    public object WaitForOperation(
        [ToolParameter(Description = "Operation id returned in tool metadata")] string operationId,
        [ToolParameter(Description = "Maximum time to wait in milliseconds")] int timeoutMs = 10000,
        [ToolParameter(Description = "Polling interval in milliseconds")] int pollIntervalMs = 50)
    {
        return InvokeAlias(Arguments((nameof(operationId), operationId), (nameof(timeoutMs), timeoutMs), (nameof(pollIntervalMs), pollIntervalMs)));
    }

    [ReadmeTool("Bridge Diagnostics", "Wait until RimWorld has finished loading a playable game and, optionally, until screen fade is complete")]
    [Tool("rimbridge/wait_for_game_loaded", Description = "Wait until RimWorld has finished loading a playable game and, optionally, until screen fade is complete")]
    public object WaitForGameLoaded(
        [ToolParameter(Description = "Maximum time to wait in milliseconds")] int timeoutMs = 30000,
        [ToolParameter(Description = "Polling interval in milliseconds")] int pollIntervalMs = 100,
        [ToolParameter(Description = "Wait until RimWorld's screen fade has fully cleared")] bool waitForScreenFade = true,
        [ToolParameter(Description = "Pause the game before returning success if it is still running")] bool pauseIfNeeded = false)
    {
        return InvokeAlias(Arguments((nameof(timeoutMs), timeoutMs), (nameof(pollIntervalMs), pollIntervalMs), (nameof(waitForScreenFade), waitForScreenFade), (nameof(pauseIfNeeded), pauseIfNeeded)));
    }

    [ReadmeTool("Bridge Diagnostics", "Wait until RimWorld reports no long events in progress")]
    [Tool("rimbridge/wait_for_long_event_idle", Description = "Wait until RimWorld reports no long events in progress")]
    public object WaitForLongEventIdle(
        [ToolParameter(Description = "Maximum time to wait in milliseconds")] int timeoutMs = 30000,
        [ToolParameter(Description = "Polling interval in milliseconds")] int pollIntervalMs = 100)
    {
        return InvokeAlias(Arguments((nameof(timeoutMs), timeoutMs), (nameof(pollIntervalMs), pollIntervalMs)));
    }

    [ReadmeTool("Scripting", "Get a machine-readable authoring reference for rimbridge/run_script, including statement types, expressions, conditions, limits, and examples")]
    [Tool("rimbridge/get_script_reference", Description = "Get a machine-readable authoring reference for rimbridge/run_script, including statement types, expressions, conditions, limits, and examples")]
    public object GetScriptReference()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Scripting", "Get a machine-readable authoring reference for the lowered rimbridge/run_lua subset, including quick-start rules, common pitfalls, params binding, compile errors, limits, and examples")]
    [Tool("rimbridge/get_lua_reference", Description = "Get a machine-readable authoring reference for the lowered rimbridge/run_lua subset, including quick-start rules, common pitfalls, params binding, compile errors, limits, and examples")]
    public object GetLuaReference()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Scripting", "Execute a JSON script containing ordered capability calls and generic control statements; call rimbridge/get_script_reference for the machine-readable language reference")]
    [Tool("rimbridge/run_script", Description = "Execute a JSON script containing ordered capability calls and generic control statements; call rimbridge/get_script_reference for the machine-readable language reference")]
    public object RunScript(
        [ToolParameter(Description = "Structured JSON script. Call rimbridge/get_script_reference for the full machine-readable language reference. Example: {\"name\":\"setup\",\"continueOnError\":false,\"steps\":[{\"id\":\"pause\",\"call\":\"rimworld/pause_game\",\"arguments\":{\"pause\":true}}]}")] string scriptJson,
        [ToolParameter(Description = "Include each step's result payload in the returned script report")] bool includeStepResults = true)
    {
        return InvokeAlias(Arguments((nameof(scriptJson), scriptJson), (nameof(includeStepResults), includeStepResults)));
    }

    [ReadmeTool("Scripting", "Compile a small lowered Lua subset, not general-purpose Lua, into the shared script runner and execute it through the normal capability registry; start with rimbridge/get_lua_reference or rimbridge/compile_lua")]
    [Tool("rimbridge/run_lua", Description = "Compile a small lowered Lua subset, not general-purpose Lua, into the shared script runner and execute it through the normal capability registry; start with rimbridge/get_lua_reference or rimbridge/compile_lua")]
    public object RunLua(
        [ToolParameter(Description = "Lua source using the supported rimbridge/run_lua subset. Start with local bindings, rb.call/rb.poll, static field access, and static one-based indexes such as names[1]. Call rimbridge/get_lua_reference for the full machine-readable language reference and rimbridge/compile_lua to inspect the lowered JSON script.")] string luaSource,
        [ToolParameter(Description = "Optional object-style parameters exposed to the script as a read-only global params table. Example: {\"screenshotFileName\":\"demo_capture\",\"maxPlanningAttempts\":8}")] Dictionary<string, object> parameters = null,
        [ToolParameter(Description = "Include each successful call step's result payload in the returned script report")] bool includeStepResults = true)
    {
        return InvokeAlias(Arguments((nameof(luaSource), luaSource), (nameof(parameters), parameters), (nameof(includeStepResults), includeStepResults)));
    }

    [ReadmeTool("Scripting", "Load a .lua file, treat it as the same lowered Lua subset used by rimbridge/run_lua, and execute it through the shared script runner")]
    [Tool("rimbridge/run_lua_file", Description = "Load a .lua file, treat it as the same lowered Lua subset used by rimbridge/run_lua, and execute it through the shared script runner")]
    public object RunLuaFile(
        [ToolParameter(Description = "Absolute path or current-working-directory-relative path to a .lua file")] string scriptPath,
        [ToolParameter(Description = "Optional object-style parameters exposed to the script as a read-only global params table")] Dictionary<string, object> parameters = null,
        [ToolParameter(Description = "Include each successful call step's result payload in the returned script report")] bool includeStepResults = true)
    {
        return InvokeAlias(Arguments((nameof(scriptPath), scriptPath), (nameof(parameters), parameters), (nameof(includeStepResults), includeStepResults)));
    }

    [ReadmeTool("Scripting", "Compile the supported lowered Lua subset, not general-purpose Lua, into the JSON script model without executing capability calls; use this first for new script shapes")]
    [Tool("rimbridge/compile_lua", Description = "Compile the supported lowered Lua subset, not general-purpose Lua, into the JSON script model without executing capability calls; use this first for new script shapes")]
    public object CompileLua(
        [ToolParameter(Description = "Lua source using the supported rimbridge/run_lua subset. Prefer local bindings, rb.call/rb.poll, static field access, and static one-based indexes. Call rimbridge/get_lua_reference for the full machine-readable language reference.")] string luaSource,
        [ToolParameter(Description = "Optional object-style parameters exposed to the script as a read-only global params table")] Dictionary<string, object> parameters = null)
    {
        return InvokeAlias(Arguments((nameof(luaSource), luaSource), (nameof(parameters), parameters)));
    }

    [ReadmeTool("Scripting", "Load a .lua file and compile it as the same lowered Lua subset used by rimbridge/run_lua without executing capability calls")]
    [Tool("rimbridge/compile_lua_file", Description = "Load a .lua file and compile it as the same lowered Lua subset used by rimbridge/run_lua without executing capability calls")]
    public object CompileLuaFile(
        [ToolParameter(Description = "Absolute path or current-working-directory-relative path to a .lua file")] string scriptPath,
        [ToolParameter(Description = "Optional object-style parameters exposed to the script as a read-only global params table")] Dictionary<string, object> parameters = null)
    {
        return InvokeAlias(Arguments((nameof(scriptPath), scriptPath), (nameof(parameters), parameters)));
    }

    [ReadmeTool("Debug Actions And Mods", "Pause or unpause the game")]
    [Tool("rimworld/pause_game", Description = "Pause or unpause the game")]
    public object PauseGame([ToolParameter(Description = "True to pause, false to unpause")] bool pause = true)
    {
        return InvokeAlias(Arguments((nameof(pause), pause)));
    }

    [ReadmeTool("Debug Actions And Mods", "List top-level RimWorld debug action roots using stable internal debug-action paths")]
    [Tool("rimworld/list_debug_action_roots", Description = "List top-level RimWorld debug action roots using stable internal debug-action paths")]
    public object ListDebugActionRoots([ToolParameter(Description = "Include roots that are currently hidden in the active game state")] bool includeHidden = false)
    {
        return InvokeAlias(Arguments((nameof(includeHidden), includeHidden)));
    }

    [ReadmeTool("Debug Actions And Mods", "List direct children of a RimWorld debug action path")]
    [Tool("rimworld/list_debug_action_children", Description = "List direct children of a RimWorld debug action path")]
    public object ListDebugActionChildren(
        [ToolParameter(Description = "Stable debug action path returned by the discovery tools")] string path,
        [ToolParameter(Description = "Include child nodes that are currently hidden in the active game state")] bool includeHidden = false)
    {
        return InvokeAlias(Arguments((nameof(path), path), (nameof(includeHidden), includeHidden)));
    }

    [ReadmeTool("Debug Actions And Mods", "Search the full RimWorld debug-action tree globally by path, label, category, and source metadata so callers do not need to walk one subtree at a time")]
    [Tool("rimworld/search_debug_actions", Description = "Search the full RimWorld debug-action tree globally by path, label, category, and source metadata so callers do not need to walk one subtree at a time")]
    public object SearchDebugActions(
        [ToolParameter(Description = "Case-insensitive search text such as Toggle Job Logging or Log Job Details")] string query,
        [ToolParameter(Description = "Maximum number of matches to return")] int limit = 50,
        [ToolParameter(Description = "Include nodes that are currently hidden in the active game state")] bool includeHidden = false,
        [ToolParameter(Description = "Only return nodes whose execution metadata reports supported=true")] bool supportedOnly = false,
        [ToolParameter(Description = "Optional required target kind filter such as pawn")] string requiredTargetKind = null)
    {
        return InvokeAlias(Arguments((nameof(query), query), (nameof(limit), limit), (nameof(includeHidden), includeHidden), (nameof(supportedOnly), supportedOnly), (nameof(requiredTargetKind), requiredTargetKind)));
    }

    [ReadmeTool("Debug Actions And Mods", "Get metadata for one RimWorld debug action path and, optionally, its immediate children")]
    [Tool("rimworld/get_debug_action", Description = "Get metadata for one RimWorld debug action path and, optionally, its immediate children")]
    public object GetDebugAction(
        [ToolParameter(Description = "Stable debug action path returned by the discovery tools")] string path,
        [ToolParameter(Description = "Include immediate child nodes in the response")] bool includeChildren = true,
        [ToolParameter(Description = "Include hidden child nodes when includeChildren is true")] bool includeHiddenChildren = false)
    {
        return InvokeAlias(Arguments((nameof(path), path), (nameof(includeChildren), includeChildren), (nameof(includeHiddenChildren), includeHiddenChildren)));
    }

    [ReadmeTool("Debug Actions And Mods", "Execute a supported RimWorld debug action leaf by stable path, including pawn-target actions when pawnName or pawnId is provided")]
    [Tool("rimworld/execute_debug_action", Description = "Execute a supported RimWorld debug action leaf by stable path, including pawn-target actions when pawnName or pawnId is provided")]
    public object ExecuteDebugAction(
        [ToolParameter(Description = "Stable debug action path returned by the discovery tools")] string path,
        [ToolParameter(Description = "Optional current-map pawn name for ToolMapForPawns debug actions")] string pawnName = null,
        [ToolParameter(Description = "Optional stable current-map pawn id from rimworld/list_colonists for ToolMapForPawns debug actions")] string pawnId = null)
    {
        return InvokeAlias(Arguments((nameof(path), path), (nameof(pawnName), pawnName), (nameof(pawnId), pawnId)));
    }

    [ReadmeTool("Debug Actions And Mods", "Set a RimWorld debug setting toggle by stable path to a deterministic on/off state")]
    [Tool("rimworld/set_debug_setting", Description = "Set a RimWorld debug setting toggle by stable path to a deterministic on/off state")]
    public object SetDebugSetting(
        [ToolParameter(Description = "Stable debug setting path returned by the discovery tools")] string path,
        [ToolParameter(Description = "Desired enabled state")] bool enabled)
    {
        return InvokeAlias(Arguments((nameof(path), path), (nameof(enabled), enabled)));
    }

    [ReadmeTool("Debug Actions And Mods", "Deterministically enable or disable job-tracker logging for one current-map colonist and return a log cursor plus recommended rimbridge/list_logs arguments for consuming future job lines")]
    [Tool("rimworld/set_colonist_job_logging", Description = "Deterministically enable or disable job-tracker logging for one current-map colonist and return a log cursor plus recommended rimbridge/list_logs arguments for consuming future job lines")]
    public object SetColonistJobLogging(
        [ToolParameter(Description = "Optional current-map colonist name, short name, or full name")] string pawnName = null,
        [ToolParameter(Description = "Optional stable current-map colonist pawn id from rimworld/list_colonists")] string pawnId = null,
        [ToolParameter(Description = "Desired job-logging state")] bool enabled = true)
    {
        return InvokeAlias(Arguments((nameof(pawnName), pawnName), (nameof(pawnId), pawnId), (nameof(enabled), enabled)));
    }

    [ReadmeTool("Debug Actions And Mods", "List installed RimWorld mods, whether each one is enabled in the current configuration, and whether it matches the currently loaded session")]
    [Tool("rimworld/list_mods", Description = "List installed RimWorld mods, whether each one is enabled in the current configuration, and whether it matches the currently loaded session")]
    public object ListMods([ToolParameter(Description = "Include inactive installed mods as well as the active load order")] bool includeInactive = true)
    {
        return InvokeAlias(Arguments((nameof(includeInactive), includeInactive)));
    }

    [ReadmeTool("Debug Actions And Mods", "Read semantic mod-configuration status for the current active load order, including warnings, ordering issues, and whether a restart is required to match the loaded session")]
    [Tool("rimworld/get_mod_configuration_status", Description = "Read semantic mod-configuration status for the current active load order, including warnings, ordering issues, and whether a restart is required to match the loaded session")]
    public object GetModConfigurationStatus()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Debug Actions And Mods", "Enable or disable one installed mod by stable mod id, package id, name, or folder name, optionally persisting the updated ModsConfig.xml immediately")]
    [Tool("rimworld/set_mod_enabled", Description = "Enable or disable one installed mod by stable mod id, package id, name, or folder name, optionally persisting the updated ModsConfig.xml immediately")]
    public object SetModEnabled(
        [ToolParameter(Description = "Stable modId from rimworld/list_mods, or an exact package id / package id (player-facing) / name / folder name match")] string modId,
        [ToolParameter(Description = "True to enable the mod in the current configuration, false to disable it")] bool enabled,
        [ToolParameter(Description = "True to persist the updated active-mod list to ModsConfig.xml immediately")] bool save = true,
        [ToolParameter(Description = "Allow disabling the core RimWorld mod. Defaults to false as a safety guard.")] bool allowDisableCore = false)
    {
        return InvokeAlias(Arguments((nameof(modId), modId), (nameof(enabled), enabled), (nameof(save), save), (nameof(allowDisableCore), allowDisableCore)));
    }

    [ReadmeTool("Debug Actions And Mods", "Move one currently enabled mod to a new zero-based active load-order index, optionally persisting the updated ModsConfig.xml immediately")]
    [Tool("rimworld/reorder_mod", Description = "Move one currently enabled mod to a new zero-based active load-order index, optionally persisting the updated ModsConfig.xml immediately")]
    public object ReorderMod(
        [ToolParameter(Description = "Stable modId from rimworld/list_mods, or an exact package id / package id (player-facing) / name / folder name match")] string modId,
        [ToolParameter(Description = "Desired zero-based index within the current active mod load order")] int targetIndex,
        [ToolParameter(Description = "True to persist the updated active-mod order to ModsConfig.xml immediately")] bool save = true)
    {
        return InvokeAlias(Arguments((nameof(modId), modId), (nameof(targetIndex), targetIndex), (nameof(save), save)));
    }

    [ReadmeTool("Debug Actions And Mods", "List loaded mod handles that expose a settings dialog, a persistent ModSettings state object, or both")]
    [Tool("rimworld/list_mod_settings_surfaces", Description = "List loaded mod handles that expose a settings dialog, a persistent ModSettings state object, or both")]
    public object ListModSettingsSurfaces([ToolParameter(Description = "Include loaded mod handles even when they expose neither a settings window nor a ModSettings object")] bool includeWithoutSettings = false)
    {
        return InvokeAlias(Arguments((nameof(includeWithoutSettings), includeWithoutSettings)));
    }

    [ReadmeTool("Debug Actions And Mods", "Read one loaded mod's semantic ModSettings object by stable mod id, package id, settings category, or handle type name")]
    [Tool("rimworld/get_mod_settings", Description = "Read one loaded mod's semantic ModSettings object by stable mod id, package id, settings category, or handle type name")]
    public object GetModSettings(
        [ToolParameter(Description = "Stable modId from rimworld/list_mod_settings_surfaces, or an exact package id / settings category / handle type match")] string modId,
        [ToolParameter(Description = "Maximum object depth to traverse when describing nested settings")] int maxDepth = 4,
        [ToolParameter(Description = "Maximum number of children to return for any one list or dictionary node")] int maxCollectionEntries = 32)
    {
        return InvokeAlias(Arguments((nameof(modId), modId), (nameof(maxDepth), maxDepth), (nameof(maxCollectionEntries), maxCollectionEntries)));
    }

    [ReadmeTool("Debug Actions And Mods", "Apply one or more field-path updates to a loaded mod's ModSettings object, with optional immediate persistence through Mod.WriteSettings()")]
    [Tool("rimworld/update_mod_settings", Description = "Apply one or more field-path updates to a loaded mod's ModSettings object, with optional immediate persistence through Mod.WriteSettings()")]
    public object UpdateModSettings(
        [ToolParameter(Description = "Stable modId from rimworld/list_mod_settings_surfaces, or an exact package id / settings category / handle type match")] string modId,
        [ToolParameter(Description = "Object mapping field paths such as SemanticHarnessSmokeToggle or Nested.List[0] to desired values")] Dictionary<string, object> values,
        [ToolParameter(Description = "True to persist through Mod.WriteSettings() after applying the updates")] bool write = true,
        [ToolParameter(Description = "Maximum object depth to traverse when returning the updated settings snapshot")] int maxDepth = 4,
        [ToolParameter(Description = "Maximum number of children to return for any one list or dictionary node in the updated snapshot")] int maxCollectionEntries = 32)
    {
        return InvokeAlias(Arguments((nameof(modId), modId), (nameof(values), values), (nameof(write), write), (nameof(maxDepth), maxDepth), (nameof(maxCollectionEntries), maxCollectionEntries)));
    }

    [ReadmeTool("Debug Actions And Mods", "Reload a loaded mod's ModSettings object from disk, discarding unsaved in-memory changes")]
    [Tool("rimworld/reload_mod_settings", Description = "Reload a loaded mod's ModSettings object from disk, discarding unsaved in-memory changes")]
    public object ReloadModSettings(
        [ToolParameter(Description = "Stable modId from rimworld/list_mod_settings_surfaces, or an exact package id / settings category / handle type match")] string modId,
        [ToolParameter(Description = "Maximum object depth to traverse when returning the reloaded settings snapshot")] int maxDepth = 4,
        [ToolParameter(Description = "Maximum number of children to return for any one list or dictionary node in the reloaded snapshot")] int maxCollectionEntries = 32)
    {
        return InvokeAlias(Arguments((nameof(modId), modId), (nameof(maxDepth), maxDepth), (nameof(maxCollectionEntries), maxCollectionEntries)));
    }

    [ReadmeTool("Debug Actions And Mods", "Open RimWorld's native Dialog_ModSettings window for one loaded mod without foreground-dependent input")]
    [Tool("rimworld/open_mod_settings", Description = "Open RimWorld's native Dialog_ModSettings window for one loaded mod without foreground-dependent input")]
    public object OpenModSettings(
        [ToolParameter(Description = "Stable modId from rimworld/list_mod_settings_surfaces, or an exact package id / settings category / handle type match")] string modId,
        [ToolParameter(Description = "Close any currently open mod-settings dialogs before opening the requested one")] bool replaceExisting = true)
    {
        return InvokeAlias(Arguments((nameof(modId), modId), (nameof(replaceExisting), replaceExisting)));
    }

    [ReadmeTool("Architect And Map State", "Get the current Architect/designator selection state, including god mode and the selected designator")]
    [Tool("rimworld/get_designator_state", Description = "Get the current Architect/designator selection state, including god mode and the selected designator")]
    public object GetDesignatorState()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Architect And Map State", "Enable or disable RimWorld god mode deterministically")]
    [Tool("rimworld/set_god_mode", Description = "Enable or disable RimWorld god mode deterministically")]
    public object SetGodMode([ToolParameter(Description = "True to enable god mode, false to disable it")] bool enabled = true)
    {
        return InvokeAlias(Arguments((nameof(enabled), enabled)));
    }

    [ReadmeTool("Architect And Map State", "List RimWorld Architect categories using stable category ids")]
    [Tool("rimworld/list_architect_categories", Description = "List RimWorld Architect categories using stable category ids")]
    public object ListArchitectCategories(
        [ToolParameter(Description = "Include categories that are currently hidden")] bool includeHidden = false,
        [ToolParameter(Description = "Include categories even if they currently expose no designators")] bool includeEmpty = false)
    {
        return InvokeAlias(Arguments((nameof(includeHidden), includeHidden), (nameof(includeEmpty), includeEmpty)));
    }

    [ReadmeTool("Architect And Map State", "List Architect designators for one category, flattening dropdown widgets into actionable child designators")]
    [Tool("rimworld/list_architect_designators", Description = "List Architect designators for one category, flattening dropdown widgets into actionable child designators")]
    public object ListArchitectDesignators(
        [ToolParameter(Description = "Stable category id from rimworld/list_architect_categories or the raw category defName")] string categoryId,
        [ToolParameter(Description = "Include designators that are currently hidden")] bool includeHidden = false)
    {
        return InvokeAlias(Arguments((nameof(categoryId), categoryId), (nameof(includeHidden), includeHidden)));
    }

    [ReadmeTool("Architect And Map State", "Select an Architect designator by stable id without relying on foreground UI interaction")]
    [Tool("rimworld/select_architect_designator", Description = "Select an Architect designator by stable id without relying on foreground UI interaction")]
    public object SelectArchitectDesignator([ToolParameter(Description = "Stable designator id returned by rimworld/list_architect_designators")] string designatorId)
    {
        return InvokeAlias(Arguments((nameof(designatorId), designatorId)));
    }

    [ReadmeTool("Architect And Map State", "Apply an Architect designator to one cell or a rectangle, with optional dry-run validation")]
    [Tool("rimworld/apply_architect_designator", Description = "Apply an Architect designator to one cell or a rectangle, with optional dry-run validation")]
    public object ApplyArchitectDesignator(
        [ToolParameter(Description = "Stable designator id returned by rimworld/list_architect_designators")] string designatorId,
        [ToolParameter(Description = "Target cell x coordinate")] int x,
        [ToolParameter(Description = "Target cell z coordinate")] int z,
        [ToolParameter(Description = "Rectangle width in cells starting at x/z")] int width = 1,
        [ToolParameter(Description = "Rectangle height in cells starting at x/z")] int height = 1,
        [ToolParameter(Description = "Validate placement without mutating the map")] bool dryRun = false,
        [ToolParameter(Description = "Keep the designator selected after the call completes")] bool keepSelected = true)
    {
        return InvokeAlias(Arguments((nameof(designatorId), designatorId), (nameof(x), x), (nameof(z), z), (nameof(width), width), (nameof(height), height), (nameof(dryRun), dryRun), (nameof(keepSelected), keepSelected)));
    }

    [ReadmeTool("Architect And Map State", "List current-map zones such as stockpiles and growing zones")]
    [Tool("rimworld/list_zones", Description = "List current-map zones such as stockpiles and growing zones")]
    public object ListZones(
        [ToolParameter(Description = "Include hidden zones")] bool includeHidden = false,
        [ToolParameter(Description = "Include zones with zero cells")] bool includeEmpty = false)
    {
        return InvokeAlias(Arguments((nameof(includeHidden), includeHidden), (nameof(includeEmpty), includeEmpty)));
    }

    [ReadmeTool("Architect And Map State", "List current-map areas such as home, roof, snow-clear, and allowed areas")]
    [Tool("rimworld/list_areas", Description = "List current-map areas such as home, roof, snow-clear, and allowed areas")]
    public object ListAreas(
        [ToolParameter(Description = "Include areas with zero cells")] bool includeEmpty = false,
        [ToolParameter(Description = "Include only areas that can be assigned as allowed areas")] bool includeAssignableOnly = false)
    {
        return InvokeAlias(Arguments((nameof(includeEmpty), includeEmpty), (nameof(includeAssignableOnly), includeAssignableOnly)));
    }

    [ReadmeTool("Architect And Map State", "Create a new allowed area and optionally make it the selected allowed-area target")]
    [Tool("rimworld/create_allowed_area", Description = "Create a new allowed area and optionally make it the selected allowed-area target")]
    public object CreateAllowedArea(
        [ToolParameter(Description = "Optional label for the new allowed area")] string label = null,
        [ToolParameter(Description = "Select the new area as the current allowed-area target")] bool select = true)
    {
        return InvokeAlias(Arguments((nameof(label), label), (nameof(select), select)));
    }

    [ReadmeTool("Architect And Map State", "Select an allowed area by id for area-designator flows, or clear the selection when areaId is omitted")]
    [Tool("rimworld/select_allowed_area", Description = "Select an allowed area by id for area-designator flows, or clear the selection when areaId is omitted")]
    public object SelectAllowedArea([ToolParameter(Description = "Area id from rimworld/list_areas, or omit to clear the selected allowed area")] string areaId = null)
    {
        return InvokeAlias(Arguments((nameof(areaId), areaId)));
    }

    [ReadmeTool("Architect And Map State", "Set or clear the explicit existing-zone target on a zone-add designator")]
    [Tool("rimworld/set_zone_target", Description = "Set or clear the explicit existing-zone target on a zone-add designator")]
    public object SetZoneTarget(
        [ToolParameter(Description = "Stable zone-add designator id returned by rimworld/list_architect_designators")] string designatorId,
        [ToolParameter(Description = "Zone id from rimworld/list_zones, or omit to clear the explicit zone target")] string zoneId = null)
    {
        return InvokeAlias(Arguments((nameof(designatorId), designatorId), (nameof(zoneId), zoneId)));
    }

    [ReadmeTool("Architect And Map State", "Clear all cells from a mutable area such as a custom allowed area")]
    [Tool("rimworld/clear_area", Description = "Clear all cells from a mutable area such as a custom allowed area")]
    public object ClearArea([ToolParameter(Description = "Area id from rimworld/list_areas")] string areaId)
    {
        return InvokeAlias(Arguments((nameof(areaId), areaId)));
    }

    [ReadmeTool("Architect And Map State", "Delete a mutable area such as a custom allowed area")]
    [Tool("rimworld/delete_area", Description = "Delete a mutable area such as a custom allowed area")]
    public object DeleteArea([ToolParameter(Description = "Area id from rimworld/list_areas")] string areaId)
    {
        return InvokeAlias(Arguments((nameof(areaId), areaId)));
    }

    [ReadmeTool("Architect And Map State", "Delete an existing zone by id")]
    [Tool("rimworld/delete_zone", Description = "Delete an existing zone by id")]
    public object DeleteZone([ToolParameter(Description = "Zone id from rimworld/list_zones")] string zoneId)
    {
        return InvokeAlias(Arguments((nameof(zoneId), zoneId)));
    }

    [ReadmeTool("Architect And Map State", "Inspect one map cell, including things, blueprints, frames, designations, zones, and areas")]
    [Tool("rimworld/get_cell_info", Description = "Inspect one map cell, including things, blueprints, frames, designations, zones, and areas")]
    public object GetCellInfo(
        [ToolParameter(Description = "Target cell x coordinate")] int x,
        [ToolParameter(Description = "Target cell z coordinate")] int z)
    {
        return InvokeAlias(Arguments((nameof(x), x), (nameof(z), z)));
    }

    [ReadmeTool("Architect And Map State", "Use RimWorld's expanding-radius random cell search to find a nearby cell or footprint that satisfies generic map criteria")]
    [Tool("rimworld/find_random_cell_near", Description = "Use RimWorld's expanding-radius random cell search to find a nearby cell or footprint that satisfies generic map criteria")]
    public object FindRandomCellNear(
        [ToolParameter(Description = "Origin cell x coordinate")] int x,
        [ToolParameter(Description = "Origin cell z coordinate")] int z,
        [ToolParameter(Description = "Initial search radius in cells")] int startingSearchRadius = 5,
        [ToolParameter(Description = "Maximum search radius in cells")] int maxSearchRadius = 60,
        [ToolParameter(Description = "Footprint width to validate at each candidate cell")] int width = 1,
        [ToolParameter(Description = "Footprint height to validate at each candidate cell")] int height = 1,
        [ToolParameter(Description = "Interpret the candidate cell as the footprint's top_left or center anchor")] string footprintAnchor = "top_left",
        [ToolParameter(Description = "Require every footprint cell to be walkable")] bool requireWalkable = false,
        [ToolParameter(Description = "Require every footprint cell to be standable")] bool requireStandable = false,
        [ToolParameter(Description = "Require every footprint cell to be unfogged")] bool requireNotFogged = false,
        [ToolParameter(Description = "Reject footprint cells containing impassable things such as walls or solid rocks")] bool requireNoImpassableThings = false,
        [ToolParameter(Description = "Optional current-map pawn name; when provided, the returned anchor cell must be reachable by that pawn")] string reachablePawnName = null,
        [ToolParameter(Description = "Optional stable current-map pawn id from rimworld/list_colonists; when provided, the returned anchor cell must be reachable by that pawn")] string reachablePawnId = null,
        [ToolParameter(Description = "Optional architect designator id; when provided, every footprint cell must pass that designator's CanDesignateCell validation")] string designatorId = null)
    {
        return InvokeAlias(Arguments(
            (nameof(x), x),
            (nameof(z), z),
            (nameof(startingSearchRadius), startingSearchRadius),
            (nameof(maxSearchRadius), maxSearchRadius),
            (nameof(width), width),
            (nameof(height), height),
            (nameof(footprintAnchor), footprintAnchor),
            (nameof(requireWalkable), requireWalkable),
            (nameof(requireStandable), requireStandable),
            (nameof(requireNotFogged), requireNotFogged),
            (nameof(requireNoImpassableThings), requireNoImpassableThings),
            (nameof(reachablePawnName), reachablePawnName),
            (nameof(reachablePawnId), reachablePawnId),
            (nameof(designatorId), designatorId)));
    }

    [ReadmeTool("Architect And Map State", "Analyze a contiguous area from one root cell using RimWorld's generic cell flood-fill algorithm and the same reusable footprint criteria as find_random_cell_near")]
    [Tool("rimworld/flood_fill_cells", Description = "Analyze a contiguous area from one root cell using RimWorld's generic cell flood-fill algorithm and the same reusable footprint criteria as find_random_cell_near")]
    public object FloodFillCells(
        [ToolParameter(Description = "Root cell x coordinate")] int x,
        [ToolParameter(Description = "Root cell z coordinate")] int z,
        [ToolParameter(Description = "Maximum number of cells to process before stopping")] int maxCellsToProcess = 256,
        [ToolParameter(Description = "Optional minimum contiguous cell count to satisfy before stopping early")] int minimumCellCount = 0,
        [ToolParameter(Description = "Maximum number of matching cells to include in the returned sample list")] int maxReturnedCells = 64,
        [ToolParameter(Description = "Footprint width to validate at each visited cell")] int width = 1,
        [ToolParameter(Description = "Footprint height to validate at each visited cell")] int height = 1,
        [ToolParameter(Description = "Interpret each visited cell as the footprint's top_left or center anchor")] string footprintAnchor = "top_left",
        [ToolParameter(Description = "Require every footprint cell to be walkable")] bool requireWalkable = false,
        [ToolParameter(Description = "Require every footprint cell to be standable")] bool requireStandable = false,
        [ToolParameter(Description = "Require every footprint cell to be unfogged")] bool requireNotFogged = false,
        [ToolParameter(Description = "Reject footprint cells containing impassable things such as walls or solid rocks")] bool requireNoImpassableThings = false,
        [ToolParameter(Description = "Optional current-map pawn name; when provided, each visited anchor cell must be reachable by that pawn")] string reachablePawnName = null,
        [ToolParameter(Description = "Optional stable current-map pawn id from rimworld/list_colonists; when provided, each visited anchor cell must be reachable by that pawn")] string reachablePawnId = null,
        [ToolParameter(Description = "Optional architect designator id; when provided, every footprint cell must pass that designator's CanDesignateCell validation")] string designatorId = null)
    {
        return InvokeAlias(Arguments(
            (nameof(x), x),
            (nameof(z), z),
            (nameof(maxCellsToProcess), maxCellsToProcess),
            (nameof(minimumCellCount), minimumCellCount),
            (nameof(maxReturnedCells), maxReturnedCells),
            (nameof(width), width),
            (nameof(height), height),
            (nameof(footprintAnchor), footprintAnchor),
            (nameof(requireWalkable), requireWalkable),
            (nameof(requireStandable), requireStandable),
            (nameof(requireNotFogged), requireNotFogged),
            (nameof(requireNoImpassableThings), requireNoImpassableThings),
            (nameof(reachablePawnName), reachablePawnName),
            (nameof(reachablePawnId), reachablePawnId),
            (nameof(designatorId), designatorId)));
    }

    [ReadmeTool("UI And Input", "Get the current RimWorld window stack and input state for background-safe UI automation")]
    [Tool("rimworld/get_ui_state", Description = "Get the current RimWorld window stack and input state for background-safe UI automation")]
    public object GetUiState()
    {
        return InvokeAlias();
    }

    [ReadmeTool("UI And Input", "List RimWorld main tabs such as Work, Assign, Research, and mod-provided tabs with stable main-tab target ids")]
    [Tool("rimworld/list_main_tabs", Description = "List RimWorld main tabs such as Work, Assign, Research, and mod-provided tabs with stable main-tab target ids")]
    public object ListMainTabs([ToolParameter(Description = "Include tabs whose worker currently reports them as hidden or unavailable")] bool includeHidden = false)
    {
        return InvokeAlias(Arguments((nameof(includeHidden), includeHidden)));
    }

    [ReadmeTool("UI And Input", "Open one RimWorld main tab by stable target id, defName, label, or tab window type")]
    [Tool("rimworld/open_main_tab", Description = "Open one RimWorld main tab by stable target id, defName, label, or tab window type")]
    public object OpenMainTab([ToolParameter(Description = "Stable main-tab target id from rimworld/list_main_tabs, or an exact defName / label / tab window type match")] string mainTabId)
    {
        return InvokeAlias(Arguments((nameof(mainTabId), mainTabId)));
    }

    [ReadmeTool("UI And Input", "Close the currently open RimWorld main tab, optionally asserting which tab is open first")]
    [Tool("rimworld/close_main_tab", Description = "Close the currently open RimWorld main tab, optionally asserting which tab is open first")]
    public object CloseMainTab([ToolParameter(Description = "Optional stable main-tab target id or exact defName / label / tab window type expected to be open")] string mainTabId = null)
    {
        return InvokeAlias(Arguments((nameof(mainTabId), mainTabId)));
    }

    [ReadmeTool("UI And Input", "Capture a generic structured layout snapshot of the current dialogs, windows, or main tabs, including actionable control target ids")]
    [Tool("rimworld/get_ui_layout", Description = "Capture a generic structured layout snapshot of the current dialogs, windows, or main tabs, including actionable control target ids")]
    public object GetUiLayout(
        [ToolParameter(Description = "Optional surface target id such as a window target from rimworld/get_screen_targets or a main-tab target from rimworld/list_main_tabs")] string surfaceId = null,
        [ToolParameter(Description = "Maximum time to wait for the requested UI surface to draw on screen")] int timeoutMs = 2000)
    {
        return InvokeAlias(Arguments((nameof(surfaceId), surfaceId), (nameof(timeoutMs), timeoutMs)));
    }

    [ReadmeTool("UI And Input", "Activate an actionable UI control target returned by rimworld/get_ui_layout on the next real draw frame")]
    [Tool("rimworld/click_ui_target", Description = "Activate an actionable UI control target returned by rimworld/get_ui_layout on the next real draw frame")]
    public object ClickUiTarget(
        [ToolParameter(Description = "Actionable ui-element target id returned by rimworld/get_ui_layout")] string targetId,
        [ToolParameter(Description = "Maximum time to wait for the target control to be redrawn so the click can be injected")] int timeoutMs = 2000)
    {
        return InvokeAlias(Arguments((nameof(targetId), targetId), (nameof(timeoutMs), timeoutMs)));
    }

    [ReadmeTool("UI And Input", "Set a persistent virtual hover target for UI review and screenshots, using either an actionable ui-element target id or a current-map cell, pawn, or thing")]
    [Tool("rimworld/set_hover_target", Description = "Set a persistent virtual hover target for UI review and screenshots, using either an actionable ui-element target id or a current-map cell, pawn, or thing")]
    public object SetHoverTarget(
        [ToolParameter(Description = "Optional actionable ui-element target id returned by rimworld/get_ui_layout")] string targetId = null,
        [ToolParameter(Description = "Current-map cell x coordinate when hovering a map cell")] int? x = null,
        [ToolParameter(Description = "Current-map cell z coordinate when hovering a map cell")] int? z = null,
        [ToolParameter(Description = "Stable current-map thing id when hovering a spawned thing")] string thingId = null,
        [ToolParameter(Description = "Optional current-map pawn name when hovering a pawn")] string pawnName = null,
        [ToolParameter(Description = "Optional stable current-map pawn id when hovering a pawn")] string pawnId = null)
    {
        return InvokeAlias(Arguments((nameof(targetId), targetId), (nameof(x), x), (nameof(z), z), (nameof(thingId), thingId), (nameof(pawnName), pawnName), (nameof(pawnId), pawnId)));
    }

    [ReadmeTool("UI And Input", "Clear the current virtual hover target so screenshots and mouseover-driven UI return to the real cursor state")]
    [Tool("rimworld/clear_hover_target", Description = "Clear the current virtual hover target so screenshots and mouseover-driven UI return to the real cursor state")]
    public object ClearHoverTarget()
    {
        return InvokeAlias();
    }

    [ReadmeTool("UI And Input", "Send semantic accept input to the active RimWorld window stack without requiring OS focus")]
    [Tool("rimworld/press_accept", Description = "Send semantic accept input to the active RimWorld window stack without requiring OS focus")]
    public object PressAccept()
    {
        return InvokeAlias();
    }

    [ReadmeTool("UI And Input", "List installed RimWorld languages, including a recommended ASCII-safe switch query for each language and the currently active language")]
    [Tool("rimworld/list_languages", Description = "List installed RimWorld languages, including a recommended ASCII-safe switch query for each language and the currently active language")]
    public object ListLanguages()
    {
        return InvokeAlias();
    }

    [ReadmeTool("UI And Input", "Send semantic cancel input to the active RimWorld window stack without requiring OS focus")]
    [Tool("rimworld/press_cancel", Description = "Send semantic cancel input to the active RimWorld window stack without requiring OS focus")]
    public object PressCancel()
    {
        return InvokeAlias();
    }

    [ReadmeTool("UI And Input", "Close an open RimWorld window by type name or, if omitted, the topmost window")]
    [Tool("rimworld/close_window", Description = "Close an open RimWorld window by type name or, if omitted, the topmost window")]
    public object CloseWindow([ToolParameter(Description = "Optional short or full .NET type name of the target window")] string windowType = null)
    {
        return InvokeAlias(Arguments((nameof(windowType), windowType)));
    }

    [ReadmeTool("UI And Input", "Open a RimWorld window by short or full .NET type name when the window exposes a public parameterless constructor")]
    [Tool("rimworld/open_window_by_type", Description = "Open a RimWorld window by short or full .NET type name when the window exposes a public parameterless constructor")]
    public object OpenWindowByType(
        [ToolParameter(Description = "Short or full .NET type name for a Verse.Window subtype, such as AchtungMod.SettingsToggles")] string windowType,
        [ToolParameter(Description = "Close already-open windows of the same type before opening a fresh instance")] bool replaceExisting = true)
    {
        return InvokeAlias(Arguments((nameof(windowType), windowType), (nameof(replaceExisting), replaceExisting)));
    }

    [ReadmeTool("UI And Input", "Semantically click a known actionable target id returned by rimworld/get_screen_targets without requiring OS focus")]
    [Tool("rimworld/click_screen_target", Description = "Semantically click a known actionable target id returned by rimworld/get_screen_targets without requiring OS focus")]
    public object ClickScreenTarget([ToolParameter(Description = "Actionable target id such as a context-menu option target or window dismiss target")] string targetId)
    {
        return InvokeAlias(Arguments((nameof(targetId), targetId)));
    }

    [ReadmeTool("UI And Input", "Switch RimWorld to an installed language by the recommendedQuery from rimworld/list_languages or another exact language name match, mirroring the main-menu language picker and saving prefs")]
    [Tool("rimworld/switch_language", Description = "Switch RimWorld to an installed language by the recommendedQuery from rimworld/list_languages or another exact language name match, mirroring the main-menu language picker and saving prefs")]
    public object SwitchLanguage([ToolParameter(Description = "Prefer the recommendedQuery from rimworld/list_languages; exact language ids and exact display/native/English names also work")] string language)
    {
        return InvokeAlias(Arguments((nameof(language), language)));
    }

    [ReadmeTool("UI And Input", "Start RimWorld's built-in quick test colony from the main menu")]
    [Tool("rimworld/start_debug_game", Description = "Start RimWorld's built-in quick test colony from the main menu")]
    public object StartDebugGame()
    {
        return InvokeAlias();
    }

    [ReadmeTool("UI And Input", "Return to the RimWorld main menu entry scene, or no-op if already there")]
    [Tool("rimworld/go_to_main_menu", Description = "Return to the RimWorld main menu entry scene, or no-op if already there")]
    public object GoToMainMenu()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Selection And Colony State", "List player-controlled colonists available for selection and drafting, including stable pawn ids")]
    [Tool("rimworld/list_colonists", Description = "List player-controlled colonists available for selection and drafting, including stable pawn ids")]
    public object ListColonists([ToolParameter(Description = "True to only include the current map")] bool currentMapOnly = false)
    {
        return InvokeAlias(Arguments((nameof(currentMapOnly), currentMapOnly)));
    }

    [ReadmeTool("Selection And Colony State", "Clear the current map selection")]
    [Tool("rimworld/clear_selection", Description = "Clear the current map selection")]
    public object ClearSelection()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Selection And Colony State", "Select a single colonist by name or stable pawn id")]
    [Tool("rimworld/select_pawn", Description = "Select a single colonist by name or stable pawn id")]
    public object SelectPawn(
        [ToolParameter(Description = "Optional colonist name, short name, or full name")] string pawnName = null,
        [ToolParameter(Description = "Optional stable colonist pawn id from rimworld/list_colonists")] string pawnId = null,
        [ToolParameter(Description = "True to append to the current selection instead of replacing it")] bool append = false)
    {
        return InvokeAlias(Arguments((nameof(pawnName), pawnName), (nameof(pawnId), pawnId), (nameof(append), append)));
    }

    [ReadmeTool("Selection And Colony State", "Deselect a single selected pawn by name or stable pawn id")]
    [Tool("rimworld/deselect_pawn", Description = "Deselect a single selected pawn by name or stable pawn id")]
    public object DeselectPawn(
        [ToolParameter(Description = "Optional selected pawn name")] string pawnName = null,
        [ToolParameter(Description = "Optional stable selected pawn id from rimworld/list_colonists")] string pawnId = null)
    {
        return InvokeAlias(Arguments((nameof(pawnName), pawnName), (nameof(pawnId), pawnId)));
    }

    [ReadmeTool("Selection And Colony State", "Draft or undraft a colonist by name or stable pawn id")]
    [Tool("rimworld/set_draft", Description = "Draft or undraft a colonist by name or stable pawn id")]
    public object SetDraft(
        [ToolParameter(Description = "Optional colonist name")] string pawnName = null,
        [ToolParameter(Description = "Optional stable colonist pawn id from rimworld/list_colonists")] string pawnId = null,
        [ToolParameter(Description = "True to draft, false to undraft")] bool drafted = true)
    {
        return InvokeAlias(Arguments((nameof(pawnName), pawnName), (nameof(pawnId), pawnId), (nameof(drafted), drafted)));
    }

    [ReadmeTool("Selection Semantics And Notifications", "Get structured details for the current selection, including inspect strings, inspect-tab types, and the current selection fingerprint")]
    [Tool("rimworld/get_selection_semantics", Description = "Get structured details for the current selection, including inspect strings, inspect-tab types, and the current selection fingerprint")]
    public object GetSelectionSemantics()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Selection Semantics And Notifications", "List the current selection's actionable grouped gizmos using deterministic selection-scoped gizmo ids")]
    [Tool("rimworld/list_selected_gizmos", Description = "List the current selection's actionable grouped gizmos using deterministic selection-scoped gizmo ids")]
    public object ListSelectedGizmos()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Selection Semantics And Notifications", "Execute one grouped gizmo for the current selection by gizmo id returned from rimworld/list_selected_gizmos")]
    [Tool("rimworld/execute_gizmo", Description = "Execute one grouped gizmo for the current selection by gizmo id returned from rimworld/list_selected_gizmos")]
    public object ExecuteGizmo([ToolParameter(Description = "Selection-scoped gizmo id returned by rimworld/list_selected_gizmos")] string gizmoId)
    {
        return InvokeAlias(Arguments((nameof(gizmoId), gizmoId)));
    }

    [ReadmeTool("Selection Semantics And Notifications", "List live RimWorld messages with native message ids and structured look-target metadata")]
    [Tool("rimworld/list_messages", Description = "List live RimWorld messages with native message ids and structured look-target metadata")]
    public object ListMessages([ToolParameter(Description = "Maximum number of live messages to return")] int limit = 12)
    {
        return InvokeAlias(Arguments((nameof(limit), limit)));
    }

    [ReadmeTool("Selection Semantics And Notifications", "List current letter-stack entries with native letter ids, semantic letter content, and structured look-target metadata")]
    [Tool("rimworld/list_letters", Description = "List current letter-stack entries with native letter ids, semantic letter content, and structured look-target metadata")]
    public object ListLetters([ToolParameter(Description = "Maximum number of letters to return")] int limit = 40)
    {
        return InvokeAlias(Arguments((nameof(limit), limit)));
    }

    [ReadmeTool("Selection Semantics And Notifications", "Open a specific letter by native letter id, mirroring a normal left-click on the letter stack entry")]
    [Tool("rimworld/open_letter", Description = "Open a specific letter by native letter id, mirroring a normal left-click on the letter stack entry")]
    public object OpenLetter([ToolParameter(Description = "Letter id returned by rimworld/list_letters")] string letterId)
    {
        return InvokeAlias(Arguments((nameof(letterId), letterId)));
    }

    [ReadmeTool("Selection Semantics And Notifications", "Dismiss a specific dismissible letter by native letter id, mirroring a normal right-click on the letter stack entry")]
    [Tool("rimworld/dismiss_letter", Description = "Dismiss a specific dismissible letter by native letter id, mirroring a normal right-click on the letter stack entry")]
    public object DismissLetter([ToolParameter(Description = "Letter id returned by rimworld/list_letters")] string letterId)
    {
        return InvokeAlias(Arguments((nameof(letterId), letterId)));
    }

    [ReadmeTool("Selection Semantics And Notifications", "List active RimWorld alerts with structured culprit targets and alert-snapshot-scoped alert ids")]
    [Tool("rimworld/list_alerts", Description = "List active RimWorld alerts with structured culprit targets and alert-snapshot-scoped alert ids")]
    public object ListAlerts([ToolParameter(Description = "Maximum number of active alerts to return")] int limit = 40)
    {
        return InvokeAlias(Arguments((nameof(limit), limit)));
    }

    [ReadmeTool("Selection Semantics And Notifications", "Activate one alert by alert id returned from rimworld/list_alerts, mirroring a normal left-click on the alert readout")]
    [Tool("rimworld/activate_alert", Description = "Activate one alert by alert id returned from rimworld/list_alerts, mirroring a normal left-click on the alert readout")]
    public object ActivateAlert([ToolParameter(Description = "Alert id returned by rimworld/list_alerts")] string alertId)
    {
        return InvokeAlias(Arguments((nameof(alertId), alertId)));
    }

    [ReadmeTool("Camera And Screenshots", "Get the current map camera position, zoom, and visible rect")]
    [Tool("rimworld/get_camera_state", Description = "Get the current map camera position, zoom, and visible rect")]
    public object GetCameraState()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Camera And Screenshots", "Get current screen-space targets such as open windows and active context-menu geometry")]
    [Tool("rimworld/get_screen_targets", Description = "Get current screen-space targets such as open windows and active context-menu geometry")]
    public object GetScreenTargets()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Camera And Screenshots", "Jump the camera to a pawn by name or stable pawn id")]
    [Tool("rimworld/jump_camera_to_pawn", Description = "Jump the camera to a pawn by name or stable pawn id")]
    public object JumpCameraToPawn(
        [ToolParameter(Description = "Optional pawn name on the current map")] string pawnName = null,
        [ToolParameter(Description = "Optional stable current-map pawn id from rimworld/list_colonists")] string pawnId = null)
    {
        return InvokeAlias(Arguments((nameof(pawnName), pawnName), (nameof(pawnId), pawnId)));
    }

    [ReadmeTool("Camera And Screenshots", "Jump the camera to a map cell")]
    [Tool("rimworld/jump_camera_to_cell", Description = "Jump the camera to a map cell")]
    public object JumpCameraToCell(
        [ToolParameter(Description = "Cell x coordinate")] int x,
        [ToolParameter(Description = "Cell z coordinate")] int z)
    {
        return InvokeAlias(Arguments((nameof(x), x), (nameof(z), z)));
    }

    [ReadmeTool("Camera And Screenshots", "Move the camera by a cell offset")]
    [Tool("rimworld/move_camera", Description = "Move the camera by a cell offset")]
    public object MoveCamera(
        [ToolParameter(Description = "Delta x in map cells")] float deltaX,
        [ToolParameter(Description = "Delta z in map cells")] float deltaZ)
    {
        return InvokeAlias(Arguments((nameof(deltaX), deltaX), (nameof(deltaZ), deltaZ)));
    }

    [ReadmeTool("Camera And Screenshots", "Adjust the current camera zoom/root size")]
    [Tool("rimworld/zoom_camera", Description = "Adjust the current camera zoom/root size")]
    public object ZoomCamera([ToolParameter(Description = "Positive values zoom out, negative values zoom in")] float delta)
    {
        return InvokeAlias(Arguments((nameof(delta), delta)));
    }

    [ReadmeTool("Camera And Screenshots", "Set the current camera root size directly")]
    [Tool("rimworld/set_camera_zoom", Description = "Set the current camera root size directly")]
    public object SetCameraZoom([ToolParameter(Description = "Desired camera root size")] float rootSize)
    {
        return InvokeAlias(Arguments((nameof(rootSize), rootSize)));
    }

    [ReadmeTool("Camera And Screenshots", "Frame a comma-separated list of pawns by name and/or stable pawn id so they fit in view")]
    [Tool("rimworld/frame_pawns", Description = "Frame a comma-separated list of pawns by name and/or stable pawn id so they fit in view")]
    public object FramePawns(
        [ToolParameter(Description = "Comma-separated pawn names. If omitted together with pawnIdsCsv, uses the current selection.")] string pawnNamesCsv = null,
        [ToolParameter(Description = "Comma-separated stable pawn ids from rimworld/list_colonists. If omitted together with pawnNamesCsv, uses the current selection.")] string pawnIdsCsv = null)
    {
        return InvokeAlias(Arguments((nameof(pawnNamesCsv), pawnNamesCsv), (nameof(pawnIdsCsv), pawnIdsCsv)));
    }

    [ReadmeTool("Camera And Screenshots", "Take an in-game screenshot and return the saved file path plus optional target metadata")]
    [Tool("rimworld/take_screenshot", Description = "Take an in-game screenshot and return the saved file path plus optional target metadata")]
    public object TakeScreenshot(
        [ToolParameter(Description = "Optional screenshot file name without extension")] string fileName = null,
        [ToolParameter(Description = "Include current screen target metadata such as windows and context menus")] bool includeTargets = true,
        [ToolParameter(Description = "Suppress RimWorld's screenshot-taken message during this automated capture")] bool suppressMessage = true,
        [ToolParameter(Description = "Optional target id from rimworld/get_screen_targets to crop around")] string clipTargetId = null,
        [ToolParameter(Description = "Logical screen-pixel padding to include around the clip target")] int clipPadding = 8)
    {
        return InvokeAlias(Arguments((nameof(fileName), fileName), (nameof(includeTargets), includeTargets), (nameof(suppressMessage), suppressMessage), (nameof(clipTargetId), clipTargetId), (nameof(clipPadding), clipPadding)));
    }

    [ReadmeTool("Save/Load And Spawning", "List saved RimWorld games")]
    [Tool("rimworld/list_saves", Description = "List saved RimWorld games")]
    public object ListSaves()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Save/Load And Spawning", "Spawn a thing on the current map at a target cell")]
    [Tool("rimworld/spawn_thing", Description = "Spawn a thing on the current map at a target cell")]
    public object SpawnThing(
        [ToolParameter(Description = "ThingDef defName to spawn")] string defName,
        [ToolParameter(Description = "Target cell x coordinate")] int x,
        [ToolParameter(Description = "Target cell z coordinate")] int z,
        [ToolParameter(Description = "Optional stack count. Clamped to the thing's stack limit.")] int stackCount = 1)
    {
        return InvokeAlias(Arguments((nameof(defName), defName), (nameof(x), x), (nameof(z), z), (nameof(stackCount), stackCount)));
    }

    [ReadmeTool("Save/Load And Spawning", "Save the current game to a named save")]
    [Tool("rimworld/save_game", Description = "Save the current game to a named save")]
    public object SaveGame([ToolParameter(Description = "Save name without extension")] string saveName)
    {
        return InvokeAlias(Arguments((nameof(saveName), saveName)));
    }

    [ReadmeTool("Save/Load And Spawning", "Load a named RimWorld save")]
    [Tool("rimworld/load_game", Description = "Load a named RimWorld save")]
    public object LoadGame([ToolParameter(Description = "Save name without extension")] string saveName)
    {
        return InvokeAlias(Arguments((nameof(saveName), saveName)));
    }

    [ReadmeTool("Context Menus And Map Interaction", "Dispatch a live map click at a target pawn or cell and capture the resulting context menu when one remains open")]
    [Tool("rimworld/open_context_menu", Description = "Dispatch a live map click at a target pawn or cell and capture the resulting context menu when one remains open")]
    public object OpenContextMenu(
        [ToolParameter(Description = "Optional target pawn name on the current map.")] string targetPawnName = null,
        [ToolParameter(Description = "Optional stable target pawn id from rimworld/list_colonists.")] string targetPawnId = null,
        [ToolParameter(Description = "Target cell x coordinate when no pawn name or id is given")] int x = 0,
        [ToolParameter(Description = "Target cell z coordinate when no pawn name or id is given")] int z = 0,
        [ToolParameter(Description = "Compatibility hint. 'vanilla', 'auto', and 'live' are accepted; the tool always routes through the live play-UI click path.")] string mode = "vanilla",
        [ToolParameter(Description = "Mouse button to inject. Supported values are 'left', 'right', and 'middle'.")] string button = "right",
        [ToolParameter(Description = "How long to hold the mouse button down before releasing it. Use this for mods that distinguish tap from hold on map clicks.")] int holdDurationMs = 0,
        [ToolParameter(Description = "Optional comma-, space-, or plus-separated event modifiers such as 'shift', 'ctrl', 'alt', or 'command'.")] string modifiers = null)
    {
        return InvokeAlias(Arguments((nameof(targetPawnName), targetPawnName), (nameof(targetPawnId), targetPawnId), (nameof(x), x), (nameof(z), z), (nameof(mode), mode), (nameof(button), button), (nameof(holdDurationMs), holdDurationMs), (nameof(modifiers), modifiers)));
    }

    [ReadmeTool("Context Menus And Map Interaction", "Dispatch a live map click interaction for the current pawn selection so vanilla and modded handlers see the same input path as a real click")]
    [Tool("rimworld/right_click_cell", Description = "Dispatch a live map click interaction for the current pawn selection so vanilla and modded handlers see the same input path as a real click")]
    public object RightClickCell(
        [ToolParameter(Description = "Optional target pawn name on the current map.")] string targetPawnName = null,
        [ToolParameter(Description = "Optional stable target pawn id from rimworld/list_colonists.")] string targetPawnId = null,
        [ToolParameter(Description = "Target cell x coordinate when no pawn name or id is given")] int x = 0,
        [ToolParameter(Description = "Target cell z coordinate when no pawn name or id is given")] int z = 0,
        [ToolParameter(Description = "Mouse button to inject. Supported values are 'left', 'right', and 'middle'.")] string button = "right",
        [ToolParameter(Description = "How long to hold the mouse button down before releasing it. Use this for mods that distinguish tap from hold on map clicks.")] int holdDurationMs = 0,
        [ToolParameter(Description = "Optional comma-, space-, or plus-separated event modifiers such as 'shift', 'ctrl', 'alt', or 'command'.")] string modifiers = null)
    {
        return InvokeAlias(Arguments((nameof(targetPawnName), targetPawnName), (nameof(targetPawnId), targetPawnId), (nameof(x), x), (nameof(z), z), (nameof(button), button), (nameof(holdDurationMs), holdDurationMs), (nameof(modifiers), modifiers)));
    }

    [ReadmeTool("Context Menus And Map Interaction", "Get the currently opened debug context menu options")]
    [Tool("rimworld/get_context_menu_options", Description = "Get the currently opened debug context menu options")]
    public object GetContextMenuOptions()
    {
        return InvokeAlias();
    }

    [ReadmeTool("Context Menus And Map Interaction", "Execute a context menu option by index or label")]
    [Tool("rimworld/execute_context_menu_option", Description = "Execute a context menu option by index or label")]
    public object ExecuteContextMenuOption(
        [ToolParameter(Description = "1-based option index. Use -1 to resolve by label instead.")] int optionIndex = -1,
        [ToolParameter(Description = "Exact or partial menu label to execute when optionIndex is -1")] string label = null)
    {
        return InvokeAlias(Arguments((nameof(optionIndex), optionIndex), (nameof(label), label)));
    }

    [ReadmeTool("Context Menus And Map Interaction", "Close the currently opened debug context menu")]
    [Tool("rimworld/close_context_menu", Description = "Close the currently opened debug context menu")]
    public object CloseContextMenu()
    {
        return InvokeAlias();
    }

    private static object InvokeAlias(Dictionary<string, object> arguments = null, [CallerMemberName] string memberName = null)
    {
        return LegacyToolExecution.InvokeAlias(memberName, arguments);
    }

    private static Dictionary<string, object> Arguments(params (string Name, object Value)[] arguments)
    {
        var result = new Dictionary<string, object>(arguments.Length, System.StringComparer.Ordinal);
        foreach (var argument in arguments)
            result[argument.Name] = argument.Value;

        return result;
    }
}
