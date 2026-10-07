# Tool Response Token Efficiency Review

Status: revision 4 (adds an agent A/B test at the end). Revision 3: Revision 1 was static source analysis, revision 2 added real Codex transcript evidence, revision 3 adds a prototype on branch `token-efficiency` and a measured before/after benchmark (see "Prototype and benchmark" at the end).

## Evidence base

- 52 Codex sessions (2026-05-15 to 2026-07-10) using GABS with RimBridgeServer: 8,453 `games_call_tool` calls, 5,823 of them to RimBridgeServer-owned `rimworld/*` and `rimbridge/*` tools; the rest are companion tools, mostly `zombieland/*`.
- No Claude Code session used GABS, so all evidence is from Codex (gpt-5.5 era).
- Tool output the model saw: 41.3M chars. Estimated true output before truncation: 95.8M chars. 217 calls (2.6%) were truncated, losing about 54M chars.
- Sizes below are characters of model-visible JSON; Codex estimates about 4 characters per token.

## Who truncates

Codex, not GABS, Lib.GAB or RimBridgeServer.

- Codex's own raw MCP result event (`mcp_tool_call_end`) holds the complete GABS result (57-68K chars, no marker).
- The model-visible `function_call_output` for the same call is capped at about 48,024 chars and contains `…N tokens truncated…`.
- Turns record `truncation_policy: {"mode":"tokens","limit":10000}`. The cap of about 48K chars is consistent with 10,000 tokens at about 4 bytes per token plus headroom (inference, not verified in Codex source).
- Codex removes the middle of the output and keeps head and tail. The JSON becomes invalid, and the middle of a list is lost. The tail, with `"success":true` and the envelope, survives, so the result looks complete.
- Agents notice but work around it, for example: "the returned payload is large enough that the chat transcript truncated the middle … I'm rerunning compact batches", or "the returned layout is huge and partly truncated. I'm going to use the source order to choose a tighter scroll position".

Above about 10K tokens, size is therefore a correctness problem, not only a cost problem: the agent acts on partial data.

Side note: `~/.codex/config.toml` has `tool_output_token_limit = "2000"` inside the `[notice.model_migrations]` table, so it is not a top-level setting and does not take effect.

## Can GABS detect the client's limit?

Not reliably.

- MCP `initialize` carries only `clientInfo` (name, version) and capabilities. There is no field for a tool-result budget.
- The limit is client policy: it depends on the model family, user configuration and profiles, and the client version, and none of these reach the server.
- Codex spawns GABS with a minimal environment (observed: `HOME LANG LOGNAME PATH SHELL TERM TMPDIR USER`), so client settings are not visible through environment variables. Claude Code's `MAX_MCP_OUTPUT_TOKENS` would be visible only if the user set it in GABS's environment.
- GABP `tools/call` params are `additionalProperties: false` (`GABP/SCHEMA/1.0/methods/tools.call.request.json`), so passing a budget needs a protocol addition.

Proposed design: an explicit budget with a conservative default, plus a safety net.

1. Budget resolution in GABS: per-call override, then GABS config or an env var set in the MCP registration (`[mcp_servers.gabs.env]` in Codex), then a `clientInfo` lookup table as a hint only, then a conservative default of about 8K tokens (about 32K chars), below both known client caps (Codex 10K, Claude Code 25K by default).
2. GABP: add an optional field to `tools/call` params, for example `limits: { maxResultChars }`. Lib.GAB exposes it to handlers as ambient call context.
3. RimBridgeServer: list-shaped tools use the budget for paging, visible-only filtering or summaries, and always report what was omitted plus the cursor or parameter that gets it.
4. GABS safety net for every game and companion: if a result still exceeds the budget, write the full JSON to a file and return valid JSON with `truncated: true`, the file path, sizes and the top-level keys, instead of letting the client cut the middle. Agents can extract from the file with `jq`.

## Measured consumers

True size is visible size plus the truncated tokens times 4.

