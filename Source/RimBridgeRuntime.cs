using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using RimBridgeServer.Core;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgePatches
{
    private const string HarmonyId = "pardeike.rimbridgeserver.runtime";
    private static readonly object Sync = new();
    private static bool _applied;
    private static bool _essentialApplied;
    private static int _optionalPatchAttemptCount;
    private static int _optionalPatchSuccessCount;
    private static readonly List<string> OptionalPatchFailures = [];

    public static void Apply()
    {
        lock (Sync)
        {
            if (_applied)
                return;

            _essentialApplied = false;
            _optionalPatchAttemptCount = 0;
            _optionalPatchSuccessCount = 0;
            OptionalPatchFailures.Clear();
            var harmony = new Harmony(HarmonyId);
            ApplyEssentialPatches(harmony);
            ApplyOptionalPatches(harmony);
            _applied = true;
        }
    }

    public static object DescribeStatus()
    {
        lock (Sync)
        {
            return new
            {
                applied = _applied,
                essentialApplied = _essentialApplied,
                optionalPatchAttemptCount = _optionalPatchAttemptCount,
                optionalPatchSuccessCount = _optionalPatchSuccessCount,
                optionalPatchFailureCount = OptionalPatchFailures.Count,
                optionalPatchFailures = OptionalPatchFailures.ToArray()
            };
        }
    }

    private static void ApplyEssentialPatches(Harmony harmony)
    {
        try
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(Root), nameof(Root.Update)),
                postfix: new HarmonyMethod(typeof(Root_Update_Patch), nameof(Root_Update_Patch.Postfix)));
            _essentialApplied = true;
            Log.Message("[RimBridge] Applied essential Harmony patches.");
        }
        catch (Exception ex)
        {
            Log.Error($"[RimBridge] STARTUP_ESSENTIAL_PATCH_FAILURE: {ex}");
            throw;
        }
    }

    private static void ApplyOptionalPatches(Harmony harmony)
    {
        var optionalPatchTypes = typeof(RimBridgePatches).Assembly
            .GetTypes()
            .Where(type => type != typeof(Root_Update_Patch))
            .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        _optionalPatchAttemptCount = optionalPatchTypes.Count;

        foreach (var patchType in optionalPatchTypes)
        {
            try
            {
                harmony.CreateClassProcessor(patchType).Patch();
                _optionalPatchSuccessCount++;
            }
            catch (Exception ex)
            {
                var failure = $"{patchType.FullName}: {ex.GetType().Name}: {ex.Message}";
                OptionalPatchFailures.Add(failure);
                Log.Error($"[RimBridge] STARTUP_OPTIONAL_PATCH_FAILURE: {patchType.FullName}: {ex}");
            }
        }

        if (OptionalPatchFailures.Count == 0)
        {
            Log.Message($"[RimBridge] Applied {_optionalPatchSuccessCount} optional Harmony patch classes.");
            return;
        }

        Log.Warning($"[RimBridge] Applied {_optionalPatchSuccessCount} of {_optionalPatchAttemptCount} optional Harmony patch classes. Failed: {OptionalPatchFailures.Count}.");
    }
}

[HarmonyPatch(typeof(Root), nameof(Root.Update))]
internal static class Root_Update_Patch
{
    public static void Postfix()
    {
        RimBridgeMainThread.Pump();
        RimBridgeUiWorkbench.AdvanceFrame(Time.frameCount);
        if (PlayDataLoader.Loaded && !LongEventHandler.AnyEventNowOrWaiting && Find.UIRoot != null)
            RimBridgeStartup.OnRuntimeReady();
    }
}

internal static class RimBridgeMainThread
{
    private interface IMainThreadWorkItem
    {
        void ExecuteIfPending();

        bool CancelIfPending();
    }

    private sealed class MainThreadWorkItem<T> : IMainThreadWorkItem
    {
        private readonly Func<T> _func;
        private readonly TaskCompletionSource<T> _completion = new();
        private readonly OperationContextSnapshot _context = OperationContext.Capture();
        private int _state;

        public MainThreadWorkItem(Func<T> func)
        {
            _func = func ?? throw new ArgumentNullException(nameof(func));
        }

        public Task<T> Completion => _completion.Task;

        public void ExecuteIfPending()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
                return;

            try
            {
                using var scope = OperationContext.Restore(_context);
                var result = _func();
                _completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _state, 2);
            }
        }

        public bool CancelIfPending()
        {
            if (Interlocked.CompareExchange(ref _state, 3, 0) != 0)
                return false;

            _completion.TrySetCanceled();
            return true;
        }
    }

    private static readonly Queue<IMainThreadWorkItem> Pending = [];
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
            IMainThreadWorkItem workItem;
            lock (Sync)
            {
                if (Pending.Count == 0)
                    break;

                workItem = Pending.Dequeue();
            }

            try
            {
                workItem.ExecuteIfPending();
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

        var workItem = new MainThreadWorkItem<T>(func);

        lock (Sync)
        {
            Pending.Enqueue(workItem);
        }

        if (timeoutMs <= 0)
            return workItem.Completion.GetAwaiter().GetResult();

        if (workItem.Completion.Wait(timeoutMs))
            return workItem.Completion.GetAwaiter().GetResult();

        if (workItem.Completion.IsCompleted)
            return workItem.Completion.GetAwaiter().GetResult();

        if (workItem.CancelIfPending())
            throw new TimeoutException($"Timed out waiting for main-thread work after {timeoutMs}ms.");

        if (workItem.Completion.IsCompleted)
            return workItem.Completion.GetAwaiter().GetResult();

        throw new TimeoutException($"Timed out waiting for main-thread work after {timeoutMs}ms.");
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
