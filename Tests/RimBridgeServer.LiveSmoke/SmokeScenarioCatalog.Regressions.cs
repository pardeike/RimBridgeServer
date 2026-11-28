using System.Text.Json.Nodes;

namespace RimBridgeServer.LiveSmoke;

// Live checks for defects that a review of the bridge found in specific game states: forced pauses, the world view,
// held pawns, the draft rules, hidden designators, zone targets, the map edge and a load from a running game.
internal static partial class SmokeScenarioCatalog
{
    public const string TimeAndInputGuardsScenarioName = "time-and-input-guards";
    public const string HeldPawnsAndDraftingScenarioName = "held-pawns-and-drafting";
    public const string ArchitectRulesScenarioName = "architect-rules";
    public const string LoadReadinessScenarioName = "load-readiness";

    private static IEnumerable<KeyValuePair<string, SmokeScenarioDefinition>> RegressionScenarios()
    {
        yield return new(TimeAndInputGuardsScenarioName, new SmokeScenarioDefinition
        {
            Name = TimeAndInputGuardsScenarioName,
            Description = "Verify that rimworld/set_time_speed refuses a number that is not a time speed. Open the force-pausing Dialog_Options and verify that rimworld/pause_game unpauses and pauses by the time speed. Verify that rimworld/close_window without a type closes the dialog and not an ImmediateWindow. Open the World tab and verify that rimworld/click_cell refuses and rimworld/jump_camera_to_cell shows the map. Verify that rimworld/switch_language refuses to switch while a game runs.",
            RunAsync = RunTimeAndInputGuardsAsync
        });
        yield return new(HeldPawnsAndDraftingScenarioName, new SmokeScenarioDefinition
        {
            Name = HeldPawnsAndDraftingScenarioName,
            Description = "Order a colonist into a spawned cryptosleep casket. Verify that rimworld/list_pawns and rimworld/get_pawn_details report the held pawn with heldBy, its map and the casket cell, and that a debug action can target it. Verify that rimworld/set_draft drafts through the game's toggle, reports changed, and refuses a downed colonist with the game's reason. Verify that rimworld/select_pawn selects an animal.",
            RunAsync = RunHeldPawnsAndDraftingAsync
        });
        yield return new(ArchitectRulesScenarioName, new SmokeScenarioDefinition
        {
            Name = ArchitectRulesScenarioName,
            Description = "With god mode off, verify that a build designator that the game hides is not listed by default and that applying it fails. Make two stockpiles, target the first with rimworld/set_zone_target, and verify that an apply over the gap and the second stockpile adds only the gap, reports the second stockpile's cells as rejected and leaves them in the second stockpile. Verify that an apply that does not connect to the target fails. Verify that rimworld/spawn_thing refuses a 4x4 building that does not fit at the map corner without an error log.",
            RunAsync = RunArchitectRulesAsync
        });
        yield return new(LoadReadinessScenarioName, new SmokeScenarioDefinition
        {
            Name = LoadReadinessScenarioName,
            Description = "Save the game, play on, and load the save with rimworld/load_game_ready and the default mapData readiness. Verify that the reply describes the loaded game and not the game that was running when the load was queued.",
            RunAsync = RunLoadReadinessAsync
        });
    }

    private static bool? ReplySuccess(JsonNode? reply) => JsonNodeHelpers.ReadBoolean(reply, "success");

    // The harness reports a reply with success false as a failed call, so an expected refusal is checked here.
    private static async Task<ToolInvocationResult> ExpectRefusalAsync(SmokeScenarioContext context, string step, string tool, object arguments, string mustContain, CancellationToken cancellationToken)
    {
        var reply = await context.CallGameToolAsync(step, tool, arguments, cancellationToken);
        if (reply.Success)
            throw new InvalidOperationException($"{tool} must refuse here: {reply.StructuredContent?.ToJsonString()}");
        if (ReadReplyMessage(reply).Contains(mustContain, StringComparison.OrdinalIgnoreCase) == false)
            throw new InvalidOperationException($"The refusal of {tool} must say '{mustContain}': {ReadReplyMessage(reply)}");
        return reply;
    }

    private static async Task RunTimeAndInputGuardsAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("guards.wait_for_long_event_idle", cancellationToken);
        var stepIndex = 0;
        string Step(string name) => $"guards.{name}_{++stepIndex}";

        async Task<JsonNode?> CallAsync(string step, string tool, object arguments, string action)
        {
            var reply = await context.CallGameToolAsync(Step(step), tool, arguments, cancellationToken);
            context.EnsureSucceeded(reply, action);
            return reply.StructuredContent;
        }

        // Enum.TryParse accepts any number, but 5 is not a TimeSpeed.
        await ExpectRefusalAsync(context, Step("bad_speed"), "rimworld/set_time_speed", new { speed = "5" }, "Unknown time speed", cancellationToken);