| Tool | Calls | Visible total | Median true | Max true | Truncated |
|---|---|---|---|---|---|
| `rimworld/get_ui_layout` | 228 | 6.9M | 33K | 322K | 94 (41%) |
| `rimworld/load_game_ready` | 1,409 | 3.0M | 2.1K | 7K | 0 |
| `rimworld/get_mod_configuration_status` | 157 | 2.3M | 11K | 111K | 6 |
| `rimbridge/list_logs` | 1,132 | 1.9M | 0.4K | 86K | 3 |
| `rimworld/list_mods` | 46 | 1.3M | 27K | 203K | 12 |
| `rimworld/search_debug_actions` | 71 | 1.1M | 8K | 88K | 9 |
| `rimworld/get_cells_info` | 36 | 0.9M | 23K | 236K | 11 |
| `rimworld/list_saves` | 52 | 0.6M | 15K | 21K | 0 |
| `rimbridge/get_lua_reference` | 20 | 0.5M | 25K | 25K | 0 |
| `rimbridge/list_capabilities` | 21 | 0.4M | 7K | 176K | 6 |
| `rimbridge/get_operation` | 8 | 0.3M | 149K | 437K | 5 |
| `rimbridge/run_lua` | 16 | 0.3M | 9K | 66K | 2 |
| `rimworld/take_screenshot` | 125 | 0.3M | 2.3K | 10K | 0 |

Companion tools are as large or larger: `zombieland/albino_realtime_until_dead` has a median of 867K chars and a maximum of 3.3M, with 28 of 39 calls truncated. That is outside this repository, but the GABS safety net and an SDK budget API would cover it.

GABS discovery is significant too: `games_tool_names` was called 3,896 times (7.8M chars, median 520), and `games_tool_detail` 1,074 times (1.0M).

`get_ui_layout`: 85% of calls passed no `surfaceId` (median 33K). Calls with an explicit `surfaceId` are larger (median 180K) because big mod settings windows include every off-screen element of their scroll content.

## Overhead breakdown (8,115 parseable responses, 32.7M chars)

| Component | Share | Notes |
|---|---|---|
| `false` fields | 12.3% | often meaningful; omit only where documented |
| `null` fields | 8.3% | safe to omit |
| `0` fields | 3.8% | context dependent |
| empty lists/objects | 2.4% | safe to omit when a count is present |
| empty strings | 1.5% | for example `list_logs` entries: `"ScriptCall":""`, `"StackTrace":""` and six more per entry |
| `operation` envelope | 9.8% | about 400 chars per call |
| `state` snapshot | 5.2% | present in 2,399 responses, about 710 chars each |

Envelope problems seen in practice:

- `"Status":2` is a numeric enum the LLM cannot decode.
- `"Result":null`, `"Metadata":{}`, `"HasResult"`, `"ResultWasTruncated":false`, `"Error":null` and both timestamps are always present.
- The envelope can contradict the payload: `load_game_ready` returned `success:false` ("Save 'EMPTY' does not exist.") while the envelope said `Success:true`.
- Field casing is mixed: the envelope and log entries use PascalCase, payloads camelCase.

## Corrections to revision 1

- The argument echo does not exist in replies. `metadata.arguments` goes only to the journal (`CapabilityRegistry.cs:136`); live replies show `"Metadata":{}`.
- The state snapshot is about 710 chars, not about 120 tokens' worth of short fields; it is still useful, but compacting it matters more than estimated.
- The screenshot target metadata is small in practice (`take_screenshot` median 2.3K). Keeping `includeTargets=true` is confirmed.
- Script reports are not a major consumer in practice (16 `run_lua` calls); the duplication fix remains cheap but low priority.
- `list_capabilities` is rare, but when called without filters it is truncated and loses most of the descriptors.

## Downsides of reducing information (unchanged)

1. Round trips cost more than bytes, especially while the game runs.
2. State on every reply anchors the LLM in time; the game tick is missing.
3. Absence reads as nonexistence; every reduction must say what was omitted and how to get it.
4. Inline data is read more reliably than ID references within one payload.
5. Failures need detail; be terse on success.
6. Lua/JSON scripts, SDK companions and the live-smoke harness read capability payloads; only the outer envelope layer is LLM-exclusive.

## Revised recommendation

### Priority 1: stop silent truncation (correctness)

- Introduce a result budget (default about 32K chars) and make the largest list tools honor it with paging plus an explicit `omitted`/`nextCursor` report: `get_ui_layout`, `list_mods`, `get_mod_configuration_status`, `get_cells_info`, `search_debug_actions`, `list_capabilities`, `list_logs`, `get_operation`.
- `get_ui_layout`: default to elements that are visible inside their scroll viewport, report the off-screen element count, and offer an `includeOffscreen` or range parameter. Keep one rounded rectangle per element; omit default-valued flags.
- `get_operation`: its "bounded" retained result reached 437K chars; apply the same budget.
- Add the GABS file-spill safety net (separate repo) so companions are covered too.

### Priority 2: outer-layer cleanup (no script or companion impact)

