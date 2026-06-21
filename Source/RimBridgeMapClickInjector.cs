using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class MapClickDispatchResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public ContextMenuSnapshot Snapshot { get; set; }

    public string GestureKind { get; set; } = "click";
}

internal sealed class MapClickDispatchOptions
{
    public int Button { get; set; } = 1;

    public string ButtonName { get; set; } = "right";

    public EventModifiers Modifiers { get; set; } = EventModifiers.None;

    public string ModifiersText { get; set; } = "none";

    public int HoldDurationMs { get; set; }

    public static MapClickDispatchOptions DefaultRightClick { get; } = new();
}

internal static class RimBridgeMapClickInjector
{
    private enum MapClickPhase
    {
        MouseDown = 0,
        Hold = 1,
        MouseDrag = 2,
        MouseUp = 3
    }

    private sealed class MapClickRequest
    {
        public IntVec3 ClickCell { get; set; } = IntVec3.Invalid;

        public IntVec3 EndCell { get; set; } = IntVec3.Invalid;

        public string TargetLabel { get; set; } = string.Empty;

        public Vector2 ScreenPositionInverted { get; set; }

        public Vector2 EndScreenPositionInverted { get; set; }

        public bool IsDrag { get; set; }

        public MapClickDispatchOptions Options { get; set; } = MapClickDispatchOptions.DefaultRightClick;

        public MapClickPhase Phase { get; set; } = MapClickPhase.MouseDown;

        public int PhaseInjectedFrame { get; set; } = -1;

        public int HoldUntilTicks { get; set; } = -1;

        public int PointerToken { get; set; }

        public TaskCompletionSource<MapClickDispatchResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class InjectionState
    {
        public bool Active { get; set; }

        public Event PreviousEvent { get; set; }

        public EventType ObservedRawType { get; set; }
    }

    internal sealed class RootInjectionState
    {
        public bool Active { get; set; }

