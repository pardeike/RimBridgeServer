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
    private static GabpServer _server;

    public RimBridgeServerMod(ModContentPack content) : base(content)
    {
        if (_server == null)
        {
            try
            {
                // Create GABS-aware server that automatically adapts to environment
                var tools = new RimBridgeTools();
                _server = Gabp.CreateGabsAwareServerWithInstance("RimBridgeServer", "0.1.0", tools, fallbackPort: 5174);
                
                // Start the server
                _server.StartAsync().ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        Log.Error($"[RimBridge] Failed to start server: {task.Exception}");
                    }
                    else
                    {
                        if (Gabp.IsRunningUnderGabs())
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
}

public sealed class RimBridgeGameComponent : GameComponent
{
    public RimBridgeGameComponent(Game game) { }
    
    // Game component no longer needed for thread dispatching with Lib.GAB
    // Lib.GAB handles its own threading
}

// RimWorld-specific tools
public class RimBridgeTools
{
    [Tool("rimbridge.core/ping", Description = "Connectivity test. Returns 'pong'.")]
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
}