- Envelope: keep `operationId`, a string status, `durationMs`, and warnings/error only when present. Drop `Result`, `Metadata`, `HasResult`, `ResultWasTruncated` (when false) and both timestamps. Make the envelope success agree with the payload `success`, or drop the envelope success field.
- Omit `null` values and empty strings in the serialized LLM-facing output. `list_logs` entries alone carry eight empty-string fields each.
- State: keep it on every reply but compact: game tick, paused, speed, program state, long-event flag, readiness as one level. Add it in the outer layer so coverage is uniform.

### Priority 3: frequent mid-size responses

- `load_game_ready` (1,409 calls) and `start_debug_game_ready`: return a compact success summary; detail on failure.
- `get_mod_configuration_status` (157 calls, median 11K): compact entries keeping `rootDir`; include `loadedSessionMods` only when it differs from `activeMods`.
- `list_mods`: compact entries, inactive mods still listed, details via filter.
- `list_saves`: drop the per-save `path`; consider a limit with newest first.

### Priority 4: reference documents

- `get_lua_reference` (25K) and `get_script_reference` (21K) were each fetched 10-20 times. Offer a compact quick-start section by default with full detail on request, or move them to skill files that the agent reads once.

### Leave as is

- Screenshot `includeTargets=true`, letter text, `includeStepResults` defaults (adaptive behavior is still a good idea, but low volume).

Suggested order: measurement hook in the journal, then priority 1 (with the Lib.GAB/GABP budget plumbing in a separate change), then priority 2, then 3 and 4.

## Prototype and benchmark (revision 3)

### Setup

- Branch `token-efficiency` (worktree), based on `b480660`. Unit tests pass (`scripts/build-mod.sh --test`). The live smoke suite was not run.
- Offline RimWorld 1.6 (`~/Library/Application Support/OlderRimWorlds`, GABS entry `rimworld-local`) driven through a private GABS instance; mods: Harmony, RimBridgeServer, all DLCs, Zombieland (plus Achtung and Zombieland companion tools).
- A fixed save (`token-bench`) and a 49-call scenario replaying the most frequent and the heaviest real call shapes from the Codex transcripts. Two baseline runs differed by 0.14% (new log lines only), so the comparison is deterministic.

### What the prototype changes

Outer, agent-only layer (`Source/AgentResponseShaper.cs`; scripts and companion tools unaffected):

- Envelope reduced to `operationId`, string `status`, `durationMs`, plus `warnings`/`error` only when present.
- `null` and empty-string fields removed. `false`, `0` and empty lists are kept, because absence would read as "unknown" or "none".
- Compact `state` on every reply: `tick`, `paused`, `speed`, `program`, `map`, highest satisfied readiness level as `ready`, and `longEventPending` only when true. Replies without a payload state get `tick`/`paused`/`speed`/`program` from plain field reads.

Budget and paging (`Source/ResponseBudget.cs`):

- Direct agent calls get a 30,000-character budget for list-shaped results. Items are measured as serialized without nulls, at least one item is always returned, and a `page` object reports `offset`, `returned`, `total`, `nextOffset` and a hint.
- Calls nested in scripts or companion tools (any call with a parent operation) stay unbounded. Verified: a Lua script calling a 32x32 `get_cells_info` received all 1024 cells.
- New `offset` parameter on `get_cells_info`, `get_ui_layout`, `list_mods`, `list_saves`, `list_capabilities`, `search_debug_actions`.

Tool-specific changes:

- `get_ui_layout`: elements scrolled outside their scroll viewport are omitted and counted (`offscreenElementCount`, new `includeOffscreen` parameter). The scroll content root is the sibling drawn directly after the scroll view; the parent chain does not reach the scroll view itself. Label-less, non-actionable `spacing`/`slot` filler is omitted and counted. Each element keeps one rounded `screenRect`.
- `get_cells_info` (rectangle only; the single-cell tool is unchanged for the smoke suite): zones and areas are described once at the top level, and each cell keeps id plus label inline. Derived per-cell def lists are dropped, empty per-cell lists omitted, counts kept.
- `list_mods`: compact entries (identity, state, order, problem fields only when present); new `includeDetails` parameter for full metadata.
- `get_mod_configuration_status` (also embedded in `set_mod_enabled`/`reorder_mod`): compact entries that keep `rootDir`; new `loadedSessionMatchesActive`, with `loadedSessionMods` included only when it differs; the redundant id lists are gone.
- `list_capabilities`: `includeParameters` defaults to false.
- `list_saves`: newest first, no per-save `path` (`saveFolder` plus name).
- `run_lua`/`run_script`: `result`, `error` and `output` no longer duplicated inside `script`.