        await CallAsync("pause", "rimworld/pause_game", new { pause = true }, "Pausing the game");
        try
        {
            // Dialog_Options forces a pause. TickManager.Paused is then true at every time speed.
            await CallAsync("open_options", "rimworld/open_window_by_type", new { windowType = "Dialog_Options" }, "Opening Dialog_Options");
            var unpause = await CallAsync("unpause_forced", "rimworld/pause_game", new { pause = false }, "Unpausing during a forced pause");
            if (ReplySuccess(unpause) != true || JsonNodeHelpers.ReadString(unpause, "timeSpeed") == "Paused" || JsonNodeHelpers.ReadBoolean(unpause, "forcePaused") != true)
                throw new InvalidOperationException($"Unpausing during a forced pause must set a running time speed and report forcePaused: {unpause?.ToJsonString()}");

            var pause = await CallAsync("pause_forced", "rimworld/pause_game", new { pause = true }, "Pausing during a forced pause");
            if (ReplySuccess(pause) != true || JsonNodeHelpers.ReadString(pause, "timeSpeed") != "Paused")
                throw new InvalidOperationException($"Pausing during a forced pause must set the time speed Paused: {pause?.ToJsonString()}");

            // An ImmediateWindow, such as a tooltip, is often the top window. Without a type, the dialog must close.
            var close = await CallAsync("close_top", "rimworld/close_window", new { }, "Closing the top window");
            if (JsonNodeHelpers.ReadArray(close, "closedWindowTypes").All(type => type?.GetValue<string>() != "RimWorld.Dialog_Options"))
                throw new InvalidOperationException($"rimworld/close_window without a type must close Dialog_Options: {close?.ToJsonString()}");
            if (JsonNodeHelpers.ReadString(close, "state", "timeSpeed") != "Paused")
                throw new InvalidOperationException($"The pause from the forced pause must hold after the dialog closes: {close?["state"]?.ToJsonString()}");
        }
        finally
        {
            await context.CallGameToolAsync(Step("close_options"), "rimworld/close_window", new { windowType = "Dialog_Options" }, cancellationToken);
            await context.CallGameToolAsync(Step("pause_again"), "rimworld/pause_game", new { pause = true }, cancellationToken);
        }

        // A map click while the world view is open would act on the world map.
        var colonists = await CallAsync("list_colonists", "rimworld/list_colonists", new { currentMapOnly = true }, "Listing current-map colonists");
        var origin = ResolveColonistSearchOrigin(colonists);
        var open = await CallAsync("open_world", "rimworld/open_main_tab", new { mainTabId = "main-tab:World" }, "Opening the World tab");
        if (JsonNodeHelpers.ReadBoolean(open, "after", "worldViewOpen") != true)
            throw new InvalidOperationException("Opening the World tab must open the world view.");
        try
        {
            var click = await context.CallGameToolAsync(Step("click_in_world_view"), "rimworld/click_cell", new { x = origin.X, z = origin.Z }, cancellationToken);
            if (click.Success && ReplySuccess(click.StructuredContent) != false)
                throw new InvalidOperationException($"rimworld/click_cell must refuse while the world view is open: {click.StructuredContent?.ToJsonString()}");
            if (ReadReplyMessage(click).Contains("world view", StringComparison.OrdinalIgnoreCase) == false)
                throw new InvalidOperationException($"The refusal must name the world view: {ReadReplyMessage(click)}");

            await CallAsync("jump_to_map", "rimworld/jump_camera_to_cell", new { x = origin.X, z = origin.Z }, "Jumping the camera to a map cell");
            var ui = await CallAsync("ui_state", "rimworld/get_ui_state", new { }, "Reading the UI state");
            if (JsonNodeHelpers.ReadBoolean(ui, "worldViewOpen") != false)
                throw new InvalidOperationException($"A camera jump to a map cell must show the map: {ui?.ToJsonString()}");
        }
        finally
        {
            await context.CallGameToolAsync(Step("close_world"), "rimworld/close_main_tab", new { mainTabId = "main-tab:World" }, cancellationToken);
        }

        // A language switch reloads all defs, which would end the running game.
        var languages = await CallAsync("list_languages", "rimworld/list_languages", new { }, "Listing languages");
        var activeId = JsonNodeHelpers.ReadString(languages, "activeLanguageId");
        var other = JsonNodeHelpers.ReadArray(languages, "languages")
            .FirstOrDefault(language => JsonNodeHelpers.ReadBoolean(language, "isActive") != true && JsonNodeHelpers.ReadBoolean(language, "hasErrors") != true);
        if (other == null)
        {
            context.Note("Only one language is installed, so the in-game language switch check did not run.");
            return;
        }

