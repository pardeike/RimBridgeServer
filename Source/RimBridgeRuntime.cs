using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgePatches
{
    private static bool _applied;

    public static void Apply()
    {
        if (_applied)
            return;

        new Harmony("pardeike.rimbridgeserver.runtime").PatchAll();
        _applied = true;
    }
}

[HarmonyPatch(typeof(Root), nameof(Root.Update))]
internal static class Root_Update_Patch
{
    public static void Postfix()
    {
        RimBridgeMainThread.Pump();
    }
}

internal static class RimBridgeMainThread
{
    private static readonly Queue<Action> Pending = [];
    private static readonly object Sync = new();
    private static int _mainThreadId;

    public static void Initialize()
    {
        if (_mainThreadId == 0)
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

    public static void Pump()
    {
        while (true)
        {
            Action action;
            lock (Sync)
            {
                if (Pending.Count == 0)
                    break;

                action = Pending.Dequeue();
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimBridge] Main-thread work item failed: {ex}");
            }
        }
    }

    public static T Invoke<T>(Func<T> func, int timeoutMs = 5000)
    {
        if (IsMainThread)
            return func();

        using var waitHandle = new ManualResetEventSlim(false);
        T result = default;
        Exception error = null;

        lock (Sync)
        {
            Pending.Enqueue(() =>
            {
                try
                {
                    result = func();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    waitHandle.Set();
                }
            });
        }

        if (!waitHandle.Wait(timeoutMs))
            throw new TimeoutException($"Timed out waiting for main-thread work after {timeoutMs}ms.");

        if (error != null)
            throw new InvalidOperationException("Main-thread work item failed.", error);

        return result;
    }

    public static void Invoke(Action action, int timeoutMs = 5000)
    {
        Invoke(() =>
        {
            action();
            return true;
        }, timeoutMs);
    }
}

internal sealed class ContextMenuSnapshot
{
    public int Id;
    public string Provider;
    public FloatMenu Menu;
    public List<FloatMenuOption> Options = [];
    public IntVec3 ClickCell = IntVec3.Invalid;
    public string TargetLabel;
}

internal static class RimBridgeContextMenus
{
    private static int _nextId = 1;

    public static ContextMenuSnapshot Current { get; private set; }

    public static ContextMenuSnapshot Store(string provider, FloatMenu menu, IEnumerable<FloatMenuOption> options, IntVec3 clickCell, string targetLabel)
    {
        Current = new ContextMenuSnapshot
        {
            Id = _nextId++,
            Provider = provider,
            Menu = menu,
            Options = [.. options],
            ClickCell = clickCell,
            TargetLabel = targetLabel
        };

        return Current;
    }

    public static void Clear()
    {
        Current = null;
    }
}
