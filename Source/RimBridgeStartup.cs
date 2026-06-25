using System;
using System.Diagnostics;
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
    }

    public static void OnRuntimeReady()
    {
        var shouldLog = false;
        var shouldApplyRuntimeReadyPatches = false;
        var alreadyStarted = false;

        lock (Sync)
        {
            if (!_runtimeReady)
            {
                _runtimeReady = true;
                shouldLog = true;
                shouldApplyRuntimeReadyPatches = true;
            }

            alreadyStarted = _started;
        }

        if (shouldLog)
        {
            Log.Message(alreadyStarted
                ? "[RimBridge] Play-data load complete; bridge services are already available."
                : "[RimBridge] Startup conditions satisfied after play-data load; initializing bridge services.");
        }

        if (shouldApplyRuntimeReadyPatches)
        {
            using (RimBridgeStartupTiming.Phase("late-input-patches"))
                RimBridgeVirtualPointer.ApplyLateInputPatches();
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
                continue;

            server.Tools.RegisterTool(
                tool.Alias,
                parameters => TaskShim.FromResult<object>(LegacyToolExecution.InvokeAlias(tool.Alias, ReflectedCapabilityBinding.NormalizeInvocationArguments(parameters))),
                tool.ToolInfo);
        }
    }

    private static void TryStart()
    {
        lock (Sync)
        {
            if (!_modConstructed || !_runtimeReady || _started)
                return;

            _started = true;
        }

        try
        {
            using (RimBridgeStartupTiming.Phase("bridge-start.total"))
            {
                using (RimBridgeStartupTiming.Phase("capabilities.initialize"))
                    RimBridgeCapabilities.Initialize();
                using (RimBridgeStartupTiming.Phase("logs.initialize"))
                    RimBridgeLogs.Initialize(RimBridgeCapabilities.LogJournal);
                using (RimBridgeStartupTiming.Phase("application.run-in-background"))
                    Application.runInBackground = true;

                RimBridgeTools tools;
                using (RimBridgeStartupTiming.Phase("tools.construct"))
                    tools = new RimBridgeTools();

                var version = typeof(RimBridgeServerMod).Assembly.GetName().Version?.ToString() ?? "0.1.0.0";
                var builder = Lib.GAB.Gabp.CreateServer();
                using (RimBridgeStartupTiming.Phase("gabp.builder.configure"))
                {
                    builder = builder
                        .UseAppInfo("RimBridgeServer", version)
                        .UseGabsEnvironmentIfAvailable();
                }

                var usingGabsConfig = Lib.GAB.Gabp.IsRunningUnderGabs();

                using (RimBridgeStartupTiming.Phase("gabp.server.build"))
                {
                    _server = builder
                        .UsePortIfNotSet(5174)
                        .EnableAttentionSupport()
                        .Build();
                }

                using (RimBridgeStartupTiming.Phase("tools.register-core"))
                    _server.Tools.RegisterToolsFromInstance(tools);
                using (RimBridgeStartupTiming.Phase("tools.register-extensions"))
                    RegisterExtensionTools(_server);
                using (RimBridgeStartupTiming.Phase("event-relay.initialize"))
                    RimBridgeEventRelay.Initialize(_server.Events, RimBridgeCapabilities.Journal, RimBridgeCapabilities.LogJournal);
                using (RimBridgeStartupTiming.Phase("attention.initialize"))
                    _attentionPublisher = new RimBridgeAttentionPublisher(_server.Attention, RimBridgeCapabilities.Journal, RimBridgeCapabilities.LogJournal);

                var serverStartElapsedMs = RimBridgeStartupTiming.ElapsedMilliseconds;
                using (RimBridgeStartupTiming.Phase("gabp.server.start-submit"))
                {
                    _server.StartAsync().ContinueWith(task =>
                    {
                        RimBridgeStartupTiming.LogCompletedPhase("gabp.server.start-async", serverStartElapsedMs);
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
            }
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

internal static class RimBridgeStartupTiming
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static long ElapsedMilliseconds => Clock.ElapsedMilliseconds;

    public static IDisposable Phase(string name)
    {
        return new PhaseScope(name, Clock.ElapsedMilliseconds);
    }

    public static void LogCompletedPhase(string name, long startedElapsedMilliseconds)
    {
        if (!IsDebugTimingEnabled())
            return;

        var ended = Clock.ElapsedMilliseconds;
        Log.Warning($"[RimBridge] STARTUP_TIMING phase={name} durationMs={ended - startedElapsedMilliseconds} elapsedMs={ended}");
    }

    private static bool IsDebugTimingEnabled()
    {
        try
        {
            return Prefs.DevMode;
        }
        catch
        {
            return false;
        }
    }

    private sealed class PhaseScope : IDisposable
    {
        private readonly string _name;
        private readonly long _startedElapsedMilliseconds;
        private bool _disposed;

        public PhaseScope(string name, long startedElapsedMilliseconds)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "unknown" : name;
            _startedElapsedMilliseconds = startedElapsedMilliseconds;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            LogCompletedPhase(_name, _startedElapsedMilliseconds);
        }
    }
}
