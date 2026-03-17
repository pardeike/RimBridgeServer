using System.Text;

internal static class ReadmeToolSurface
{
    private static readonly GroupDefinition[] Groups =
    [
        new("Bridge Diagnostics",
        [
            "rimbridge/ping",
            "rimworld/get_game_info",
            "rimbridge/get_operation",
            "rimbridge/get_bridge_status",
            "rimbridge/list_capabilities",
            "rimbridge/get_capability",
            "rimbridge/list_operations",
            "rimbridge/list_operation_events",
            "rimbridge/list_logs",
            "rimbridge/wait_for_operation",
            "rimbridge/wait_for_game_loaded",
            "rimbridge/wait_for_long_event_idle"
        ]),
        new("Scripting",
        [
            "rimbridge/get_script_reference",
            "rimbridge/get_lua_reference",
            "rimbridge/run_script",
            "rimbridge/run_lua",
            "rimbridge/run_lua_file",
            "rimbridge/compile_lua",
            "rimbridge/compile_lua_file"
        ]),
        new("Debug Actions And Mods",
        [
            "rimworld/pause_game",
            "rimworld/list_debug_action_roots",
            "rimworld/list_debug_action_children",
            "rimworld/get_debug_action",
            "rimworld/execute_debug_action",
            "rimworld/set_debug_setting",
            "rimworld/list_mods",
            "rimworld/get_mod_configuration_status",
            "rimworld/set_mod_enabled",
            "rimworld/reorder_mod",
            "rimworld/list_mod_settings_surfaces",
            "rimworld/get_mod_settings",
            "rimworld/update_mod_settings",
            "rimworld/reload_mod_settings",
            "rimworld/open_mod_settings"
        ]),
        new("Architect And Map State",
        [
            "rimworld/get_designator_state",
            "rimworld/set_god_mode",
            "rimworld/list_architect_categories",
            "rimworld/list_architect_designators",
            "rimworld/select_architect_designator",
            "rimworld/apply_architect_designator",
            "rimworld/list_zones",
            "rimworld/list_areas",
            "rimworld/create_allowed_area",
            "rimworld/select_allowed_area",
            "rimworld/set_zone_target",
            "rimworld/clear_area",
            "rimworld/delete_area",
            "rimworld/delete_zone",
            "rimworld/get_cell_info",
            "rimworld/find_random_cell_near",
            "rimworld/flood_fill_cells"
        ]),
        new("UI And Input",
        [
            "rimworld/get_ui_state",
            "rimworld/list_main_tabs",
            "rimworld/open_main_tab",
            "rimworld/close_main_tab",
            "rimworld/get_ui_layout",
            "rimworld/click_ui_target",
            "rimworld/set_hover_target",
            "rimworld/clear_hover_target",
            "rimworld/press_accept",
            "rimworld/press_cancel",
            "rimworld/close_window",
            "rimworld/click_screen_target",
            "rimworld/start_debug_game",
            "rimworld/go_to_main_menu"
        ]),
        new("Selection And Colony State",
        [
            "rimworld/list_colonists",
            "rimworld/clear_selection",
            "rimworld/select_pawn",
            "rimworld/deselect_pawn",
            "rimworld/set_draft"
        ]),
        new("Selection Semantics And Notifications",
        [
            "rimworld/get_selection_semantics",
            "rimworld/list_selected_gizmos",
            "rimworld/execute_gizmo",
            "rimworld/list_messages",
            "rimworld/list_letters",
            "rimworld/open_letter",
            "rimworld/dismiss_letter",
            "rimworld/list_alerts",
            "rimworld/activate_alert"
        ]),
        new("Camera And Screenshots",
        [
            "rimworld/get_camera_state",
            "rimworld/get_screen_targets",
            "rimworld/jump_camera_to_pawn",
            "rimworld/jump_camera_to_cell",
            "rimworld/move_camera",
            "rimworld/zoom_camera",
            "rimworld/set_camera_zoom",
            "rimworld/frame_pawns",
            "rimworld/take_screenshot"
        ]),
        new("Save/Load And Spawning",
        [
            "rimworld/list_saves",
            "rimworld/spawn_thing",
            "rimworld/save_game",
            "rimworld/load_game"
        ]),
        new("Context Menus And Map Interaction",
        [
            "rimworld/open_context_menu",
            "rimworld/right_click_cell",
            "rimworld/get_context_menu_options",
            "rimworld/execute_context_menu_option",
            "rimworld/close_context_menu"
        ])
    ];

    public static string Render(IReadOnlyList<ToolDefinition> tools)
    {
        var toolByName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        ValidateCoverage(toolByName);
        var inlineCodeTerms = toolByName.Keys
            .Concat(InlineCodeTerms)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(term => term.Length)
            .ToArray();

        var builder = new StringBuilder();
        foreach (var group in Groups)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.AppendLine($"### {group.Title}");
            builder.AppendLine();

            foreach (var toolName in group.ToolNames)
            {
                var tool = toolByName[toolName];
                builder.AppendLine($"- `{tool.Name}` - {FormatSummary(tool.Description, inlineCodeTerms)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void ValidateCoverage(IReadOnlyDictionary<string, ToolDefinition> toolByName)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in Groups)
        {
            foreach (var toolName in group.ToolNames)
            {
                if (!toolByName.ContainsKey(toolName))
                    throw new InvalidOperationException($"README tool surface references unknown tool '{toolName}' in group '{group.Title}'.");

                if (!assigned.Add(toolName))
                    throw new InvalidOperationException($"README tool surface assigns tool '{toolName}' more than once.");
            }
        }

        var missing = toolByName.Keys
            .Where(toolName => !assigned.Contains(toolName))
            .OrderBy(toolName => toolName, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"README tool surface is missing {missing.Length} tool assignments: {string.Join(", ", missing)}");
    }

    private static string FormatSummary(string description, IReadOnlyList<string> inlineCodeTerms)
    {
        var result = description;
        var placeholders = new List<(string Placeholder, string Replacement)>(inlineCodeTerms.Count);
        for (var index = 0; index < inlineCodeTerms.Count; index++)
        {
            var codeTerm = inlineCodeTerms[index];
            if (!result.Contains(codeTerm, StringComparison.Ordinal))
                continue;

            var placeholder = $"\u0001CODE{index}\u0001";
            result = result.Replace(codeTerm, placeholder, StringComparison.Ordinal);
            placeholders.Add((placeholder, $"`{codeTerm}`"));
        }

        foreach (var (placeholder, replacement) in placeholders)
            result = result.Replace(placeholder, replacement, StringComparison.Ordinal);

        return result;
    }

    private static readonly string[] InlineCodeTerms =
    [
        "rimbridge/get_script_reference",
        "rimbridge/get_lua_reference",
        "rimworld/list_selected_gizmos",
        "rimworld/get_screen_targets",
        "rimworld/list_mod_settings_surfaces",
        "rimworld/list_architect_categories",
        "rimworld/list_architect_designators",
        "rimworld/list_colonists",
        "Mod.WriteSettings()",
        "ModsConfig.xml",
        "Dialog_ModSettings",
        "ModSettings",
        "main-tab",
        "ui-element",
        "params",
        "defName",
        ".lua"
    ];

    private sealed record GroupDefinition(string Title, IReadOnlyList<string> ToolNames);
}
