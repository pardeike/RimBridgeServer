using System;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class LifecycleCapabilityModule
{
    public object PauseGame(bool pause = true)
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
    }

    public object StartDebugGame()
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
    }

    public object GoToMainMenu()
    {
        var state = RimWorldState.ToolStateSnapshot();
        if (GenScene.InEntryScene && Current.ProgramState == ProgramState.Entry && Current.Game == null)
        {
            return new
            {
                success = true,
                status = "noop",
                message = "RimWorld is already at the main menu entry scene.",
                state
            };
        }

        if (LongEventHandler.AnyEventNowOrWaiting)
        {
            return new
            {
                success = false,
                message = "RimWorld is busy with another long event. Wait for it to finish before returning to the main menu.",
                state
            };
        }

        GenScene.GoToMainMenu();
        return new
        {
            success = true,
            status = "queued",
            message = "Queued return to the RimWorld main menu.",
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public object ListSaves()
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
    }

    public object SpawnThing(string defName, int x, int z, int stackCount = 1)
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
    }

    public object SaveGame(string saveName)
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
    }

    public object LoadGame(string saveName)
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
    }
}
