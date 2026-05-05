using System;
using Lib.GAB.Server;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgeStartup
{
    private static readonly object Sync = new();
    private static bool _modConstructed;
    private static bool _runtimeReady;
    private static bool _started;
    private static GabpServer _server;
    private static RimBridgeAttentionPublisher _attentionPublisher;

    public static void OnModConstructed()
    {
        lock (Sync)
        {
            _modConstructed = true;
        }

        TryStart();
    }

    public static void OnRuntimeReady()
    {
        var shouldLog = false;
        var alreadyStarted = false;

        lock (Sync)
        {
            if (!_runtimeReady)
            {
                _runtimeReady = true;
                shouldLog = true;
            }

            alreadyStarted = _started;
        }

        if (shouldLog)
        {
            Log.Message(alreadyStarted
                ? "[RimBridge] Play-data load complete; bridge services are already available."
                : "[RimBridge] Startup conditions satisfied after play-data load; initializing bridge services.");
        }

        TryStart();
    }

    public static void RegisterExtensionTools(GabpServer server)
    {
        if (server == null)
            throw new ArgumentNullException(nameof(server));

        foreach (var tool in RimBridgeCapabilities.ExtensionTools)
        {
            if (server.Tools.HasTool(tool.Alias))
            {
                Log.Warning($"[RimBridge] Skipping annotated extension tool '{tool.Alias}' because a tool with that name is already registered.");
                continue;
            }

            server.Tools.RegisterTool(
                tool.Alias,
                parameters => TaskShim.FromResult<object>(LegacyToolExecution.InvokeAlias(tool.Alias, ReflectedCapabilityBinding.NormalizeInvocationArguments(parameters))),
                tool.ToolInfo);
        }
    }

    private static void TryStart()
    {
        var startingBeforeRuntimeReady = false;

        lock (Sync)
        {
            if (!_modConstructed || _started)
                return;

            _started = true;
            startingBeforeRuntimeReady = !_runtimeReady;
        }

        try
        {
            if (startingBeforeRuntimeReady)
                Log.Message("[RimBridge] Initializing bridge services before play-data load completes.");

            RimBridgeCapabilities.Initialize();
            RimBridgeLogs.Initialize(RimBridgeCapabilities.LogJournal);
            Application.runInBackground = true;

            var tools = new RimBridgeTools();
            var version = typeof(RimBridgeServerMod).Assembly.GetName().Version?.ToString() ?? "0.1.0.0";
            var builder = Lib.GAB.Gabp.CreateServer()
                .UseAppInfo("RimBridgeServer", version)
                .UseGabsEnvironmentIfAvailable();
            var usingGabsConfig = Lib.GAB.Gabp.IsRunningUnderGabs();
            string bridgeConfigError = null;
            if (!usingGabsConfig && RimBridgeGabsBridgeConfig.TryRead("rimworld", out var bridgeConfig, out bridgeConfigError))
            {
                builder.UseExternalConfig(bridgeConfig.Port, bridgeConfig.Token, bridgeConfig.GameId);
                usingGabsConfig = true;
            }
            else if (!string.IsNullOrEmpty(bridgeConfigError))
            {
                Log.Warning($"[RimBridge] Could not read GABS bridge config: {bridgeConfigError}");
            }

            _server = builder
                .UsePortIfNotSet(5174)
                .EnableAttentionSupport()
                .Build();
            _server.Tools.RegisterToolsFromInstance(tools);
            RegisterExtensionTools(_server);
            RimBridgeEventRelay.Initialize(_server.Events, RimBridgeCapabilities.Journal, RimBridgeCapabilities.LogJournal);
            _attentionPublisher = new RimBridgeAttentionPublisher(_server.Attention, RimBridgeCapabilities.Journal, RimBridgeCapabilities.LogJournal);

            _server.StartAsync().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Log.Error($"[RimBridge] Failed to start server: {task.Exception}");
                    return;
                }

                if (usingGabsConfig)
                {
                    Log.Message($"[RimBridge] GABP server connected to GABS on port {_server.Port}");
                }
                else
                {
                    Log.Message($"[RimBridge] GABP server running standalone on port {_server.Port}");
                    Log.Message($"[RimBridge] Bridge token: {_server.Token}");
                }
            });
        }
        catch (Exception ex)
        {
            lock (Sync)
            {
                _started = false;
            }

            Log.Error($"[RimBridge] STARTUP_INIT_FAILURE: {ex}");
            Log.Error($"[RimBridge] Failed to initialize server: {ex}");
        }
    }

    private static class TaskShim
    {
        public static System.Threading.Tasks.Task<T> FromResult<T>(T value)
        {
            return System.Threading.Tasks.Task.FromResult(value);
        }
    }
}
