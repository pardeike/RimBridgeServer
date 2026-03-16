using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Lib.GAB.Tools;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

public class RimBridgeTools
{
    [Tool("rimbridge/ping", Description = "Connectivity test. Returns 'pong'.")]
    public object Ping()
    {
        return RunBackgroundTool(() => new { message = "pong", timestamp = DateTime.UtcNow });
    }

    [Tool("rimworld/get_game_info", Description = "Get basic information about the current RimWorld game")]
    public object GetGameInfo()
    {
        return RunTool(() =>
        {
            var currentGame = Current.Game;
            if (currentGame == null)
            {
                return (object)new { status = "no_game", message = "No game is currently loaded" };
            }

            return (object)new
            {
                status = "game_loaded",
                ticksGame = currentGame.tickManager.TicksGame,
                mapCount = currentGame.Maps?.Count ?? 0,
                selectedPawns = Find.Selector.SelectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList()
            };
        });
    }

    [Tool("rimworld/pause_game", Description = "Pause or unpause the game")]
    public object PauseGame([ToolParameter(Description = "True to pause, false to unpause")] bool pause = true)
    {
        return RunTool(() =>
        {
            if (Current.Game == null)
            {
                return new { success = false, message = "No game is currently loaded" };
            }

            var currentlyPaused = Find.TickManager.Paused;
            if (pause && !currentlyPaused)
                Find.TickManager.TogglePaused();
            else if (!pause && currentlyPaused)
                Find.TickManager.TogglePaused();

            return new
            {
                success = true,
                paused = Find.TickManager.Paused,
                message = pause ? "Game paused" : "Game unpaused"
            };
        });
    }

