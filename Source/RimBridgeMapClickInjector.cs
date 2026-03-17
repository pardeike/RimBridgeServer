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
}

internal static class RimBridgeMapClickInjector
{
    private sealed class MapClickRequest
    {
        public IntVec3 ClickCell { get; set; } = IntVec3.Invalid;

        public string TargetLabel { get; set; } = string.Empty;

        public Vector2 ScreenPositionInverted { get; set; }

        public TaskCompletionSource<MapClickDispatchResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class InjectionState
    {
        public bool Active { get; set; }

        public Event PreviousEvent { get; set; }

        public int PointerToken { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly System.Reflection.FieldInfo FloatMenuOptionsField = AccessTools.Field(typeof(FloatMenu), "options");

    private static MapClickRequest _pendingRequest;

    public static MapClickDispatchResult DispatchRightClick(IntVec3 clickCell, string targetLabel, int timeoutMs = 2000)
    {
        timeoutMs = timeoutMs <= 0 ? 2000 : timeoutMs;

        MapClickRequest request;
        try
        {
            request = RimBridgeMainThread.Invoke(() => QueueRequest(clickCell, targetLabel), timeoutMs: 5000);
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
                Message = $"Timed out waiting {timeoutMs}ms for RimWorld to process the synthetic map right-click."
            };
        }

        return request.Completion.Task.GetAwaiter().GetResult();
    }

    public static void BeginUiRootInjection(ref InjectionState state)
    {
        state = null;

        lock (Sync)
        {
            if (_pendingRequest == null)
                return;

            var request = _pendingRequest;
            var currentEvent = Event.current;
            var injectedEvent = currentEvent == null ? new Event() : new Event(currentEvent)
            {
                type = EventType.MouseDown,
                button = 1,
                clickCount = 1,
                modifiers = EventModifiers.None,
                mousePosition = request.ScreenPositionInverted
            };

            state = new InjectionState
            {
                Active = true,
                PreviousEvent = currentEvent,
                PointerToken = RimBridgeVirtualPointer.PushTransientOverride(request.ScreenPositionInverted)
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

        if (state.PointerToken != 0)
            RimBridgeVirtualPointer.PopTransientOverride(state.PointerToken);
        Event.current = state.PreviousEvent;

        lock (Sync)
        {
            if (!ReferenceEquals(_pendingRequest, pendingRequest) || pendingRequest == null)
                return;

            var floatMenu = Find.WindowStack?.FloatMenu;
            if (floatMenu != null)
            {
                floatMenu.vanishIfMouseDistant = false;
                var options = ExtractOptions(floatMenu);
                var snapshot = RimBridgeContextMenus.Store("ui_event", floatMenu, options, pendingRequest.ClickCell, pendingRequest.TargetLabel);
                completedResult = new MapClickDispatchResult
                {
                    Success = true,
                    Snapshot = snapshot,
                    Message = "Dispatched a live right-click through RimWorld's play UI and a context menu remained open."
                };
            }
            else
            {
                RimBridgeContextMenus.Clear();
                completedResult = new MapClickDispatchResult
                {
                    Success = true,
                    Message = "Dispatched a live right-click through RimWorld's play UI. No context menu remained open after the click."
                };
            }

            _pendingRequest = null;
        }

        pendingRequest.Completion.TrySetResult(completedResult);
    }

    private static MapClickRequest QueueRequest(IntVec3 clickCell, string targetLabel)
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
            TargetLabel = targetLabel ?? string.Empty,
            ScreenPositionInverted = RimWorldState.CellCenter(clickCell).MapToUIPosition()
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
            request.Completion.TrySetCanceled();
        }
    }

    private static List<FloatMenuOption> ExtractOptions(FloatMenu floatMenu)
    {
        return (FloatMenuOptionsField?.GetValue(floatMenu) as IEnumerable<FloatMenuOption>)?.ToList() ?? [];
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
