# Agent Friction Fix Plan

Source: four Claude sub-agents building a colony through GABS (see `docs/token-efficiency-review.md`, revision 4). Ordered by how often the friction occurred and what it cost.

| # | Friction (runs affected) | Root cause | Fix |
|---|---|---|---|
| 1 | Zones merge across rectangles and zone types (4/4) | `Designator_ZoneAdd.SelectedZone` returns whatever zone is selected; RimWorld selects every new zone, and the bridge treated that as an explicit `set_zone_target` | Remember explicit targets set through `set_zone_target`; otherwise deselect the selected zone before applying so a new zone is created. Report the resulting zone id, label and whether it was created or expanded |
| 2 | `rb.call(id, {})` rejected (4/4) | An empty Lua table compiles to an empty list, which the argument check rejects | Accept an empty list as an empty argument object |
| 3 | Lua reports overflow (6 of 7 RimBridge overflows) | Every step row (ids, timestamps, operation ids, results) and print entry is returned even with `includeStepResults:false`; `apply_architect_designator` results inside scripts list every accepted cell and the full designator state | With `includeStepResults:false` return counts, failed steps and prints only; compact step rows and print entries; budget step lists for direct agent calls; compact `apply_architect_designator` results (cell lists only when small, selected designator only) |
| 4 | Lua dialect gaps (4/4) | Missing fields fail instead of being nil before `or`; no `..`; two-argument `print` needs a literal first argument; 1000-statement default | Missing fields resolve to nil on the left of `or` and in nil comparisons; add string concatenation; accept any first `print` argument; raise the default statement budget to 10000 |
| 5 | Plans invisible (4/4) | No tool reports `map.planManager` | New `rimworld/list_plans` (id, label, color, cell count, bounds); plan reference in cell info |
| 6 | Wood only, default rotation (3/4) | `apply_architect_designator` has no material or rotation | New `stuffDefName` and `rotation` parameters, validated against the buildable |
| 7 | First debug-action search times out (4/4) | The debug-action tree is prepared lazily; the first search expands every submenu on the main thread | Breadth-first search under a 5 s budget; returns `searchComplete:false` with a hint, and repeated searches continue deeper because prepared nodes stay cached |
| 8 | Debug tools stay active (3/4) | `press_cancel` only addresses windows; nothing reports `DebugTools.curTool`; `right_click_cell` needs a selected pawn | `press_cancel` also ends an active debug tool; `get_ui_state` reports it; the `right_click_cell` error points to `click_cell` with `button: "right"` |
| 9 | Pop-up menus unreadable (3/4) | `get_context_menu_options` and `execute_context_menu_option` only know bridge-opened debug menus; menus opened by gizmos or UI clicks open off-screen and vanish because the real mouse is far away | Adopt the open `FloatMenu` (keep it open, move it on-screen); `execute_gizmo` and `click_ui_target` return its options; label resolution prefers prefix matches and lists candidates when ambiguous |
| 10 | Exceptions block GABS (2/4) | `spawn_thing` uses `ThingDef.Named` (logs an error, returns null, then NRE); `GetInspectTabTypes` dereferences null tabs | Silent def lookup with close-match suggestions; null guards |
| 11 | Large discovery results (2/4) | `list_architect_designators` returns about 1 KB per designator (220K for floors) | Compact entries plus response budget paging |
| 12 | Guessed parameters (transcripts) | Unknown arguments are silently ignored | Operation warning naming the unknown arguments and the valid parameters |

Out of scope here (other repositories): GABS `games_tool_names` brief listing size and prefix filter, GABP budget negotiation and the oversized-result file spill.

Verification: unit tests for the Lua compiler and runner changes, build and deploy to the offline profile, then live checks of each fix through GABS on the `token-bench-test` save.

## Status (implemented on branch `token-efficiency`)

All twelve items are implemented. Unit tests pass (Core 178, including four new Lua tests). Live checks on the offline RimWorld 1.6 profile with `token-bench-test`:

| # | Live result |
|---|---|
| 1 | Growing, stockpile and second growing rectangles became three zones, each reported `created: true` |
| 2, 4 | `rb.call(id, {})`, `t.missing or "defaulted"`, `"zones: " .. count` and non-literal `print` all work |
| 3 | A five-call script with `includeStepResults:false` returned about 800 characters; failed steps only, compact rows otherwise |
| 5 | `list_plans` returns `Plan_0` "New plan 1", bounds x 96-135, z 83-115, shape `rectangle_outline` |
| 6 | Steel bed facing east and granite walls built; invalid material lists the allowed ones; cell info now reports rotation |
| 7 | "roof" search answered in 5 s (`searchComplete:false`, 1214 nodes searched) and found "Edit roof (rect)..." |
| 8 | Activating a roof rect tool shows it in `get_ui_state.activeDebugTool`; `press_cancel` ends it |
| 9 | Crop gizmo opens a 16-option menu that stays open; `execute_context_menu_option` "corn" picks "Corn plant" |
| 10 | `spawn_thing` "Eggchicken" fails cleanly with suggestions (`EggChickenUnfertilized`, ...); selecting a cooler no longer throws |
| 11 | Furniture category: 52 compact entries with size, rotation and material support |
| 12 | Unknown arguments in script calls produce an `arguments.unknown` warning listing the valid parameters |

Additional compaction found during verification: nested `state` blocks (for example in `load_game_ready` and script step results) are compacted like the top-level one, and `execute_gizmo`/`click_ui_target` no longer return two full UI snapshots.

## Deviations and limits

- Item 7 first used a per-frame warm-up of the whole tree. Measured on the test profile it prepared 90,674 nodes in 73 s of main-thread time with single steps up to 2.6 s, which made the game stutter for over a minute after every load. Replaced by the time-bounded breadth-first search.
- Item 12 only reaches calls that go through the capability registry with raw arguments (Lua and JSON scripts, SDK companion calls, extension tools). Built-in tools called through GABS are bound by Lib.GAB reflection, which drops unknown keys after writing a trace line; surfacing them to agents needs a Lib.GAB change.
- The installed `rimbridge-server` skill was not regenerated (`scripts/install-skills.sh`): it is shared with Codex, which uses the main-branch build.

