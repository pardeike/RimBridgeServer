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
                optionalPatchFailures = OptionalPatchFailures.ToArray(),
                lateInputPatches = RimBridgeVirtualPointer.DescribeLateInputPatchStatus()
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

[HarmonyPatch(typeof(PrefsData), nameof(PrefsData.Apply))]
internal static class PrefsData_Apply_BackgroundBridge_Patch
{
    [HarmonyPriority(Priority.Last)]
    public static void Postfix()
    {
        // Native session cleanup reapplies preferences. Keep the bridge's existing
        // background execution contract without changing the saved preference.
        Application.runInBackground = true;
    }
}

[HarmonyPatch(typeof(Root), nameof(Root.Update))]
internal static class Root_Update_Patch
{
    public static void Postfix()
    {
        if (PlayDataLoader.Loaded && !LongEventHandler.AnyEventNowOrWaiting && Find.UIRoot != null)
            RimBridgeCameraConfig.MaintainZoomExtension();

        RimBridgeMainThread.Pump();
        RimBridgeAsyncScheduler.Pump();
        RimWorldTickStepper.AdvanceFromRootUpdate(Time.frameCount);
        RimBridgeFrameClock.AdvanceFromRootUpdate(Time.frameCount);
        RimBridgeAsyncScheduler.Pump();
        RimBridgeUiWorkbench.AdvanceFrame(Time.frameCount);
        RimBridgeVirtualPointer.ClearExpiredSyntheticMouseState();
        RimWorldHover.ClearHoverTargetForRealInputState();
        RimWorldHover.ClearExpiredHoverTarget();
        if (PlayDataLoader.Loaded && !LongEventHandler.AnyEventNowOrWaiting && Find.UIRoot != null)
        {
            RimBridgeStartup.OnRuntimeReady();
        }
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

    public static async Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        if (IsMainThread)
            return func();

        var workItem = new MainThreadWorkItem<T>(func);

        lock (Sync)
        {
            Pending.Enqueue(workItem);
        }

        CancellationTokenRegistration registration = default;
        if (cancellationToken.CanBeCanceled)
            registration = cancellationToken.Register(() => workItem.CancelIfPending());

        try
        {
            return await workItem.Completion.ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
        }
    }

    public static Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(() =>
        {
            action();
            return true;
        }, cancellationToken);
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

    // Pop-up menus opened by bridge actions (gizmos, UI clicks) vanish as soon as the real mouse is far away, which
    // it always is for background automation, and can open off-screen. Keep such a menu open and on-screen, and
    // track it so the context-menu tools can read and execute it. Call on the main thread.
    public static ContextMenuSnapshot AdoptOpenMenu(string provider)
    {
        var open = Find.WindowStack?.FloatMenu;
        if (open == null)
            return null;

        open.vanishIfMouseDistant = false;
        var rect = open.windowRect;
        rect.x = UnityEngine.Mathf.Clamp(rect.x, 0f, UnityEngine.Mathf.Max(0f, UI.screenWidth - rect.width));
        rect.y = UnityEngine.Mathf.Clamp(rect.y, 0f, UnityEngine.Mathf.Max(0f, UI.screenHeight - rect.height));
        open.windowRect = rect;
        return Current?.Menu == open ? Current : Store(provider, open, open.options ?? [], IntVec3.Invalid, null);
    }

    public static object DescribeMenu(ContextMenuSnapshot snapshot)
    {
        if (snapshot == null)
            return null;

        return new
        {
            menuId = snapshot.Id,
            optionCount = snapshot.Options.Count,
            options = snapshot.Options.Select((option, index) => (object)new
            {
                index = index + 1,
                label = option.Label,
                disabled = option.Disabled ? true : (bool?)null
            }).ToList(),
            hint = "Choose with rimworld/execute_context_menu_option (index or label)."
        };
    }

    // The bridge-opened menu if it is still showing; otherwise adopt any FloatMenu RimWorld itself opened
    // (gizmo option menus, crop pickers, material pickers) so the context-menu tools can read and execute it.
    public static ContextMenuSnapshot ResolveOpenMenu()
    {
        var open = Find.WindowStack?.FloatMenu;
        if (open == null)
        {
            Current = null;
            return null;
        }

        if (Current?.Menu == open)
            return Current;

        return Store("window", open, open.options ?? [], IntVec3.Invalid, null);
    }

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
