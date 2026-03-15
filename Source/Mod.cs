using System;
using System.Threading.Tasks;
using Lib.GAB;
using Lib.GAB.Tools;
using Lib.GAB.Server;
using RimWorld;
using Verse;

namespace RimBridgeServer;

public class RimBridgeServerMod : Mod
{
    private readonly GabpServer _server;

    public RimBridgeServerMod(ModContentPack content) : base(content)
    {
        try
        {
            // Create GABS-aware server that automatically adapts to environment
            var tools = new RimBridgeTools();
            _server = Lib.GAB.Gabp.CreateGabsAwareServerWithInstance("RimBridgeServer", "0.1.0", tools, fallbackPort: 5174);
            
            // Start the server
            _server.StartAsync().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Log.Error($"[RimBridge] Failed to start server: {task.Exception}");
                }
                else
                {
                    if (Lib.GAB.Gabp.IsRunningUnderGabs())
                    {
                        Log.Message($"[RimBridge] GABP server connected to GABS on port {_server.Port}");
                    }
                    else
                    {
                        Log.Message($"[RimBridge] GABP server running standalone on port {_server.Port}");
                        Log.Message($"[RimBridge] Bridge token: {_server.Token}");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[RimBridge] Failed to initialize server: {ex}");
        }
    }
}

// RimWorld-specific tools
public class RimBridgeTools
{
    private static object ToolStateSnapshot()
    {
        return new
        {
            programState = Current.ProgramState.ToString(),
            inEntryScene = GenScene.InEntryScene,
            hasCurrentGame = Current.Game != null,
            longEventPending = LongEventHandler.AnyEventNowOrWaiting
        };
    }

    [Tool("rimbridge/ping", Description = "Connectivity test. Returns 'pong'.")]
    public object Ping()
    {
        return new { message = "pong", timestamp = DateTime.UtcNow };
    }

    [Tool("rimworld/get_game_info", Description = "Get basic information about the current RimWorld game")]
    public object GetGameInfo()
    {
        var currentGame = Current.Game;
        if (currentGame == null)
        {
            return new { status = "no_game", message = "No game is currently loaded" };
        }

        return new
        {
            status = "game_loaded",
            ticksGame = currentGame.tickManager.TicksGame,
            // playingSince is not available in the public API, removing it
            mapCount = currentGame.Maps?.Count ?? 0
        };
    }

    [Tool("rimworld/pause_game", Description = "Pause or unpause the game")]
    public object PauseGame([ToolParameter(Description = "True to pause, false to unpause")] bool pause = true)
    {
        if (Current.Game == null)
        {
            return new { success = false, message = "No game is currently loaded" };
        }

        var currentlyPaused = Find.TickManager.Paused;
        
        // Only toggle if we need to change the current state
        if (pause && !currentlyPaused)
        {
            Find.TickManager.TogglePaused();
        }
        else if (!pause && currentlyPaused)
        {
            Find.TickManager.TogglePaused();
        }

        return new
        {
            success = true,
            paused = Find.TickManager.Paused,
            message = pause ? "Game paused" : "Game unpaused"
        };
    }

    [Tool("rimworld/start_debug_game", Description = "Start RimWorld's built-in quick test colony from the main menu")]
    public object StartDebugGame()
    {
        if (LongEventHandler.AnyEventNowOrWaiting)
        {
            return new
            {
                success = false,
                message = "RimWorld is busy with another long event. Wait for it to finish before starting a debug game.",
                state = ToolStateSnapshot()
            };
        }

        if (!GenScene.InEntryScene || Current.ProgramState != ProgramState.Entry)
        {
            return new
            {
                success = false,
                message = "Debug game start is only supported from the main menu entry scene.",
                state = ToolStateSnapshot()
            };
        }

        if (Current.Game != null)
        {
            return new
            {
                success = false,
                message = "A game is already loaded. Return to the main menu before starting a new debug game.",
                state = ToolStateSnapshot()
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
            state = ToolStateSnapshot()
        };
    }
}