### Results

| Metric | Baseline | Prototype |
|---|---|---|
| Scenario total (49 calls) | 1,406,222 chars | 392,973 chars (-72%) |
| Responses over the Codex cap (~48K) | 5 | 0 |
| Largest single response | 454,490 | 32,524 (paged) |

Selected calls (characters):

| Call | Before | After | Note |
|---|---|---|---|
| `get_cells_info` 32x32 | 454,490 | 30,379 first page; 175,645 for all 6 pages | before: one reply Codex would cut to about 48K |
| `get_ui_layout`, Zombieland settings window | 263,501 | 26,224 | all 114 visible elements in one page; 488 off-screen, 141 filler counted |
| `get_ui_layout` with `includeOffscreen` | n/a | 147,037 over 5 pages | complete layout still reachable |
| `list_capabilities`, defaults | 181,308 | 30,550 first page; 123,589 for all 200 | parameters on request |
| `get_mod_configuration_status` | 19,255 | 3,677 | `rootDir` retained |
| `list_mods` | 34,642 | 8,870 | 34 mods in this profile |
| `get_ui_layout`, Work tab | 48,708 | 25,369 | |
| `get_operation` (retained cells result) | 30,287 | 12,655 | |
| `wait_for_game_loaded` | 957 | 367 | now includes the game tick |
| `play_for` / `step_game_ticks` | 1,330 / 1,215 | 639 / 672 | |
| `list_saves` (18 saves) | 21,535 | 19,037 | compatibility details dominate |
| `get_lua_reference` / `get_script_reference` | 24,562 / 21,517 | 23,453 / 20,471 | unchanged design |

Small calls shrink by 10-60% from envelope and state cleanup alone, which matters most for the frequent tools (`load_game_ready` -26%, `list_logs` -34%).

### Information checks

- Compact state carries the tick: 63108 before and 63168 after `step_game_ticks` with 60 ticks.
- Mod status keeps `rootDir`, hashes and the session-match flag the AGENTS.md workflow and smoke suite rely on.
- Compact cells keep terrain, roof, fog, walkability, things with hit points, designations, zone and area references.
- Every paged list was fetched completely through `nextOffset` (items returned equal totals).

### Open issues and next steps

1. Run the live smoke suite. It reads `StructuredContent`, which now passes through the shaper; tolerant reads should be fine, but this is unverified.
2. GABS/GABP: pass a client budget into Lib.GAB (replace the fixed 30K default) and add the file-spill safety net for companion tools such as Zombieland, which remain far over the cap.
3. Agents guess parameters that do not exist (`list_mods` with `enabledOnly`/`onlyEnabled`, `get_ui_layout` with `includeRects`/`includeActions`/`includeAllText`). Consider reporting unknown arguments as a warning so the agent learns the real surface.
4. `search_debug_actions` matches embed full debug nodes (about 900 chars each). Compacting them needs the smoke suite's `node.source.name` and map-target assertions adjusted.
5. `list_capabilities` descriptors are still about 600 chars without parameters (title, summary, modes, result type); compacting them is low priority.
6. Reference documents (`get_lua_reference`, `get_script_reference`) at 20-24K: offer a short quick-start by default.
7. Rebuilds change the tracked `Contracts`/`Core`/`Sdk`/`Extensions.Abstractions` DLLs without source changes (worktree path); leave them out of a commit unless intended.

### Operational lessons from this run

- iCloud "Optimize Mac Storage" had offloaded 15,047 files of the offline install under `~/Documents`; RimWorld then appeared to hang on "Initializing…" while macOS fetched files one by one (about 2 s each). The install now lives in `~/Library/Application Support/OlderRimWorlds`; GABS, the launchers, Codex trust entries and memories, the DecompilerServer context and `ZeFlammenwerfer/scripts/run-tank-pipe-evidence.sh` point there.
- GABS entries that share `stopProcessName` "RimWorld by Ludeon Studios" cannot distinguish parallel instances: GABS refused to start the offline game while two Steam instances ran, and a stop by name could hit the wrong game. A private GABS config without `stopProcessName` tracks the launched process by pid.
- `dotnet build` leaves the C# compiler server running for about ten minutes with inherited stdout; piping build output into another command then blocks until it exits. Redirect build output to a file instead.

## Agent A/B test (revision 4)

### Setup