    [Tool("rimworld/start_debug_game", Description = "Start RimWorld's built-in quick test colony from the main menu")]
    public object StartDebugGame()
    {
        return RunTool(() =>
        {
            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                return new
                {
                    success = false,
                    message = "RimWorld is busy with another long event. Wait for it to finish before starting a debug game.",
                    state = RimWorldState.ToolStateSnapshot()
                };
            }

            if (!GenScene.InEntryScene || Current.ProgramState != ProgramState.Entry)
            {
                return new
                {
                    success = false,
                    message = "Debug game start is only supported from the main menu entry scene.",
                    state = RimWorldState.ToolStateSnapshot()
                };
            }

            if (Current.Game != null)
            {
                return new
                {
                    success = false,
                    message = "A game is already loaded. Return to the main menu before starting a new debug game.",
                    state = RimWorldState.ToolStateSnapshot()
                };
            }

            LongEventHandler.QueueLongEvent(delegate
            {
                Root_Play.SetupForQuickTestPlay();
                PageUtility.InitGameStart();
            }, "GeneratingMap", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);

            return new
            {
                success = true,
                status = "queued",
                message = "Queued RimWorld quick test start.",
                scenario = ScenarioDefOf.Crashlanded.defName,
                storyteller = StorytellerDefOf.Cassandra.defName,
                difficulty = DifficultyDefOf.Rough.defName,
                mapSize = 250,
                state = RimWorldState.ToolStateSnapshot()
            };
        });
    }

    [Tool("rimworld/list_colonists", Description = "List player-controlled colonists available for selection and drafting")]
    public object ListColonists([ToolParameter(Description = "True to only include the current map")] bool currentMapOnly = false)
    {
        return RunTool(() =>
        {
            if (Current.Game == null)
                return new { success = false, message = "No game is currently loaded" };

            var pawns = currentMapOnly
                ? RimWorldState.CurrentMapColonists(RimWorldState.CurrentMapOrThrow())
                : RimWorldState.AllPlayerColonists();

            return new
            {
                success = true,
                count = pawns.Count,
                colonists = pawns.Select(RimWorldState.DescribePawn).ToList()
            };
        });
    }

    [Tool("rimworld/clear_selection", Description = "Clear the current map selection")]
    public object ClearSelection()
    {
        return RunTool(() =>
        {
            Find.Selector.ClearSelection();
            return new { success = true, selectedCount = Find.Selector.NumSelected };
        });
    }

    [Tool("rimworld/select_pawn", Description = "Select a single colonist by name")]
    public object SelectPawn(
        [ToolParameter(Description = "Colonist name, short name, or full name")] string pawnName,
        [ToolParameter(Description = "True to append to the current selection instead of replacing it")] bool append = false)
    {
        return RunTool(() =>
        {
            var pawn = RimWorldState.ResolveColonist(pawnName);
            if (!append)
                Find.Selector.ClearSelection();

            Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);

            return new
            {
                success = true,
                selected = RimWorldState.DescribePawn(pawn),
                selectedCount = Find.Selector.NumSelected
            };
        });
    }

    [Tool("rimworld/deselect_pawn", Description = "Deselect a single selected pawn by name")]
    public object DeselectPawn([ToolParameter(Description = "Selected pawn name")] string pawnName)
    {
        return RunTool(() =>
        {
            var pawn = RimWorldState.ResolveSelectedPawn(pawnName);
            Find.Selector.Deselect(pawn);

            return new
            {
                success = true,
                deselected = pawn.Name?.ToStringShort ?? pawn.LabelShort,
                selectedCount = Find.Selector.NumSelected
            };
        });
    }

    [Tool("rimworld/set_draft", Description = "Draft or undraft a colonist by name")]
    public object SetDraft(
        [ToolParameter(Description = "Colonist name")] string pawnName,
        [ToolParameter(Description = "True to draft, false to undraft")] bool drafted = true)
    {
        return RunTool(() =>
        {
            var pawn = RimWorldState.ResolveColonist(pawnName);
            if (pawn.drafter == null)
                return new { success = false, message = $"Pawn '{pawnName}' cannot be drafted." };

            pawn.drafter.Drafted = drafted;

            return new
            {
                success = true,
                pawn = pawn.Name?.ToStringShort ?? pawn.LabelShort,
                drafted = pawn.drafter.Drafted
            };
        });
    }

    [Tool("rimworld/get_camera_state", Description = "Get the current map camera position, zoom, and visible rect")]
    public object GetCameraState()
    {
        return RunTool(RimWorldState.DescribeCamera);
    }

    [Tool("rimworld/jump_camera_to_pawn", Description = "Jump the camera to a pawn by name")]
    public object JumpCameraToPawn([ToolParameter(Description = "Pawn name on the current map")] string pawnName)
    {
        return RunTool(() =>
        {
            var pawn = RimWorldState.ResolveCurrentMapPawn(pawnName);
            Find.CameraDriver.JumpToCurrentMapLoc(pawn.Position);
            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
            return new
            {
                success = true,
                target = pawn.Name?.ToStringShort ?? pawn.LabelShort,
                camera = RimWorldState.DescribeCamera()
            };
        });
    }

    [Tool("rimworld/jump_camera_to_cell", Description = "Jump the camera to a map cell")]
    public object JumpCameraToCell(
        [ToolParameter(Description = "Cell x coordinate")] int x,
        [ToolParameter(Description = "Cell z coordinate")] int z)
    {
        return RunTool(() =>
        {
            var cell = new IntVec3(x, 0, z);
            var map = RimWorldState.CurrentMapOrThrow();
            if (!cell.InBounds(map))
                return new { success = false, message = $"Cell ({x}, {z}) is out of bounds for the current map." };

            Find.CameraDriver.JumpToCurrentMapLoc(cell);
            return new { success = true, cell = new { x, z }, camera = RimWorldState.DescribeCamera() };
        });
    }

    [Tool("rimworld/move_camera", Description = "Move the camera by a cell offset")]
    public object MoveCamera(
        [ToolParameter(Description = "Delta x in map cells")] float deltaX,
        [ToolParameter(Description = "Delta z in map cells")] float deltaZ)
    {
        return RunTool(() =>
        {
            var driver = Find.CameraDriver;
            var current = driver.MapPosition;
            var target = new IntVec3(
                Mathf.RoundToInt(current.x + deltaX),
                0,
                Mathf.RoundToInt(current.z + deltaZ));

            var map = RimWorldState.CurrentMapOrThrow();
            target.x = Mathf.Clamp(target.x, 0, map.Size.x - 1);
            target.z = Mathf.Clamp(target.z, 0, map.Size.z - 1);
            driver.JumpToCurrentMapLoc(target);

            return new { success = true, cell = new { x = target.x, z = target.z }, camera = RimWorldState.DescribeCamera() };
        });
    }

    [Tool("rimworld/zoom_camera", Description = "Adjust the current camera zoom/root size")]
    public object ZoomCamera([ToolParameter(Description = "Positive values zoom out, negative values zoom in")] float delta)
    {
        return RunTool(() =>
        {
            var driver = Find.CameraDriver;
            var newSize = Mathf.Clamp(driver.RootSize + delta, 8f, 140f);
            driver.SetRootSize(newSize);

            return new { success = true, rootSize = driver.RootSize, camera = RimWorldState.DescribeCamera() };
        });
    }

    [Tool("rimworld/set_camera_zoom", Description = "Set the current camera root size directly")]
    public object SetCameraZoom([ToolParameter(Description = "Desired camera root size")] float rootSize)
    {
        return RunTool(() =>
        {
            var driver = Find.CameraDriver;
            driver.SetRootSize(Mathf.Clamp(rootSize, 8f, 140f));

            return new { success = true, rootSize = driver.RootSize, camera = RimWorldState.DescribeCamera() };
        });
    }

    [Tool("rimworld/frame_pawns", Description = "Frame a comma-separated list of pawns so they fit in view")]
    public object FramePawns([ToolParameter(Description = "Comma-separated pawn names. If omitted, uses the current selection.")] string pawnNamesCsv = null)
    {
        return RunTool(() =>
        {
            List<Pawn> pawns;
            if (string.IsNullOrWhiteSpace(pawnNamesCsv))
            {
                pawns = Find.Selector.SelectedPawns.Where(pawn => pawn.Spawned).ToList();
            }
            else
            {
                pawns = RimWorldState.ParseNames(pawnNamesCsv)
                    .Select(RimWorldState.ResolveCurrentMapPawn)
                    .Where(pawn => pawn.Spawned)
                    .Distinct()
                    .ToList();
            }

            if (pawns.Count == 0)
                return new { success = false, message = "No spawned pawns were available to frame." };

            var center = new Vector3(
                (float)pawns.Average(pawn => pawn.Position.x) + 0.5f,
                0f,
                (float)pawns.Average(pawn => pawn.Position.z) + 0.5f);
            var size = RimWorldState.ComputeFrameRootSize(pawns);

            Find.CameraDriver.SetRootPosAndSize(center, size);

            return new
            {
                success = true,
                framedPawns = pawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
                rootSize = Find.CameraDriver.RootSize,
                camera = RimWorldState.DescribeCamera()
            };
        });
    }

    [Tool("rimworld/take_screenshot", Description = "Take an in-game screenshot and return the saved file path")]
    public object TakeScreenshot([ToolParameter(Description = "Optional screenshot file name without extension")] string fileName = null)
    {
        return RunBackgroundTool(() =>
        {
            var safeName = RimWorldState.SanitizeName(fileName, "rimbridge");
            var expectedPath = RimBridgeMainThread.Invoke(() =>
            {
                ScreenshotTaker.TakeNonSteamShot(safeName);
                return Path.Combine(GenFilePaths.ScreenshotFolderPath, safeName + ".png");
            }, timeoutMs: 5000);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(expectedPath))
                {
                    var info = new FileInfo(expectedPath);
                    if (info.Length > 0)
                    {
                        return new
                        {
                            success = true,
                            path = expectedPath,
                            screenshotFolder = GenFilePaths.ScreenshotFolderPath,
                            sizeBytes = info.Length
                        };
                    }
                }

                Thread.Sleep(100);
            }

            return new
            {
                success = false,
                message = "Timed out waiting for RimWorld to finish writing the screenshot file.",
                expectedPath
            };
        });
    }

    [Tool("rimworld/get_achtung_state", Description = "Get Achtung-specific debug state when the mod is loaded")]
    public object GetAchtungState()
    {
        return RunTool(() => AchtungIntegration.DescribeState());
    }

    [Tool("rimworld/set_achtung_show_drafted_orders_when_undrafted", Description = "Enable or disable Achtung's compatibility mode that merges drafted-only orders into undrafted menus")]
    public object SetAchtungShowDraftedOrdersWhenUndrafted(
        [ToolParameter(Description = "True to re-enable the old merged-menu behavior, false to use the fixed behavior")] bool enabled)
    {
        return RunTool(() =>
        {
            if (!AchtungIntegration.IsLoaded())
                return new { success = false, message = "Achtung is not loaded." };

            var value = AchtungIntegration.SetShowDraftedOrdersWhenUndrafted(enabled);
            return new
            {
                success = true,
                loaded = true,
                showDraftedOrdersWhenUndrafted = value
            };
        });
    }

    [Tool("rimworld/list_saves", Description = "List saved RimWorld games")]
    public object ListSaves()
    {
        return RunTool(() =>
        {
            var saves = GenFilePaths.AllSavedGameFiles
                .Select(file => new
                {
                    name = Path.GetFileNameWithoutExtension(file.Name),
                    path = file.FullName,
                    lastWriteTimeUtc = file.LastWriteTimeUtc,
                    sizeBytes = file.Length
                })
                .ToList();

            return new
            {
                success = true,
                saveFolder = GenFilePaths.SavedGamesFolderPath,
                count = saves.Count,
                saves
            };
        });
    }

    [Tool("rimworld/spawn_thing", Description = "Spawn a thing on the current map at a target cell")]
    public object SpawnThing(
        [ToolParameter(Description = "ThingDef defName to spawn")] string defName,
        [ToolParameter(Description = "Target cell x coordinate")] int x,
        [ToolParameter(Description = "Target cell z coordinate")] int z,
        [ToolParameter(Description = "Optional stack count. Clamped to the thing's stack limit.")] int stackCount = 1)
    {
        return RunTool(() =>
        {
            var map = RimWorldState.CurrentMapOrThrow();
            var cell = new IntVec3(x, 0, z);
            if (!cell.InBounds(map))
                return new { success = false, message = $"Cell ({x}, {z}) is out of bounds for the current map." };

            ThingDef thingDef;
            try
            {
                thingDef = ThingDef.Named(defName);
            }
            catch (Exception ex)
            {
                return new { success = false, message = $"Could not resolve ThingDef '{defName}'.", exception = ex.Message };
            }

            var thing = ThingMaker.MakeThing(thingDef);
            if (thing.def.stackLimit > 1)
                thing.stackCount = Mathf.Clamp(stackCount, 1, thing.def.stackLimit);

            var spawned = GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
            return new
            {
                success = true,
                defName = spawned.def.defName,
                label = spawned.LabelCap,
                stackCount = spawned.stackCount,
                cell = new { x = cell.x, z = cell.z }
            };
        });
    }

    [Tool("rimworld/save_game", Description = "Save the current game to a named save")]
    public object SaveGame([ToolParameter(Description = "Save name without extension")] string saveName)
    {
        return RunTool(() =>
        {
            if (Current.Game == null)
                return new { success = false, message = "No game is currently loaded." };
            if (GameDataSaveLoader.SavingIsTemporarilyDisabled)
                return new { success = false, message = "RimWorld is temporarily blocking saves because a UI/window state or cutscene prevents saving." };

            var safeName = RimWorldState.SanitizeName(saveName, "rimbridge_save");
            GameDataSaveLoader.SaveGame(safeName);
            var path = GenFilePaths.FilePathForSavedGame(safeName);
            var info = new FileInfo(path);

            return new
            {
                success = true,
                saveName = safeName,
                path,
                exists = info.Exists,
                sizeBytes = info.Exists ? info.Length : 0
            };
        });
    }

    [Tool("rimworld/load_game", Description = "Load a named RimWorld save")]
    public object LoadGame([ToolParameter(Description = "Save name without extension")] string saveName)
    {
        return RunTool(() =>
        {
            var safeName = RimWorldState.SanitizeName(saveName, "rimbridge_save");
            var path = GenFilePaths.FilePathForSavedGame(safeName);
            if (!File.Exists(path))
            {
                return new { success = false, message = $"Save '{safeName}' does not exist.", path };
            }

            GameDataSaveLoader.LoadGame(safeName);

            return new
            {
                success = true,
                status = "queued",
                saveName = safeName,
                path,
                state = RimWorldState.ToolStateSnapshot()
            };
        });
    }

    [Tool("rimworld/open_context_menu", Description = "Open a debug context menu at a target pawn or cell using Achtung when available")]
    public object OpenContextMenu(
        [ToolParameter(Description = "Target pawn name on the current map. Optional if x/z are provided.")] string targetPawnName = null,
        [ToolParameter(Description = "Target cell x coordinate when no pawn name is given")] int x = 0,
        [ToolParameter(Description = "Target cell z coordinate when no pawn name is given")] int z = 0,
        [ToolParameter(Description = "Context menu provider: auto, achtung, or vanilla")] string mode = "auto")
    {
        return RunTool(() =>
        {
            if (Current.Game == null)
                return new { success = false, message = "No game is currently loaded." };

            var selectedPawns = Find.Selector.SelectedPawns.ToList();
            if (selectedPawns.Count == 0)
                return new { success = false, message = "No pawns are currently selected." };

            var map = RimWorldState.CurrentMapOrThrow();
            Pawn targetPawn = null;
            IntVec3 clickCell;
            string targetLabel;

            if (!string.IsNullOrWhiteSpace(targetPawnName))
            {
                targetPawn = RimWorldState.ResolveCurrentMapPawn(targetPawnName);
                if (!targetPawn.Spawned || targetPawn.Map != map)
                    return new { success = false, message = $"Pawn '{targetPawnName}' is not spawned on the current map." };

                clickCell = targetPawn.Position;
                targetLabel = targetPawn.Name?.ToStringShort ?? targetPawn.LabelShort;
            }
            else
            {
                clickCell = new IntVec3(x, 0, z);
                if (!clickCell.InBounds(map))
                    return new { success = false, message = $"Cell ({x}, {z}) is out of bounds for the current map." };

                targetLabel = $"cell {x},{z}";
            }

            if (Find.WindowStack.FloatMenu != null)
                Find.WindowStack.TryRemove(Find.WindowStack.FloatMenu, doCloseSound: false);

            var clickPos = RimWorldState.CellCenter(clickCell);
            var normalizedMode = (mode ?? "auto").Trim().ToLowerInvariant();
            if (normalizedMode != "auto" && normalizedMode != "achtung" && normalizedMode != "vanilla")
            {
                return new
                {
                    success = false,
                    message = $"Unsupported context menu mode '{mode}'. Use auto, achtung, or vanilla."
                };
            }

            FloatMenu menu;
            List<FloatMenuOption> options;
            string provider;

            if (normalizedMode == "achtung" || (normalizedMode == "auto" && AchtungIntegration.IsLoaded()))
            {
                if (!AchtungIntegration.IsLoaded())
                    return new { success = false, message = "Achtung is not loaded, so an Achtung context menu is unavailable." };

                (menu, options) = AchtungIntegration.BuildMenu(clickPos);
                provider = "achtung";
            }
            else
            {
                var vanillaOptions = FloatMenuMakerMap.GetOptions(selectedPawns, clickPos, out _);
                menu = new FloatMenu(vanillaOptions) { givesColonistOrders = true };
                options = vanillaOptions.ToList();
                provider = "vanilla";
            }

            if (options.Count == 0)
            {
                RimBridgeContextMenus.Clear();
                return new
                {
                    success = true,
                    menuId = 0,
                    provider,
                    clickCell = new { x = clickCell.x, z = clickCell.z },
                    target = targetLabel,
                    selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
                    optionCount = 0,
                    options = new List<object>(),
                    message = "No context menu options were generated for the current selection and target."
                };
            }

            Find.WindowStack.Add(menu);
            PositionDebugMenu(menu);
            var snapshot = RimBridgeContextMenus.Store(provider, menu, options, clickCell, targetLabel);

            return new
            {
                success = true,
                menuId = snapshot.Id,
                provider,
                clickCell = new { x = clickCell.x, z = clickCell.z },
                target = targetLabel,
                selectedPawns = selectedPawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
                optionCount = options.Count,
                options = DescribeOptions(snapshot.Options)
            };
        });
    }

    [Tool("rimworld/get_context_menu_options", Description = "Get the currently opened debug context menu options")]
    public object GetContextMenuOptions()
    {
        return RunTool(() =>
        {
            var snapshot = RimBridgeContextMenus.Current;
            if (snapshot == null || snapshot.Menu == null)
                return new { success = false, message = "No debug context menu has been opened yet." };
            if (Find.WindowStack.FloatMenu != snapshot.Menu)
            {
                RimBridgeContextMenus.Clear();
                return new { success = false, message = "No debug context menu has been opened yet." };
            }

            return new
            {
                success = true,
                menuId = snapshot.Id,
                provider = snapshot.Provider,
                target = snapshot.TargetLabel,
                clickCell = new { x = snapshot.ClickCell.x, z = snapshot.ClickCell.z },
                optionCount = snapshot.Options.Count,
                options = DescribeOptions(snapshot.Options)
            };
        });
    }

    [Tool("rimworld/execute_context_menu_option", Description = "Execute a context menu option by index or label")]
    public object ExecuteContextMenuOption(
        [ToolParameter(Description = "1-based option index. Use -1 to resolve by label instead.")] int optionIndex = -1,
        [ToolParameter(Description = "Exact or partial menu label to execute when optionIndex is -1")] string label = null)
    {
        return RunTool(() =>
        {
            var snapshot = RimBridgeContextMenus.Current;
            if (snapshot == null || snapshot.Menu == null)
                return new { success = false, message = "No debug context menu is available." };
            if (Find.WindowStack.FloatMenu != snapshot.Menu)
            {
                RimBridgeContextMenus.Clear();
                return new { success = false, message = "No debug context menu is available." };
            }

            FloatMenuOption option = null;
            int resolvedIndex = -1;

            if (optionIndex > 0)
            {
                if (optionIndex > snapshot.Options.Count)
                    return new { success = false, message = $"Option index {optionIndex} is out of range for a menu with {snapshot.Options.Count} options." };

                resolvedIndex = optionIndex;
                option = snapshot.Options[optionIndex - 1];
            }
            else
            {
                if (string.IsNullOrWhiteSpace(label))
                    return new { success = false, message = "Either optionIndex or label must be provided." };

                var exactMatches = snapshot.Options
                    .Select((candidate, index) => new { candidate, index })
                    .Where(item => string.Equals(item.candidate.Label, label, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (exactMatches.Count == 1)
                {
                    option = exactMatches[0].candidate;
                    resolvedIndex = exactMatches[0].index + 1;
                }
                else if (exactMatches.Count > 1)
                {
                    return new { success = false, message = $"Label '{label}' is ambiguous within the current menu." };
                }
                else
                {
                    var partialMatches = snapshot.Options
                        .Select((candidate, index) => new { candidate, index })
                        .Where(item => item.candidate.Label.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    if (partialMatches.Count != 1)
                    {
                        return new { success = false, message = $"Could not resolve menu label '{label}' to a single option." };
                    }

                    option = partialMatches[0].candidate;
                    resolvedIndex = partialMatches[0].index + 1;
                }
            }

            if (option.Disabled)
                return new { success = false, message = $"Menu option {resolvedIndex} is disabled.", label = option.Label };
            if (option.action == null)
                return new { success = false, message = $"Menu option {resolvedIndex} has no executable action.", label = option.Label };

            option.Chosen(snapshot.Menu.givesColonistOrders, snapshot.Menu);
            RimBridgeContextMenus.Clear();

            return new
            {
                success = true,
                executedIndex = resolvedIndex,
                label = option.Label
            };
        });
    }

    [Tool("rimworld/close_context_menu", Description = "Close the currently opened debug context menu")]
    public object CloseContextMenu()
    {
        return RunTool(() =>
        {
            if (Find.WindowStack.FloatMenu != null)
                Find.WindowStack.TryRemove(Find.WindowStack.FloatMenu, doCloseSound: false);

            RimBridgeContextMenus.Clear();
            return new { success = true };
        });
    }

    private static List<object> DescribeOptions(IEnumerable<FloatMenuOption> options)
    {
        return options.Select((option, index) => (object)new
        {
            index = index + 1,
            label = option.Label,
            disabled = option.Disabled,
            priority = option.Priority.ToString(),
            orderInPriority = option.orderInPriority,
            autoTakeable = option.autoTakeable,
            hasAction = option.action != null
        }).ToList();
    }

    private static void PositionDebugMenu(FloatMenu menu)
    {
        menu.vanishIfMouseDistant = false;

        var size = menu.InitialSize;
        const float margin = 24f;
        var desiredX = (float)UI.screenWidth * 0.22f;
        var desiredY = 48f;
        var x = Mathf.Clamp(desiredX, margin, Mathf.Max(margin, (float)UI.screenWidth - size.x - margin));
        var y = Mathf.Clamp(desiredY, margin, Mathf.Max(margin, (float)UI.screenHeight - size.y - margin));
        menu.windowRect = new Rect(x, y, size.x, size.y);
    }

    private static object RunTool(Func<object> func, [CallerMemberName] string memberName = null)
    {
        return LegacyToolExecution.Run(func, marshalToMainThread: true, memberName);
    }

    private static object RunBackgroundTool(Func<object> func, [CallerMemberName] string memberName = null)
    {
        return LegacyToolExecution.Run(func, marshalToMainThread: false, memberName);
    }
}