        var query = ReadRequiredString(other, "recommendedQuery");
        await ExpectRefusalAsync(context, Step("switch_language"), "rimworld/switch_language", new { language = query }, "main menu", cancellationToken);
        var languagesAfter = await CallAsync("list_languages_after", "rimworld/list_languages", new { }, "Listing languages again");
        if (JsonNodeHelpers.ReadString(languagesAfter, "activeLanguageId") != activeId || JsonNodeHelpers.ReadString(languagesAfter, "state", "programState") != "Playing")
            throw new InvalidOperationException($"The refused switch must keep the language and the game: {languagesAfter?.ToJsonString()}");
    }

    private static string ReadReplyMessage(ToolInvocationResult result)
    {
        return JsonNodeHelpers.ReadString(result.StructuredContent, "message") ?? result.Message ?? string.Empty;
    }

    private static async Task RunHeldPawnsAndDraftingAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("held_pawns.wait_for_long_event_idle", cancellationToken);
        var stepIndex = 0;
        string Step(string name) => $"held_pawns.{name}_{++stepIndex}";

        async Task<JsonNode?> CallAsync(string step, string tool, object arguments, string action)
        {
            var reply = await context.CallGameToolAsync(Step(step), tool, arguments, cancellationToken);
            context.EnsureSucceeded(reply, action);
            return reply.StructuredContent;
        }

        await CallAsync("pause", "rimworld/pause_game", new { pause = true }, "Pausing the game");
        var colonists = await CallAsync("list_colonists", "rimworld/list_colonists", new { currentMapOnly = true }, "Listing current-map colonists");
        var mapId = ReadRequiredString(JsonNodeHelpers.ReadArray(colonists, "colonists").FirstOrDefault(), "mapId");
        var origin = ResolveColonistSearchOrigin(colonists);

        // The scenario uses colonists of its own and removes them at the end, so no later scenario finds a colonist
        // in a casket or a downed colonist.
        var spawned = new List<string>();
        async Task<string> SpawnColonistAsync(string step)
        {
            var cell = await CallAsync(step + "_cell", "rimworld/find_random_cell_near", new { x = origin.X, z = origin.Z, startingSearchRadius = 2, maxSearchRadius = 12, requireStandable = true, requireNoImpassableThings = true }, "Finding a cell for a colonist");
            var spawn = await CallAsync(step, "rimworld/spawn_thing", new { defName = "Colonist", x = JsonNodeHelpers.ReadInt32(cell, "cell", "x"), z = JsonNodeHelpers.ReadInt32(cell, "cell", "z") }, "Spawning a colonist");
            var id = ReadRequiredString(spawn, "thingId");
            spawned.Add(id);
            return id;
        }

        var sleeperId = await SpawnColonistAsync("spawn_sleeper");
        var drafteeId = await SpawnColonistAsync("spawn_draftee");
        try
        {

            // A casket is 1x2. The colonist walks to it after the context-menu order.
            var found = await CallAsync("find_casket_cell", "rimworld/find_random_cell_near", new
            {
                x = origin.X,
                z = origin.Z,
                startingSearchRadius = 3,
                maxSearchRadius = 20,
                requireStandable = true,
                requireNoImpassableThings = true,
                width = 1,
                height = 2
            }, "Finding a cell for a cryptosleep casket");
            var casketX = JsonNodeHelpers.ReadInt32(found, "cell", "x") ?? throw new InvalidOperationException("No casket cell x.");
            var casketZ = JsonNodeHelpers.ReadInt32(found, "cell", "z") ?? throw new InvalidOperationException("No casket cell z.");
            var casket = await CallAsync("spawn_casket", "rimworld/spawn_thing", new { defName = "CryptosleepCasket", x = casketX, z = casketZ }, "Spawning a cryptosleep casket");
            var casketId = ReadRequiredString(casket, "thingId");
            spawned.Add(casketId);

            await CallAsync("select_sleeper", "rimworld/select_pawn", new { pawnId = sleeperId }, "Selecting the colonist for the casket");
            var menu = await CallAsync("casket_menu", "rimworld/open_context_menu", new { x = casketX, z = casketZ }, "Opening the context menu on the casket");
            var enter = JsonNodeHelpers.ReadArray(menu, "options")
                .FirstOrDefault(option => (JsonNodeHelpers.ReadString(option, "label") ?? string.Empty).Contains("cryptosleep", StringComparison.OrdinalIgnoreCase)
                    && JsonNodeHelpers.ReadBoolean(option, "disabled") != true)
                ?? throw new InvalidOperationException($"The casket context menu must offer to enter it: {menu?.ToJsonString()}");
            await CallAsync("enter_casket", "rimworld/execute_context_menu_option", new { optionIndex = JsonNodeHelpers.ReadInt32(enter, "index") ?? 1 }, "Ordering the colonist into the casket");

            JsonNode? sleeper = null;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                await CallAsync("play", "rimworld/play_for", new { durationMs = 1000, speed = "Superfast" }, "Letting the colonist walk to the casket");
                sleeper = await CallAsync("sleeper_details", "rimworld/get_pawn_details", new { pawnId = sleeperId }, "Reading the colonist");
                if (JsonNodeHelpers.ReadString(sleeper, "pawn", "heldBy", "thingId") == casketId)
                    break;
            }

            await CallAsync("pause_after_walk", "rimworld/pause_game", new { pause = true }, "Pausing the game");
            if (JsonNodeHelpers.ReadString(sleeper, "pawn", "heldBy", "thingId") != casketId
                || JsonNodeHelpers.ReadBoolean(sleeper, "pawn", "spawned") != false
                || JsonNodeHelpers.ReadString(sleeper, "pawn", "mapId") != mapId
                || JsonNodeHelpers.ReadInt32(sleeper, "pawn", "position", "x") != casketX
                || JsonNodeHelpers.ReadInt32(sleeper, "pawn", "position", "z") != casketZ)
                throw new InvalidOperationException($"A colonist in a casket must report heldBy the casket, the map and the casket cell: {sleeper?["pawn"]?.ToJsonString()}");

            var colonistList = await CallAsync("list_pawns", "rimworld/list_pawns", new { role = "colonist" }, "Listing colonists");
            var sleeperRow = JsonNodeHelpers.ReadArray(colonistList, "pawns").FirstOrDefault(row => JsonNodeHelpers.ReadString(row, "pawnId") == sleeperId)
                ?? throw new InvalidOperationException("rimworld/list_pawns must list the colonist in the casket.");
            if (JsonNodeHelpers.ReadString(sleeperRow, "heldBy", "thingId") != casketId || JsonNodeHelpers.ReadInt32(sleeperRow, "position", "x") != casketX)
                throw new InvalidOperationException($"The list row of the held colonist must have heldBy and the casket cell: {sleeperRow.ToJsonString()}");

            // The game's debug tool targets the pawns at a cell, including held pawns.
            var ageBefore = JsonNodeHelpers.ReadInt32(sleeper, "identity", "ageBiologicalYears") ?? throw new InvalidOperationException("identity must report the biological age.");
            var birthday = await context.CallGameToolAsync(Step("birthday"), "rimworld/execute_debug_action", new { path = "Actions\\T: Force Birthday", pawnId = sleeperId }, cancellationToken);
            if (!birthday.Success || ReplySuccess(birthday.StructuredContent) != true)
                throw new InvalidOperationException($"A debug action must target the colonist in the casket: {ReadReplyMessage(birthday)}");
            var aged = await CallAsync("aged_details", "rimworld/get_pawn_details", new { pawnId = sleeperId }, "Reading the colonist after the birthday");
            if (JsonNodeHelpers.ReadInt32(aged, "identity", "ageBiologicalYears") != ageBefore + 1)
                throw new InvalidOperationException($"The forced birthday must add one year: {aged?["identity"]?.ToJsonString()}");

            // The draft rules come from the game's draft toggle.
            var draft = await CallAsync("draft", "rimworld/set_draft", new { pawnId = drafteeId, drafted = true }, "Drafting a colonist");
            if (ReplySuccess(draft) != true || JsonNodeHelpers.ReadBoolean(draft, "drafted") != true || JsonNodeHelpers.ReadBoolean(draft, "changed") != true)
                throw new InvalidOperationException($"Drafting a colonist must succeed and report changed: {draft?.ToJsonString()}");
            var again = await CallAsync("draft_again", "rimworld/set_draft", new { pawnId = drafteeId, drafted = true }, "Drafting the colonist again");
            if (ReplySuccess(again) != true || JsonNodeHelpers.ReadBoolean(again, "changed") != false)
                throw new InvalidOperationException($"Drafting a drafted colonist must succeed without a change: {again?.ToJsonString()}");
            var undraft = await CallAsync("undraft", "rimworld/set_draft", new { pawnId = drafteeId, drafted = false }, "Undrafting the colonist");
            if (JsonNodeHelpers.ReadBoolean(undraft, "drafted") != false)
                throw new InvalidOperationException($"Undrafting must undraft the colonist: {undraft?.ToJsonString()}");

            await CallAsync("down", "rimworld/execute_debug_action", new { path = "Actions\\T: Damage Until Down", pawnId = drafteeId }, "Downing the colonist");
            var downed = await CallAsync("downed_details", "rimworld/get_pawn_details", new { pawnId = drafteeId }, "Reading the downed colonist");
            if (JsonNodeHelpers.ReadBoolean(downed, "pawn", "downed") != true)
                throw new InvalidOperationException("The debug action must down the colonist.");
            var downedDraft = await ExpectRefusalAsync(context, Step("draft_downed"), "rimworld/set_draft", new { pawnId = drafteeId, drafted = true }, "does not allow drafting", cancellationToken);
            if (string.IsNullOrWhiteSpace(JsonNodeHelpers.ReadString(downedDraft.StructuredContent, "disabledReason")))
                throw new InvalidOperationException($"Drafting a downed colonist must give the game's reason: {downedDraft.StructuredContent?.ToJsonString()}");

            // As a click in the game, select_pawn selects any spawned pawn of the current map.
            var animals = await CallAsync("list_animals", "rimworld/list_pawns", new { role = "animal" }, "Listing animals");
            var animalId = JsonNodeHelpers.ReadArray(animals, "pawns").Select(row => JsonNodeHelpers.ReadString(row, "pawnId")).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            if (animalId == null)
            {
                var cell = await CallAsync("find_animal_cell", "rimworld/find_random_cell_near", new { x = origin.X, z = origin.Z, startingSearchRadius = 3, maxSearchRadius = 15, requireStandable = true, requireNoImpassableThings = true }, "Finding a cell for an animal");
                var muffalo = await CallAsync("spawn_muffalo", "rimworld/spawn_thing", new { defName = "Muffalo", x = JsonNodeHelpers.ReadInt32(cell, "cell", "x"), z = JsonNodeHelpers.ReadInt32(cell, "cell", "z") }, "Spawning a Muffalo");
                animalId = ReadRequiredString(muffalo, "thingId");
            }

            var select = await CallAsync("select_animal", "rimworld/select_pawn", new { pawnId = animalId }, "Selecting an animal");
            if (ReplySuccess(select) != true || JsonNodeHelpers.ReadBoolean(select, "selected", "selected") != true)
                throw new InvalidOperationException($"rimworld/select_pawn must select an animal: {select?.ToJsonString()}");
            await context.CallGameToolAsync(Step("clear_selection"), "rimworld/clear_selection", new { }, cancellationToken);
        }
        finally
        {
            // T: Destroy destroys every thing on the cell of its target. The casket goes with its sleeper.
            foreach (var thingId in spawned.AsEnumerable().Reverse())
                await context.CallGameToolAsync(Step("destroy"), "rimworld/execute_debug_action", new { path = "Actions\\T: Destroy", thingId }, cancellationToken);
            var rows = await context.CallGameToolAsync(Step("list_after"), "rimworld/list_pawns", new { role = "colonist" }, cancellationToken);
            var left = JsonNodeHelpers.ReadArray(rows.StructuredContent, "pawns").Select(row => JsonNodeHelpers.ReadString(row, "pawnId")).Where(spawned.Contains).ToList();
            if (left.Count > 0)
                context.Note($"The cleanup left the spawned colonists {string.Join(", ", left)}.");
        }
    }

    private static async Task RunArchitectRulesAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("architect_rules.wait_for_long_event_idle", cancellationToken);
        var stepIndex = 0;
        string Step(string name) => $"architect_rules.{name}_{++stepIndex}";

        async Task<JsonNode?> CallAsync(string step, string tool, object arguments, string action)
        {
            var reply = await context.CallGameToolAsync(Step(step), tool, arguments, cancellationToken);
            context.EnsureSucceeded(reply, action);
            return reply.StructuredContent;
        }

        await CallAsync("pause", "rimworld/pause_game", new { pause = true }, "Pausing the game");
        var initialState = await CallAsync("designator_state", "rimworld/get_designator_state", new { }, "Reading the designator state");
        var originalGodMode = JsonNodeHelpers.ReadBoolean(initialState, "godMode") == true;
        var colonists = await CallAsync("list_colonists", "rimworld/list_colonists", new { currentMapOnly = true }, "Listing current-map colonists");
        var origin = ResolveColonistSearchOrigin(colonists);
        var zoneIds = new List<string>();
        string? stockpileId = null;

        try
        {
            await EnsureGodModeAsync(context, Step("god_mode_off"), false, cancellationToken);

            // A designator that the game hides, for example because its research is not finished.
            var categories = JsonNodeHelpers.ReadArray(await CallAsync("categories", "rimworld/list_architect_categories", new { includeHidden = false, includeEmpty = false }, "Listing categories"), "categories");
            JsonNode? hidden = null;
            string? hiddenCategoryId = null;
            foreach (var category in categories)
            {
                var categoryId = ReadRequiredString(category, "id");
                var all = await CallAsync("designators_all", "rimworld/list_architect_designators", new { categoryId, includeHidden = true }, $"Listing all designators of '{categoryId}'");
                hidden = JsonNodeHelpers.ReadArray(all, "designators").FirstOrDefault(entry => JsonNodeHelpers.ReadBoolean(entry, "visible") == false
                    && JsonNodeHelpers.ReadString(entry, "kind") == "build"
                    && JsonNodeHelpers.ReadBoolean(entry, "actionable") != false);
                if (hidden != null)
                {
                    hiddenCategoryId = categoryId;
                    break;
                }
            }

            if (hidden == null)
            {
                context.Note("Every build designator is available in this game, so the hidden-designator check did not run.");
            }
            else
            {
                var hiddenId = ReadRequiredString(hidden, "id");
                var visibleList = await CallAsync("designators_visible", "rimworld/list_architect_designators", new { categoryId = hiddenCategoryId, includeHidden = false }, "Listing visible designators");
                if (JsonNodeHelpers.ReadArray(visibleList, "designators").Any(entry => JsonNodeHelpers.ReadString(entry, "id") == hiddenId))
                    throw new InvalidOperationException($"The default listing must leave out the hidden designator '{hiddenId}'.");
                await ExpectRefusalAsync(context, Step("apply_hidden"), "rimworld/apply_architect_designator", new { designatorId = hiddenId, x = origin.X, z = origin.Z, dryRun = true }, "not available", cancellationToken);
                context.SetSummaryValue("hiddenDesignatorId", hiddenId);
            }

            // Two stockpiles in one row with a two-cell gap, and one row below them that does not touch them.
            var zoneCategoryId = ResolveArchitectCategoryId(categories, "Zone");
            var zoneDesignators = JsonNodeHelpers.ReadArray(await CallAsync("zone_designators", "rimworld/list_architect_designators", new { categoryId = zoneCategoryId }, "Listing zone designators"), "designators");
            stockpileId = ResolveArchitectDesignatorIdByClassName(zoneDesignators, "RimWorld.Designator_ZoneAddStockpile_Resources");
            var row = await FindAcceptedArchitectPlacementCellAsync(context, Step("find_zone_rect"), stockpileId, colonists, new HashSet<string>(StringComparer.Ordinal), cancellationToken, width: 8, height: 3, minRadius: 12, maxRadius: 40);

            async Task<(string ZoneId, JsonNode? Reply)> ApplyStockpileAsync(string step, int x, int z, int width)
            {
                var reply = await CallAsync(step, "rimworld/apply_architect_designator", new { designatorId = stockpileId, x, z, width, height = 1 }, $"Applying a stockpile at ({x}, {z})");
                return (JsonNodeHelpers.ReadString(reply, "zone", "id") ?? string.Empty, reply);
            }

            var (zoneA, _) = await ApplyStockpileAsync("stockpile_a", row.X, row.Z, 3);
            var (zoneB, _) = await ApplyStockpileAsync("stockpile_b", row.X + 5, row.Z, 3);
            zoneIds.AddRange([zoneA, zoneB]);
            if (string.IsNullOrEmpty(zoneA) || string.IsNullOrEmpty(zoneB) || zoneA == zoneB)
                throw new InvalidOperationException($"The two applies must make two stockpiles, not '{zoneA}' and '{zoneB}'.");

            await CallAsync("target_a", "rimworld/set_zone_target", new { designatorId = stockpileId, zoneId = zoneA }, "Targeting the first stockpile");
            var (grownZone, grow) = await ApplyStockpileAsync("grow_a", row.X, row.Z, 8);
            var rejectedReasons = JsonNodeHelpers.ReadArray(grow, "rejectedCells").Select(cell => JsonNodeHelpers.ReadString(cell, "reason") ?? string.Empty).ToList();
            if (grownZone != zoneA || JsonNodeHelpers.ReadInt32(grow, "appliedCellCount") != 2 || rejectedReasons.Count(reason => reason.Contains("belongs to the zone", StringComparison.Ordinal)) != 3)
                throw new InvalidOperationException($"Growing the target over the gap and the second stockpile must add the two gap cells and reject the three cells of the second stockpile: {grow?.ToJsonString()}");

            async Task<string?> ZoneAtAsync(int x, int z) => JsonNodeHelpers.ReadString(await CallAsync("cell_zone", "rimworld/get_cell_info", new { x, z }, $"Reading cell ({x}, {z})"), "cell", "zone", "id");
            if (await ZoneAtAsync(row.X + 4, row.Z) != zoneA || await ZoneAtAsync(row.X + 6, row.Z) != zoneB)
                throw new InvalidOperationException("The gap must be in the first stockpile and the second stockpile must keep its cells.");

            var apart = await context.CallGameToolAsync(Step("grow_apart"), "rimworld/apply_architect_designator", new { designatorId = stockpileId, x = row.X, z = row.Z + 2, width = 3, height = 1 }, cancellationToken);
            if (apart.Success
                || JsonNodeHelpers.ReadArray(apart.StructuredContent, "rejectedCells").Any(cell => (JsonNodeHelpers.ReadString(cell, "reason") ?? string.Empty).Contains("does not connect", StringComparison.Ordinal)) == false
                || !string.IsNullOrEmpty(await ZoneAtAsync(row.X, row.Z + 2)))
                throw new InvalidOperationException($"Cells that do not connect to the target must be rejected and stay unzoned: {apart.StructuredContent?.ToJsonString()}");
        }
        finally
        {
            if (stockpileId != null)
                await context.CallGameToolAsync(Step("clear_target"), "rimworld/set_zone_target", new { designatorId = stockpileId }, cancellationToken);
            foreach (var zoneId in zoneIds.Where(id => !string.IsNullOrEmpty(id)))
                await context.CallGameToolAsync(Step("delete_zone"), "rimworld/delete_zone", new { zoneId }, cancellationToken);
            await context.CallGameToolAsync(Step("cancel"), "rimworld/press_cancel", new { }, cancellationToken);
            await EnsureGodModeAsync(context, Step("restore_god_mode"), originalGodMode, cancellationToken);
        }

        // GenSpawn.Spawn logs an error and returns null for a thing that does not fit in the map.
        await ExpectRefusalAsync(context, Step("spawn_at_corner"), "rimworld/spawn_thing", new { defName = "SolarGenerator", x = 0, z = 0 }, "does not fit", cancellationToken);
        await AssertNoOpenAttentionAsync(context, Step("corner_attention"), "Spawning a building at the map corner", cancellationToken);
    }

    // Called from the floor dropdown scenario with god mode on. The game selects a dropdown child, not the dropdown, and
    // a dropdown id must stay the same when another child becomes active. A floor keeps the terrain under it.
    private static async Task RunDropdownChildChecksAsync(
        SmokeScenarioContext context,
        string floorsCategoryId,
        IReadOnlyList<JsonNode?> designators,
        string dropdownParentId,
        string firstChildId,
        (int X, int Z) usedRect,
        JsonNode? colonists,
        CancellationToken cancellationToken)
    {
        var stepIndex = 0;
        string Step(string name) => $"architect_floor.child_{name}_{++stepIndex}";

        async Task<JsonNode?> CallAsync(string step, string tool, object arguments, string action)
        {
            var reply = await context.CallGameToolAsync(Step(step), tool, arguments, cancellationToken);
            context.EnsureSucceeded(reply, action);
            return reply.StructuredContent;
        }

        var sibling = designators.FirstOrDefault(entry => JsonNodeHelpers.ReadString(entry, "parentId") == dropdownParentId
                && JsonNodeHelpers.ReadString(entry, "id") != firstChildId
                && !string.IsNullOrWhiteSpace(JsonNodeHelpers.ReadString(entry, "buildableDefName")))
            ?? throw new InvalidOperationException($"The dropdown '{dropdownParentId}' must have a second floor.");
        var siblingId = ReadRequiredString(sibling, "id");
        var siblingTerrain = ReadRequiredString(sibling, "buildableDefName");

        await CallAsync("select", "rimworld/select_architect_designator", new { designatorId = siblingId }, "Selecting the second floor of the dropdown");
        var state = await CallAsync("state", "rimworld/get_designator_state", new { }, "Reading the designator state");
        if (JsonNodeHelpers.ReadString(state, "designatorState", "selectedDesignatorId") != siblingId
            || JsonNodeHelpers.ReadString(state, "designatorState", "selectedContainerDesignatorId") != dropdownParentId)
            throw new InvalidOperationException($"The selected designator must be the second floor in its dropdown: {state?["designatorState"]?.ToJsonString()}");

        var relisted = await CallAsync("relist", "rimworld/list_architect_designators", new { categoryId = floorsCategoryId }, "Listing the floor designators again");
        var ids = JsonNodeHelpers.ReadArray(relisted, "designators").Select(entry => JsonNodeHelpers.ReadString(entry, "id")).ToHashSet(StringComparer.Ordinal);
        var lost = designators.Select(entry => JsonNodeHelpers.ReadString(entry, "id")).Where(id => id != null && !ids.Contains(id)).ToList();
        if (lost.Count > 0)
            throw new InvalidOperationException($"Designator ids must not change when another dropdown child becomes active: {string.Join(", ", lost)}");

        var blocked = new HashSet<string>(StringComparer.Ordinal);
        for (var dx = 0; dx < 2; dx++)
        {
            for (var dz = 0; dz < 2; dz++)
                blocked.Add(CellKey(usedRect.X + dx, usedRect.Z + dz));
        }

        var cell = await FindAcceptedArchitectPlacementCellAsync(context, Step("find_cell"), siblingId, colonists, blocked, cancellationToken, minRadius: 20, maxRadius: 34);
        var before = await CallAsync("cell_before", "rimworld/get_cell_info", new { x = cell.X, z = cell.Z }, "Reading the cell before the floor");
        var terrainBefore = JsonNodeHelpers.ReadString(before, "cell", "terrainDefName");

        // A live map click with the dropdown child selected. A selected dropdown would throw NotImplementedException.
        await CallAsync("reselect", "rimworld/select_architect_designator", new { designatorId = siblingId }, "Selecting the second floor again");
        await CallAsync("click", "rimworld/click_cell", new { x = cell.X, z = cell.Z }, "Clicking the map with the floor selected");
        await AssertNoOpenAttentionAsync(context, Step("click_attention"), "Clicking the map with a dropdown child selected", cancellationToken);
        var after = await CallAsync("cell_after", "rimworld/get_cell_info", new { x = cell.X, z = cell.Z }, "Reading the cell after the floor");
        if (JsonNodeHelpers.ReadString(after, "cell", "terrainDefName") != siblingTerrain)
            throw new InvalidOperationException($"The map click must place '{siblingTerrain}': {after?["cell"]?.ToJsonString()}");
        if (!string.IsNullOrEmpty(terrainBefore) && JsonNodeHelpers.ReadString(after, "cell", "underTerrainDefName") != terrainBefore)
            throw new InvalidOperationException($"The floor must keep '{terrainBefore}' as the terrain under it: {after?["cell"]?.ToJsonString()}");

        // Escape deselects the designator when no window takes it.
        await CallAsync("cancel", "rimworld/press_cancel", new { }, "Pressing cancel with the floor selected");
        var afterCancel = await CallAsync("state_after_cancel", "rimworld/get_designator_state", new { }, "Reading the designator state after cancel");
        if (JsonNodeHelpers.ReadBoolean(afterCancel, "designatorState", "hasSelection") != false)
            throw new InvalidOperationException($"rimworld/press_cancel must deselect the designator: {afterCancel?["designatorState"]?.ToJsonString()}");
    }

    // Called from the mod configuration scenario. ModsConfig.TryReorder inserts before its second argument, so a move
    // down by one changed nothing before the fix.
    private static async Task RunModReorderDownCheckAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        var stepIndex = 0;
        string Step(string name) => $"mod_configuration.down_{name}_{++stepIndex}";
        var status = await context.CallGameToolAsync(Step("status"), "rimworld/get_mod_configuration_status", new { }, cancellationToken);
        context.EnsureSucceeded(status, "Reading the mod configuration status");
        var baselineHash = ReadRequiredString(status.StructuredContent, "currentConfigurationHash");
        var ordered = JsonNodeHelpers.ReadArray(status.StructuredContent, "activeMods")
            .OrderBy(mod => JsonNodeHelpers.ReadInt32(mod, "activeLoadOrder") ?? int.MaxValue)
            .ToList();

        for (var index = 0; index < ordered.Count - 1; index++)
        {
            var modId = ReadRequiredString(ordered[index], "modId");
            var down = await context.CallGameToolAsync(Step("move"), "rimworld/reorder_mod", new { modId, targetIndex = index + 1, save = false }, cancellationToken);
            if (!down.Success || ReplySuccess(down.StructuredContent) != true)
                continue;

            if (JsonNodeHelpers.ReadInt32(down.StructuredContent, "currentIndex") != index + 1)
                throw new InvalidOperationException($"Moving '{modId}' from {index} to {index + 1} must put it at {index + 1}: {down.StructuredContent?.ToJsonString()}");

            var back = await context.CallGameToolAsync(Step("restore"), "rimworld/reorder_mod", new { modId, targetIndex = index, save = false }, cancellationToken);
            context.EnsureSucceeded(back, $"Moving '{modId}' back to {index}");
            if (JsonNodeHelpers.ReadInt32(back.StructuredContent, "currentIndex") != index)
                throw new InvalidOperationException($"Moving '{modId}' back must put it at {index}: {back.StructuredContent?.ToJsonString()}");

            var restored = await context.CallGameToolAsync(Step("status_after"), "rimworld/get_mod_configuration_status", new { }, cancellationToken);
            context.EnsureSucceeded(restored, "Reading the mod configuration status after the move");
            if (ReadRequiredString(restored.StructuredContent, "currentConfigurationHash") != baselineHash)
                throw new InvalidOperationException("Moving a mod down and back must restore the configuration hash.");
            context.SetSummaryValue("movedDownModId", modId);
            return;
        }

        context.Note("No active mod could move down by one without a load-order conflict, so the move-down check did not run.");
    }

    private static async Task RunLoadReadinessAsync(SmokeScenarioContext context, CancellationToken cancellationToken)
    {
        const string saveName = "rimbridge_load_readiness";
        await context.EnsurePlayableGameAsync(cancellationToken);
        await context.WaitForLongEventIdleAsync("load_readiness.wait_for_long_event_idle", cancellationToken);

        var pause = await context.CallGameToolAsync("load_readiness.pause", "rimworld/pause_game", new { pause = true }, cancellationToken);
        context.EnsureSucceeded(pause, "Pausing the game");
        var save = await context.CallGameToolAsync("load_readiness.save", "rimworld/save_game", new { saveName }, cancellationToken);
        context.EnsureSucceeded(save, "Saving the game");
        if (ReplySuccess(save.StructuredContent) == false)
            throw new InvalidOperationException($"Saving must succeed: {save.StructuredContent?.ToJsonString()}");

        var play = await context.CallGameToolAsync("load_readiness.play", "rimworld/play_for", new { durationMs = 2000, speed = "Superfast" }, cancellationToken);
        context.EnsureSucceeded(play, "Playing on after the save");
        var savedTick = JsonNodeHelpers.ReadInt64(play.StructuredContent, "startTick") ?? throw new InvalidOperationException("play_for must report startTick.");
        var runningTick = JsonNodeHelpers.ReadInt64(play.StructuredContent, "endTick") ?? throw new InvalidOperationException("play_for must report endTick.");
        if (runningTick < savedTick + 100)
            throw new InvalidOperationException($"The game must run on after the save, but went from tick {savedTick} to {runningTick}.");

        // The default readiness is mapData. The game that was running still has its maps until the load starts.
        var load = await context.CallGameToolAsync("load_readiness.load", "rimworld/load_game_ready", new { saveName, timeoutMs = 120000, pollIntervalMs = 50 }, cancellationToken);
        context.EnsureSucceeded(load, "Loading the save with the default readiness");
        var loadedTick = JsonNodeHelpers.ReadInt64(load.StructuredContent, "state", "tick");
        if (ReplySuccess(load.StructuredContent) != true || loadedTick == null || loadedTick >= runningTick)
            throw new InvalidOperationException($"The load reply must describe the loaded game at about tick {savedTick}, not the running game at tick {runningTick}: {load.StructuredContent?["state"]?.ToJsonString()}");

        await context.WaitForGameLoadedAsync("load_readiness.wait_visual", cancellationToken);
        context.SetSummaryValue("savedTick", savedTick.ToString(System.Globalization.CultureInfo.InvariantCulture));
        context.SetSummaryValue("loadedTick", loadedTick.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