- Task (user-written): build a complete colony inside the marked area of save `token-bench-test` with dev tools and other means, take a screenshot, save under a new name, report the time.
- Four fresh Claude Code sub-agents (Opus 5.5, high effort) with an identical prompt plus a fixed environment note (use only GABS game `rimworld-local`, use the `rimbridge-server` skill). Order old, new, new, old (ABBA). Fresh game start before each run; the original save was verified unchanged after all runs.
- Claude Code itself persists tool results above its limit to a file and shows the agent only a notice; agents then extract with `jq`/`grep`.
- Four blind reviewers (timelines renamed P/Q/R/S) analyzed each transcript for difficulties with tool results.

### Numbers

| Run | Minutes | Tool calls (bridge) | Agent tokens | Bridge chars | Per bridge call | Overflowed results (RimBridge) |
|---|---|---|---|---|---|---|
| old1 | 14.2 | 128 (66) | 299K | 326K | 4.9K | 4 (2) |
| new1 | 17.5 | 169 (100) | 376K | 360K | 3.6K | 1 (1) |
| new2 | 12.2 | 102 (57) | 276K | 263K | 4.6K | 4 (2) |
| old2 | 12.2 | 115 (70) | 324K | 356K | 5.1K | 2 (2) |

Bridge character counts undercount overflowed results (only the notice was counted).

- Excluding Lua script reports, bridge output per call fell 25% (4.2K to 3.2K).
- Run time and tokens did not differ measurably between versions. The spread inside each version (new1 built far more: granite, roofs, 6 bedrooms, 3 extra colonists) exceeds the difference.
- Unscoped `get_ui_layout` calls during the material-menu detour returned about 39.5K in both old runs; in the new runs layout results stayed small.
- No run reached a budget-paged result, so the paging mechanism was not exercised by agents in this task.

### What the agents struggled with (all four runs unless noted)

Response size:
1. `run_lua`/`run_lua_file` reports are the dominant remaining size problem: 6 of 7 RimBridge overflows (54K-110K). Even with `includeStepResults:false` every step row and print entry comes back.
2. `list_architect_designators` for floors: 220K (new2), not yet budgeted.
3. GABS `games_tool_names` with `brief:true, limit:200` still returned 74K (two runs overflowed); without `brief` about 48K; `prefix` filtering did not work; `nextCursor` was mostly ignored. 17-23 `games_tool_detail` calls per run.
4. `list_saves` 20-23K just to confirm one save name.

Semantic gaps and bugs (cost 1-4 minutes per run, independent of the version):
1. The marked area is a Plan, which no tool exposes; every run fell back to screenshots read as images, pixel analysis or clicking cells.
2. Zone designators add to the previously used zone; four rectangles merged into one zone in every run, and the apply results did not show it.
3. `apply_architect_designator` has no material or rotation parameter; three runs detoured through the Architect UI (2-3.5 min), ending with wood or with Zombieland companion tools.
4. `get_context_menu_options` only covers debug menus, not the game's FloatMenu (misread in three runs).
5. The first `search_debug_actions` call hit the 30 s GABP timeout in every run.
6. Debug rect tools stay active; `press_cancel` does not end them, no tool reports debug-tool state, and `right_click_cell` only works with a selected pawn (three runs).
7. Lua: `rb.call(id, {})` is rejected ("arguments must be a table or nil") in all four runs; `rb.print` needs a literal first argument; no `or` defaults for missing fields; no string concatenation; 1000-statement limit hit twice.
8. `spawn_thing` with an unknown def throws a NullReferenceException; `get_selection_semantics` throws for a selected cooler; both trigger the GABS attention block. Errors also auto-open the debug log window, which pollutes screenshots.

### Conclusions

- Response trimming works as intended per call and removed the large layout replies, but it is not what limits agent speed on a building task. The time sinks are semantic gaps, Lua dialect limits, discovery overhead and timeouts.
- Next steps by expected payoff:
  1. Lua reports: summary-only output when `includeStepResults:false` (counts, failures, prints), budget for step lists.
  2. Fix `rb.call` with `{}`.
  3. Zone designators: start a new zone by default (or report merges).
  4. Material and rotation on `apply_architect_designator`.
  5. Plans in `list_areas`/`get_cell_info` or a `list_plans` tool.
  6. Debug-tool state and cancel; clarify `right_click_cell`.
  7. FloatMenu support in context-menu tools.
  8. Warm the debug-action index at startup or raise the timeout.
  9. Clean errors for unknown defs and selection semantics.
  10. Budget `list_architect_designators`.
  11. GABS: smaller brief listings, working prefix filter.