        public Event PreviousEvent { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly System.Reflection.FieldInfo FloatMenuOptionsField = AccessTools.Field(typeof(FloatMenu), "options");

    private static MapClickRequest _pendingRequest;

    public static MapClickDispatchResult DispatchClick(IntVec3 clickCell, string targetLabel, MapClickDispatchOptions options = null, int timeoutMs = 2000)
    {
        return DispatchGesture(clickCell, IntVec3.Invalid, targetLabel, options, timeoutMs);
    }

    public static MapClickDispatchResult DispatchDrag(IntVec3 startCell, IntVec3 endCell, string targetLabel, MapClickDispatchOptions options = null, int timeoutMs = 2000)
    {
        return DispatchGesture(startCell, endCell, targetLabel, options, timeoutMs);
    }

    private static MapClickDispatchResult DispatchGesture(IntVec3 clickCell, IntVec3 endCell, string targetLabel, MapClickDispatchOptions options = null, int timeoutMs = 2000)
    {
        options ??= MapClickDispatchOptions.DefaultRightClick;
        timeoutMs = NormalizeTimeout(timeoutMs, options.HoldDurationMs);

        MapClickRequest request;
        try
        {
            request = RimBridgeMainThread.Invoke(() => QueueRequest(clickCell, endCell, targetLabel, options), timeoutMs: 5000);
        }
        catch (Exception ex)
        {
            return new MapClickDispatchResult
            {
                Success = false,
                Message = ex.Message
            };
        }

        if (!request.Completion.Task.Wait(timeoutMs))
        {
            RimBridgeMainThread.Invoke(CancelPendingRequest, timeoutMs: 5000);
            return new MapClickDispatchResult
            {
                Success = false,
                Message = $"Timed out waiting {timeoutMs}ms for RimWorld to process the synthetic {options.ButtonName}-{(endCell.IsValid ? "drag" : "click")}."
            };
        }

        return request.Completion.Task.GetAwaiter().GetResult();
    }

    public static void BeginRootOnGuiInjection(ref RootInjectionState state)
    {
        state = null;

        lock (Sync)
        {
            if (_pendingRequest == null)
                return;

            var request = _pendingRequest;
            EnsurePointerState(request);
            var currentEvent = Event.current;
            Event.current = CreateInjectedEvent(request, currentEvent);
            state = new RootInjectionState
            {
                Active = true,
                PreviousEvent = currentEvent
            };
        }
    }

    public static void EndRootOnGuiInjection(RootInjectionState state)
    {
        if (state == null || !state.Active)
            return;

        Event.current = state.PreviousEvent;
        RimBridgeVirtualPointer.ScheduleSyntheticMouseStateClear();
    }

    public static void BeginUiRootInjection(ref InjectionState state)
    {
        state = null;
        RimWorldHover.ClearHoverTargetForRealInput(Event.current);

        lock (Sync)
        {
            if (_pendingRequest == null)
                return;

            var request = _pendingRequest;
            EnsurePointerState(request);

            var currentEvent = Event.current;
            var injectedEvent = CreateInjectedEvent(request, currentEvent);

            if (request.Phase == MapClickPhase.MouseDown)
            {
                request.PhaseInjectedFrame = Time.frameCount;
            }
            else if (request.Phase == MapClickPhase.MouseDrag)
            {
                request.PhaseInjectedFrame = Time.frameCount;
            }
            else if (request.Phase == MapClickPhase.MouseUp)
            {
                request.PhaseInjectedFrame = Time.frameCount;
            }

            state = new InjectionState
            {
                Active = true,
                PreviousEvent = currentEvent,
                ObservedRawType = injectedEvent.rawType
            };

            Event.current = injectedEvent;
        }
    }

    public static void EndUiRootInjection(InjectionState state)
    {
        if (state == null || !state.Active)
            return;

        MapClickDispatchResult completedResult = null;
        MapClickRequest pendingRequest;

        lock (Sync)
        {
            pendingRequest = _pendingRequest;
        }

        Event.current = state.PreviousEvent;

        lock (Sync)
        {
            if (!ReferenceEquals(_pendingRequest, pendingRequest) || pendingRequest == null)
                return;

            if (pendingRequest.Phase == MapClickPhase.Hold && TryBuildCompletionResultFromOpenMenu(pendingRequest, out completedResult))
            {
                ReleasePointerOverride(pendingRequest);
                _pendingRequest = null;
                pendingRequest.Completion.TrySetResult(completedResult);
                return;
            }

            if (AdvancePhase(pendingRequest, state.ObservedRawType))
                return;

            if (TryBuildCompletionResultFromOpenMenu(pendingRequest, out completedResult))
            {
                ReleasePointerOverride(pendingRequest);
                _pendingRequest = null;
                pendingRequest.Completion.TrySetResult(completedResult);
                return;
            }

            RimBridgeContextMenus.Clear();
            completedResult = new MapClickDispatchResult
            {
                Success = true,
                GestureKind = pendingRequest.IsDrag ? "drag" : "click",
                Message = BuildCompletionMessage(pendingRequest.Options, pendingRequest.IsDrag, menuRemainedOpen: false)
            };

            ReleasePointerOverride(pendingRequest);
            _pendingRequest = null;
        }

        pendingRequest.Completion.TrySetResult(completedResult);
    }

    private static MapClickRequest QueueRequest(IntVec3 clickCell, IntVec3 endCell, string targetLabel, MapClickDispatchOptions options)
    {
        if (Current.ProgramState != ProgramState.Playing || Current.Game == null)
            throw new InvalidOperationException("RimWorld is not currently in a playable map state.");
        if (_pendingRequest != null)
            throw new InvalidOperationException("A synthetic map click is already pending.");

        if (Find.WindowStack?.FloatMenu != null)
            Find.WindowStack.TryRemove(Find.WindowStack.FloatMenu, doCloseSound: false);
        RimBridgeContextMenus.Clear();

        var request = new MapClickRequest
        {
            ClickCell = clickCell,
            EndCell = endCell,
            TargetLabel = targetLabel ?? string.Empty,
            ScreenPositionInverted = RimWorldState.CellCenter(clickCell).MapToUIPosition(),
            EndScreenPositionInverted = endCell.IsValid ? RimWorldState.CellCenter(endCell).MapToUIPosition() : RimWorldState.CellCenter(clickCell).MapToUIPosition(),
            IsDrag = endCell.IsValid,
            Options = options
        };
        _pendingRequest = request;
        return request;
    }

    private static void CancelPendingRequest()
    {
        lock (Sync)
        {
            if (_pendingRequest == null)
                return;

            var request = _pendingRequest;
            _pendingRequest = null;
            ReleasePointerOverride(request);
            request.Completion.TrySetCanceled();
        }
    }

    private static Event CreateInjectedEvent(MapClickRequest request, Event currentEvent)
    {
        var injectedEvent = currentEvent == null ? new Event() : new Event(currentEvent);
        injectedEvent.button = request.Options.Button;
        injectedEvent.clickCount = 1;
        injectedEvent.modifiers = request.Options.Modifiers;
        injectedEvent.mousePosition = GetCurrentMousePosition(request);
        injectedEvent.delta = GetCurrentMouseDelta(request);
        injectedEvent.type = request.Phase switch
        {
            MapClickPhase.MouseDown => EventType.MouseDown,
            MapClickPhase.MouseDrag => EventType.MouseDrag,
            MapClickPhase.MouseUp => EventType.MouseUp,
            _ => EventType.Layout
        };
        return injectedEvent;
    }

    private static void EnsurePointerState(MapClickRequest request)
    {
        var mousePosition = GetCurrentMousePosition(request);
        if (request.PointerToken == 0)
            request.PointerToken = RimBridgeVirtualPointer.PushTransientOverride(mousePosition);
        else
            RimBridgeVirtualPointer.UpdateTransientOverride(request.PointerToken, mousePosition);

        var buttonHeld = request.Phase != MapClickPhase.MouseUp;
        var buttonDown = request.Phase == MapClickPhase.MouseDown && request.PhaseInjectedFrame < 0;
        var buttonUp = request.Phase == MapClickPhase.MouseUp && request.PhaseInjectedFrame < 0;
        RimBridgeVirtualPointer.SetSyntheticMouseState(
            request.Options.Button,
            mousePosition,
            buttonHeld,
            buttonDown,
            buttonUp);
    }

    private static List<FloatMenuOption> ExtractOptions(FloatMenu floatMenu)
    {
        return (FloatMenuOptionsField?.GetValue(floatMenu) as IEnumerable<FloatMenuOption>)?.ToList() ?? [];
    }

    private static int NormalizeTimeout(int timeoutMs, int holdDurationMs)
    {
        var baseTimeoutMs = timeoutMs <= 0 ? 2000 : timeoutMs;
        var holdAllowanceMs = holdDurationMs < 0 ? 0 : holdDurationMs;
        return Math.Max(baseTimeoutMs, holdAllowanceMs + 2000);
    }

    private static bool AdvancePhase(MapClickRequest request, EventType observedRawType)
    {
        if (request.Phase == MapClickPhase.MouseDown && request.PhaseInjectedFrame == Time.frameCount)
        {
            if (request.IsDrag)
            {
                request.Phase = MapClickPhase.MouseDrag;
            }
            else if (request.Options.HoldDurationMs > 0)
            {
                request.Phase = MapClickPhase.Hold;
                request.HoldUntilTicks = PositiveEnvironmentTick() + request.Options.HoldDurationMs;
            }
            else
            {
                request.Phase = MapClickPhase.MouseUp;
            }

            request.PhaseInjectedFrame = -1;
            return true;
        }

        if (request.Phase == MapClickPhase.MouseDrag && request.PhaseInjectedFrame == Time.frameCount)
        {
            if (request.Options.HoldDurationMs > 0)
            {
                request.Phase = MapClickPhase.Hold;
                request.HoldUntilTicks = PositiveEnvironmentTick() + request.Options.HoldDurationMs;
            }
            else
            {
                request.Phase = MapClickPhase.MouseUp;
            }

            request.PhaseInjectedFrame = -1;
            return true;
        }

        if (request.Phase == MapClickPhase.Hold)
        {
            if (observedRawType == EventType.Layout && PositiveEnvironmentTick() >= request.HoldUntilTicks)
                request.Phase = MapClickPhase.MouseUp;

            return true;
        }

        return request.Phase != MapClickPhase.MouseUp || request.PhaseInjectedFrame != Time.frameCount;
    }

    private static bool TryBuildCompletionResultFromOpenMenu(MapClickRequest request, out MapClickDispatchResult result)
    {
        result = null;

        var floatMenu = Find.WindowStack?.FloatMenu;
        if (floatMenu == null)
            return false;

        floatMenu.vanishIfMouseDistant = false;
        var options = ExtractOptions(floatMenu);
        var snapshot = RimBridgeContextMenus.Store("ui_event", floatMenu, options, request.ClickCell, request.TargetLabel);
        result = new MapClickDispatchResult
        {
            Success = true,
            Snapshot = snapshot,
            GestureKind = request.IsDrag ? "drag" : "click",
            Message = BuildCompletionMessage(request.Options, request.IsDrag, menuRemainedOpen: true)
        };
        return true;
    }

    private static int PositiveEnvironmentTick()
    {
        var tick = Environment.TickCount;
        return tick >= 0 ? tick : -tick;
    }

    private static void ReleasePointerOverride(MapClickRequest request)
    {
        if (request == null || request.PointerToken == 0)
            return;

        RimBridgeVirtualPointer.PopTransientOverride(request.PointerToken);
        RimBridgeVirtualPointer.ReleaseSyntheticMouseState(request.Options.Button, GetCurrentMousePosition(request));
        request.PointerToken = 0;
    }

    private static Vector2 GetCurrentMousePosition(MapClickRequest request)
    {
        return request.Phase == MapClickPhase.MouseDrag || request.Phase == MapClickPhase.MouseUp || request.Phase == MapClickPhase.Hold && request.IsDrag
            ? request.EndScreenPositionInverted
            : request.ScreenPositionInverted;
    }

    private static Vector2 GetCurrentMouseDelta(MapClickRequest request)
    {
        return request.Phase == MapClickPhase.MouseDrag
            ? request.EndScreenPositionInverted - request.ScreenPositionInverted
            : Vector2.zero;
    }

    private static string BuildCompletionMessage(MapClickDispatchOptions options, bool isDrag, bool menuRemainedOpen)
    {
        var holdText = options.HoldDurationMs > 0 ? $" held for {options.HoldDurationMs}ms" : string.Empty;
        var gesture = isDrag ? "drag" : "click";
        if (menuRemainedOpen)
            return $"Dispatched a live {options.ButtonName}-{gesture}{holdText} through RimWorld's play UI and a context menu remained open.";

        return $"Dispatched a live {options.ButtonName}-{gesture}{holdText} through RimWorld's play UI. No context menu remained open after the {gesture}.";
    }
}

[HarmonyPatch(typeof(Root), nameof(Root.OnGUI))]
internal static class Root_OnGUI_MapClickInjection_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(ref RimBridgeMapClickInjector.RootInjectionState __state)
    {
        RimBridgeMapClickInjector.BeginRootOnGuiInjection(ref __state);
    }

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(RimBridgeMapClickInjector.RootInjectionState __state)
    {
        RimBridgeMapClickInjector.EndRootOnGuiInjection(__state);
    }
}

[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
internal static class UIRoot_Play_UIRootOnGUI_MapClickInjection_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(ref RimBridgeMapClickInjector.InjectionState __state)
    {
        RimBridgeMapClickInjector.BeginUiRootInjection(ref __state);
    }

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(RimBridgeMapClickInjector.InjectionState __state)
    {
        RimBridgeMapClickInjector.EndUiRootInjection(__state);
    }
}
