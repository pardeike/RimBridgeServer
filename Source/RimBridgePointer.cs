using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using RimBridgeServer.Core;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class PointerTargetSnapshot
{
    public string Kind { get; set; } = string.Empty;

    public string TargetId { get; set; }

    public string Label { get; set; }

    public Vector2 ScreenPosition { get; set; }

    public UiRectSnapshot Rect { get; set; }

    public object Details { get; set; }

    public IntVec3 Cell { get; set; } = IntVec3.Invalid;
}

internal static class RimBridgePointer
{
    private sealed class PointerStep
    {
        public Vector2 Position { get; set; }

        public EventType EventType { get; set; }

        public bool ButtonDown { get; set; }

        public bool ButtonHeld { get; set; }

        public bool ButtonUp { get; set; }
    }

    private sealed class PointerGestureRequest
    {
        public string Command { get; set; } = string.Empty;

        public PointerTargetSnapshot From { get; set; }

        public PointerTargetSnapshot To { get; set; }

        public int Button { get; set; } = -1;

        public string ButtonName { get; set; } = "none";

        public EventModifiers Modifiers { get; set; } = EventModifiers.None;

        public string ModifiersText { get; set; } = "none";

        public int TimeoutMs { get; set; }

        public int QueuedTick { get; set; }

        public int LastAdvancedFrame { get; set; } = -1;

        public int StepIndex { get; set; }

        public int PointerToken { get; set; }

        public bool LeavePointerAtEnd { get; set; }

        public bool PersistOnComplete { get; set; }

        public bool TimedOut { get; set; }

        public ContextMenuSnapshot ContextMenuSnapshot { get; set; }

        public List<PointerStep> Steps { get; set; } = [];

        public TaskCompletionSource<object> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class EventInjectionState
    {
        public bool Active { get; set; }

        public Event PreviousEvent { get; set; }
    }

    private sealed class TooltipObservation
    {
        public Rect Rect { get; set; }

        public TipSignal Signal { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly FieldInfo ActiveTipsField = AccessTools.Field(typeof(TooltipHandler), "activeTips");
    private static readonly FieldInfo ActiveTipSignalField = AccessTools.Field(typeof(ActiveTip), "signal");
    private static readonly FieldInfo ActiveTipFirstTriggerTimeField = AccessTools.Field(typeof(ActiveTip), "firstTriggerTime");
    private static readonly FieldInfo ActiveTipLastTriggerFrameField = AccessTools.Field(typeof(ActiveTip), "lastTriggerFrame");
    private static readonly FieldInfo FloatMenuOptionsField = AccessTools.Field(typeof(FloatMenu), "options");
    private static PointerGestureRequest _activeGesture;
    private static PointerTargetSnapshot _persistentPointer;

    public static bool ShouldTrackUiSurface()
    {
        lock (Sync)
        {
            return _activeGesture != null || _persistentPointer != null;
        }
    }

    public static object PointerMoveResponse(
        Dictionary<string, object> target,
        Dictionary<string, object> offset = null,
        int durationMs = 0,
        int steps = 0,
        bool persist = true,
        bool waitForTooltip = false,
        int timeoutMs = 2000)
    {
        try
        {
            timeoutMs = NormalizeTimeout(timeoutMs);
            var resolvedTarget = RimBridgeMainThread.Invoke(() => ResolveTarget(target, offset), timeoutMs: 5000);
            var normalizedSteps = Math.Max(0, steps);
            if (durationMs <= 0 && normalizedSteps == 0)
            {
                RimBridgeMainThread.Invoke(() =>
                {
                    if (persist)
                        SetPersistentPointer(resolvedTarget);
                    else
                        ClearPersistentPointer();
                }, timeoutMs: 5000);

                if (waitForTooltip && !WaitForActiveTooltip(timeoutMs))
                    return CreateMoveResponse(false, resolvedTarget, persist, "Timed out waiting for a tooltip after moving the pointer.", tooltipTimedOut: true);

                return CreateMoveResponse(true, resolvedTarget, persist, "Pointer moved.", tooltipTimedOut: false);
            }

            var request = RimBridgeMainThread.Invoke(
                () => QueueMove(resolvedTarget, durationMs, normalizedSteps, persist, timeoutMs),
                timeoutMs: 5000);
            if (!request.Completion.Task.Wait(timeoutMs))
            {
                RimBridgeMainThread.Invoke(() => CancelGesture(request, timedOut: true), timeoutMs: 5000);
                return CreateMoveResponse(false, resolvedTarget, persist, $"Timed out waiting {timeoutMs}ms for pointer movement to complete.", tooltipTimedOut: false);
            }

            var response = request.Completion.Task.GetAwaiter().GetResult();
            if (waitForTooltip && !WaitForActiveTooltip(timeoutMs))
                return CreateMoveResponse(false, resolvedTarget, persist, "Timed out waiting for a tooltip after moving the pointer.", tooltipTimedOut: true);

            return response;
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                command = "pointer_move",
                message = ex.Message
            };
        }
    }

    public static object PointerGestureResponse(
        Dictionary<string, object> from,
        Dictionary<string, object> to,
        string button = "left",
        List<object> modifiers = null,
        int durationMs = 250,
        int steps = 8,
        int holdStartMs = 0,
        int holdEndMs = 0,
        int timeoutMs = 3000,
        bool leavePointerAtEnd = false)
    {
        try
        {
            timeoutMs = NormalizeTimeout(timeoutMs);
            var normalizedButton = ParseMouseButton(button);
            var parsedModifiers = ParseModifiers(modifiers);
            if (holdStartMs < 0 || holdEndMs < 0)
                throw new InvalidOperationException("holdStartMs and holdEndMs must be zero or greater.");

            var resolved = RimBridgeMainThread.Invoke(() =>
            {
                var start = ResolveTarget(from, offset: null);
                var end = ResolveTarget(to ?? from, offset: null);
                return (start, end);
            }, timeoutMs: 5000);
            var request = RimBridgeMainThread.Invoke(
                () => QueueGesture(
                    resolved.start,
                    resolved.end,
                    normalizedButton.Button,
                    normalizedButton.Name,
                    parsedModifiers.Modifiers,
                    parsedModifiers.Text,
                    durationMs,
                    steps,
                    holdStartMs,
                    holdEndMs,
                    timeoutMs,
                    leavePointerAtEnd),
                timeoutMs: 5000);

            if (!request.Completion.Task.Wait(timeoutMs))
            {
                RimBridgeMainThread.Invoke(() => CancelGesture(request, timedOut: true), timeoutMs: 5000);
                return new
                {
                    success = false,
                    command = "pointer_gesture",
                    message = $"Timed out waiting {timeoutMs}ms for pointer gesture to complete.",
                    from = DescribeTarget(resolved.start),
                    to = DescribeTarget(resolved.end),
                    button = normalizedButton.Name,
                    modifiers = parsedModifiers.Text
                };
            }

            return request.Completion.Task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                command = "pointer_gesture",
                message = ex.Message
            };
        }
    }

    public static object PointerClearResponse()
    {
        try
        {
            RimBridgeMainThread.Invoke(() =>
            {
                lock (Sync)
                {
                    if (_activeGesture != null)
                        CancelGesture(_activeGesture, timedOut: false);
                    ClearPersistentPointer();
                }
            }, timeoutMs: 5000);

            return new
            {
                success = true,
                command = "pointer_clear",
                message = "Pointer state cleared.",
                pointer = (object)null
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                command = "pointer_clear",
                message = ex.Message
            };
        }
    }

    public static void AdvanceFrame(int frameCount)
    {
        PointerGestureRequest completed = null;

        lock (Sync)
        {
            if (_activeGesture == null)
                return;

            var request = _activeGesture;
            if (request.LastAdvancedFrame == frameCount)
                return;

            request.LastAdvancedFrame = frameCount;
            if (request.PointerToken == 0 && request.Steps.Count > 0)
                request.PointerToken = RimBridgeVirtualPointer.PushTransientOverride(request.Steps[0].Position);

            if (request.StepIndex < request.Steps.Count)
                RimBridgeVirtualPointer.UpdateTransientOverride(request.PointerToken, request.Steps[request.StepIndex].Position);

            if (request.TimeoutMs > 0 && PositiveEnvironmentTick() - request.QueuedTick > request.TimeoutMs)
            {
                request.TimedOut = true;
                completed = request;
                _activeGesture = null;
            }
            else if (request.StepIndex >= request.Steps.Count)
            {
                completed = request;
                _activeGesture = null;
            }
            else
            {
                request.StepIndex++;
            }
        }

        CompleteGesture(completed);
    }

    public static void BeginGlobalEventInjection(ref EventInjectionState state)
    {
        state = null;
        lock (Sync)
        {
            if (!TryGetCurrentStep(out var request, out var step))
                return;

            var currentEvent = Event.current;
            var injectedEvent = currentEvent == null ? new Event() : new Event(currentEvent);
            injectedEvent.type = step.EventType;
            injectedEvent.button = request.Button < 0 ? 0 : request.Button;
            injectedEvent.clickCount = step.ButtonDown ? 1 : 0;
            injectedEvent.modifiers = request.Modifiers;
            injectedEvent.mousePosition = step.Position;
            Event.current = injectedEvent;
            state = new EventInjectionState
            {
                Active = true,
                PreviousEvent = currentEvent
            };
        }
    }

    public static void EndGlobalEventInjection(EventInjectionState state)
    {
        if (state == null || !state.Active)
            return;

        Event.current = state.PreviousEvent;
    }

    public static bool TryPrepareControlEvent(Rect rect, out Event injectedEvent, out int pointerToken, out bool shouldActivate)
    {
        injectedEvent = null;
        pointerToken = 0;
        shouldActivate = false;

        lock (Sync)
        {
            if (!TryGetCurrentStep(out var request, out var step))
                return false;

            var screenRect = ToScreenSnapshot(rect);
            if (!RectContains(screenRect, step.Position))
                return false;

            var currentEvent = Event.current;
            injectedEvent = currentEvent == null ? new Event() : new Event(currentEvent);
            injectedEvent.type = step.EventType;
            injectedEvent.button = request.Button < 0 ? 0 : request.Button;
            injectedEvent.clickCount = step.ButtonDown ? 1 : 0;
            injectedEvent.modifiers = request.Modifiers;
            injectedEvent.mousePosition = rect.center;
            pointerToken = RimBridgeVirtualPointer.PushTransientOverride(step.Position);
            shouldActivate = request.Button == 0 && step.ButtonUp;
            return true;
        }
    }

    public static bool TryPrepareHoverEvent(Rect rect, out Event injectedEvent, out int pointerToken)
    {
        injectedEvent = null;
        pointerToken = 0;

        lock (Sync)
        {
            if (_activeGesture != null || _persistentPointer == null)
                return false;
            if (!RectContains(ToScreenSnapshot(rect), _persistentPointer.ScreenPosition))
                return false;

            var currentEvent = Event.current;
            injectedEvent = currentEvent == null ? new Event() : new Event(currentEvent);
            injectedEvent.type = EventType.MouseMove;
            injectedEvent.button = 0;
            injectedEvent.clickCount = 0;
            injectedEvent.modifiers = EventModifiers.None;
            injectedEvent.mousePosition = rect.center;
            pointerToken = RimBridgeVirtualPointer.PushTransientOverride(_persistentPointer.ScreenPosition);
            return true;
        }
    }

    public static bool ShouldForceControlPress(Rect rect)
    {
        lock (Sync)
        {
            if (!TryGetCurrentStep(out var request, out var step))
                return false;
            if (request.Button != 0 || !step.ButtonUp)
                return false;

            return RectContains(ToScreenSnapshot(rect), step.Position);
        }
    }

    public static bool TryOverrideDraggableResult(Rect rect, ref Widgets.DraggableResult result)
    {
        lock (Sync)
        {
            if (!TryGetCurrentStep(out var request, out var step))
                return false;
            if (request.Button != 0)
                return false;

            var start = request.Steps.FirstOrDefault();
            if (start == null || !RectContains(ToScreenSnapshot(rect), start.Position))
                return false;

            var dragDistanceSquared = (start.Position - step.Position).sqrMagnitude;
            var dragged = dragDistanceSquared > Widgets.DragStartDistanceSquared;
            if (step.EventType == EventType.MouseDrag && dragged)
            {
                result = Widgets.DraggableResult.Dragged;
                return true;
            }

            if (step.ButtonUp && RectContains(ToScreenSnapshot(rect), step.Position))
            {
                result = dragged ? Widgets.DraggableResult.DraggedThenPressed : Widgets.DraggableResult.Pressed;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetMouseButton(int button, out bool result)
    {
        lock (Sync)
        {
            if (!TryGetCurrentStep(out var request, out var step))
            {
                result = false;
                return false;
            }

            result = request.Button == button && step.ButtonHeld;
            return true;
        }
    }

    public static bool TryGetMouseButtonDown(int button, out bool result)
    {
        lock (Sync)
        {
            if (!TryGetCurrentStep(out var request, out var step))
            {
                result = false;
                return false;
            }

            result = request.Button == button && step.ButtonDown;
            return true;
        }
    }

    public static bool TryGetMouseButtonUp(int button, out bool result)
    {
        lock (Sync)
        {
            if (!TryGetCurrentStep(out var request, out var step))
            {
                result = false;
                return false;
            }

            result = request.Button == button && step.ButtonUp;
            return true;
        }
    }

    public static object DescribePointer()
    {
        lock (Sync)
        {
            var active = _activeGesture;
            if (active != null && active.Steps.Count > 0)
            {
                var step = active.Steps[Math.Min(active.StepIndex, active.Steps.Count - 1)];
                return new
                {
                    kind = "pointer",
                    activeGesture = true,
                    command = active.Command,
                    screenPosition = CreatePointPayload(step.Position),
                    button = active.ButtonName,
                    modifiers = active.ModifiersText,
                    from = DescribeTarget(active.From),
                    to = DescribeTarget(active.To)
                };
            }

            if (_persistentPointer == null)
                return null;

            return new
            {
                kind = "pointer",
                activeGesture = false,
                targetKind = _persistentPointer.Kind,
                targetId = _persistentPointer.TargetId,
                label = _persistentPointer.Label,
                screenPosition = CreatePointPayload(_persistentPointer.ScreenPosition),
                target = DescribeTarget(_persistentPointer)
            };
        }
    }

    public static IReadOnlyList<object> DescribeActiveTooltips()
    {
        var activeTips = ActiveTipsField?.GetValue(null) as IEnumerable;
        if (activeTips == null)
            return [];

        var result = new List<object>();
        foreach (var entry in activeTips)
        {
            var value = entry;
            var entryType = value.GetType();
            var valueProperty = entryType.GetProperty("Value");
            if (valueProperty != null)
                value = valueProperty.GetValue(entry);
            if (value == null)
                continue;

            if (!TryReadTipSignal(value, out var signal))
                continue;

            result.Add(new
            {
                uniqueId = signal.uniqueId,
                text = ResolveTipText(signal),
                priority = signal.priority.ToString(),
                delay = signal.delay,
                firstTriggerTime = ReadDouble(ActiveTipFirstTriggerTimeField, value),
                lastTriggerFrame = ReadInt(ActiveTipLastTriggerFrameField, value),
                rect = TryReadTipRect(value)
            });
        }

        return result;
    }

    public static void RegisterTooltipRegion(Rect rect, TipSignal signal, ref Event previousEvent, ref int pointerToken)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement(
            "tooltip_region",
            "tooltip_handler.tip_region",
            rect,
            ResolveTipText(signal));

        lock (Sync)
        {
            if (!TryGetPointerPosition(out var position) || !RectContains(ToScreenSnapshot(rect), position))
                return;

            previousEvent = Event.current;
            var injectedEvent = previousEvent == null ? new Event() : new Event(previousEvent);
            injectedEvent.mousePosition = rect.center;
            pointerToken = RimBridgeVirtualPointer.PushTransientOverride(position);
            Event.current = injectedEvent;
        }
    }

    public static void RestoreTooltipRegionEvent(Event previousEvent, int pointerToken)
    {
        if (pointerToken != 0)
            RimBridgeVirtualPointer.PopTransientOverride(pointerToken);
        if (previousEvent != null)
            Event.current = previousEvent;
    }

    private static PointerGestureRequest QueueMove(PointerTargetSnapshot target, int durationMs, int steps, bool persist, int timeoutMs)
    {
        lock (Sync)
        {
            if (_activeGesture != null)
                throw new InvalidOperationException("A pointer gesture is already pending.");

            var start = _persistentPointer ?? target;
            var resolvedSteps = BuildMoveSteps(start.ScreenPosition, target.ScreenPosition, durationMs, steps);
            var request = new PointerGestureRequest
            {
                Command = "pointer_move",
                From = start,
                To = target,
                TimeoutMs = timeoutMs,
                QueuedTick = PositiveEnvironmentTick(),
                PersistOnComplete = persist,
                Steps = resolvedSteps
            };
            _activeGesture = request;
            return request;
        }
    }

    private static PointerGestureRequest QueueGesture(
        PointerTargetSnapshot from,
        PointerTargetSnapshot to,
        int button,
        string buttonName,
        EventModifiers modifiers,
        string modifiersText,
        int durationMs,
        int steps,
        int holdStartMs,
        int holdEndMs,
        int timeoutMs,
        bool leavePointerAtEnd)
    {
        lock (Sync)
        {
            if (_activeGesture != null)
                throw new InvalidOperationException("A pointer gesture is already pending.");

            var request = new PointerGestureRequest
            {
                Command = "pointer_gesture",
                From = from,
                To = to,
                Button = button,
                ButtonName = buttonName,
                Modifiers = modifiers,
                ModifiersText = modifiersText,
                TimeoutMs = timeoutMs,
                QueuedTick = PositiveEnvironmentTick(),
                LeavePointerAtEnd = leavePointerAtEnd,
                Steps = BuildGestureSteps(from.ScreenPosition, to.ScreenPosition, button, durationMs, steps, holdStartMs, holdEndMs)
            };
            _activeGesture = request;
            return request;
        }
    }

    private static void CancelGesture(PointerGestureRequest request, bool timedOut)
    {
        if (request == null)
            return;

        lock (Sync)
        {
            if (ReferenceEquals(_activeGesture, request))
                _activeGesture = null;

            request.TimedOut = timedOut;
        }

        CompleteGesture(request);
    }

    private static void CompleteGesture(PointerGestureRequest request)
    {
        if (request == null)
            return;

        if (request.PointerToken != 0)
        {
            RimBridgeVirtualPointer.PopTransientOverride(request.PointerToken);
            request.PointerToken = 0;
        }

        if (!request.TimedOut && request.Command == "pointer_gesture")
            TryStoreContextMenuFromGesture(request);

        if (!request.TimedOut && (request.PersistOnComplete || request.LeavePointerAtEnd))
            SetPersistentPointer(request.To);
        else if (!request.LeavePointerAtEnd && request.Command == "pointer_gesture")
            ClearPersistentPointer();

        request.Completion.TrySetResult(request.Command == "pointer_move"
            ? CreateMoveResponse(!request.TimedOut, request.To, request.PersistOnComplete, request.TimedOut ? "Pointer movement timed out." : "Pointer movement completed.", tooltipTimedOut: false)
            : CreateGestureResponse(request));
    }

    private static void SetPersistentPointer(PointerTargetSnapshot target)
    {
        lock (Sync)
        {
            _persistentPointer = target;
            RimBridgeVirtualPointer.SetPersistentPointer(
                kind: "pointer",
                targetId: target.TargetId,
                label: target.Label,
                screenPositionInverted: target.ScreenPosition,
                details: DescribeTarget(target));
        }
    }

    private static void ClearPersistentPointer()
    {
        lock (Sync)
        {
            _persistentPointer = null;
            RimBridgeVirtualPointer.ClearPersistentPointer();
        }
    }

    private static bool TryGetCurrentStep(out PointerGestureRequest request, out PointerStep step)
    {
        request = _activeGesture;
        step = null;
        if (request == null || request.Steps.Count == 0)
            return false;
        var index = Math.Min(request.StepIndex, request.Steps.Count - 1);
        step = request.Steps[index];
        return true;
    }

    private static bool TryGetPointerPosition(out Vector2 position)
    {
        if (TryGetCurrentStep(out _, out var step))
        {
            position = step.Position;
            return true;
        }

        if (_persistentPointer != null)
        {
            position = _persistentPointer.ScreenPosition;
            return true;
        }

        position = default;
        return false;
    }

    private static PointerTargetSnapshot ResolveTarget(Dictionary<string, object> target, Dictionary<string, object> offset)
    {
        if (target == null)
            throw new InvalidOperationException("A target object is required.");

        var kind = ReadString(target, "kind", required: true).Trim();
        PointerTargetSnapshot resolved;
        switch (kind.ToLowerInvariant())
        {
            case "ui":
            case "screen":
                resolved = ResolveScreenTarget(kind, ReadString(target, "id", required: true));
                break;
            case "mapcell":
            case "map_cell":
                resolved = ResolveMapCellTarget(ReadInt(target, "x", required: true), ReadInt(target, "z", required: true));
                break;
            case "pawn":
                resolved = ResolvePawnTarget(ReadString(target, "id", required: false), ReadString(target, "name", required: false));
                break;
            case "thing":
                resolved = ResolveThingTarget(ReadString(target, "id", required: true));
                break;
            case "screenpoint":
            case "screen_point":
                resolved = new PointerTargetSnapshot
                {
                    Kind = "screenPoint",
                    TargetId = null,
                    Label = "Screen point",
                    ScreenPosition = new Vector2(ReadFloat(target, "x", required: true), ReadFloat(target, "y", required: true)),
                    Details = new { kind = "screenPoint" }
                };
                break;
            default:
                throw new InvalidOperationException($"Unsupported pointer target kind '{kind}'.");
        }

        var offsetX = ReadFloat(target, "offsetX", required: false) + ReadFloat(offset, "x", required: false);
        var offsetY = ReadFloat(target, "offsetY", required: false) + ReadFloat(offset, "y", required: false);
        if (Math.Abs(offsetX) > 0.001f || Math.Abs(offsetY) > 0.001f)
            resolved.ScreenPosition += new Vector2(offsetX, offsetY);

        return resolved;
    }

    private static PointerTargetSnapshot ResolveScreenTarget(string requestedKind, string targetId)
    {
        if (!RimWorldTargeting.TryResolveClipArea(targetId, out var clipArea, out var error))
            throw new InvalidOperationException(error);

        var rect = clipArea.Rect;
        return new PointerTargetSnapshot
        {
            Kind = requestedKind,
            TargetId = targetId,
            Label = clipArea.Label,
            Rect = rect,
            ScreenPosition = new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f),
            Details = new
            {
                targetKind = clipArea.TargetKind,
                rect = CreateRectPayload(rect)
            }
        };
    }

    private static PointerTargetSnapshot ResolveMapCellTarget(int x, int z)
    {
        var map = RimWorldState.CurrentMapOrThrow();
        var cell = new IntVec3(x, 0, z);
        if (!cell.InBounds(map))
            throw new InvalidOperationException($"Cell ({x}, {z}) is out of bounds for the current map.");

        return new PointerTargetSnapshot
        {
            Kind = "mapCell",
            TargetId = $"cell:{x}:{z}",
            Label = $"Cell ({x}, {z})",
            ScreenPosition = RimWorldState.CellCenter(cell).MapToUIPosition(),
            Cell = cell,
            Details = new
            {
                cell = new { x, z },
                mapId = RimWorldState.GetMapId(map),
                mapIndex = map.Index
            }
        };
    }

    private static PointerTargetSnapshot ResolvePawnTarget(string pawnId, string pawnName)
    {
        var pawn = RimWorldState.ResolveCurrentMapPawn(pawnName, pawnId);
        return new PointerTargetSnapshot
        {
            Kind = "pawn",
            TargetId = RimWorldState.GetThingId(pawn),
            Label = pawn.Name?.ToStringShort ?? pawn.LabelShort,
            ScreenPosition = pawn.DrawPos.MapToUIPosition(),
            Cell = pawn.Position,
            Details = RimWorldState.DescribePawn(pawn)
        };
    }

    private static PointerTargetSnapshot ResolveThingTarget(string thingId)
    {
        var thing = RimWorldState.ResolveCurrentMapThing(thingId);
        return new PointerTargetSnapshot
        {
            Kind = thing is Pawn ? "pawn" : "thing",
            TargetId = RimWorldState.GetThingId(thing),
            Label = thing.LabelCap.ToString(),
            ScreenPosition = thing.DrawPos.MapToUIPosition(),
            Cell = thing.Position,
            Details = thing is Pawn pawn ? RimWorldState.DescribePawn(pawn) : RimWorldState.DescribeThing(thing)
        };
    }

    private static void TryStoreContextMenuFromGesture(PointerGestureRequest request)
    {
        if (request == null || request.Button != 1)
            return;

        var floatMenu = Find.WindowStack?.FloatMenu;
        if (floatMenu == null)
            return;

        var clickCell = request.To?.Cell ?? request.From?.Cell ?? IntVec3.Invalid;
        var targetLabel = request.To?.Label ?? request.From?.Label ?? string.Empty;
        var options = (FloatMenuOptionsField?.GetValue(floatMenu) as IEnumerable<FloatMenuOption>)?.ToList() ?? [];
        request.ContextMenuSnapshot = RimBridgeContextMenus.Store("pointer_gesture", floatMenu, options, clickCell, targetLabel);
        floatMenu.vanishIfMouseDistant = false;
    }

    private static List<PointerStep> BuildMoveSteps(Vector2 start, Vector2 end, int durationMs, int requestedSteps)
    {
        var steps = NormalizeStepCount(durationMs, requestedSteps);
        var result = new List<PointerStep>();
        for (var index = 0; index <= steps; index++)
        {
            var t = steps == 0 ? 1f : (float)index / steps;
            result.Add(new PointerStep
            {
                Position = Vector2.Lerp(start, end, t),
                EventType = EventType.MouseMove
            });
        }

        return result;
    }

    private static List<PointerStep> BuildGestureSteps(Vector2 start, Vector2 end, int button, int durationMs, int requestedSteps, int holdStartMs, int holdEndMs)
    {
        var moveSteps = NormalizeStepCount(durationMs, requestedSteps);
        var result = new List<PointerStep>
        {
            new()
            {
                Position = start,
                EventType = EventType.MouseDown,
                ButtonDown = true,
                ButtonHeld = true
            }
        };

        AppendHoldSteps(result, start, holdStartMs);
        for (var index = 1; index <= moveSteps; index++)
        {
            var t = (float)index / moveSteps;
            result.Add(new PointerStep
            {
                Position = Vector2.Lerp(start, end, t),
                EventType = EventType.MouseDrag,
                ButtonHeld = true
            });
        }

        if (moveSteps == 0 && (start - end).sqrMagnitude > 0.01f)
        {
            result.Add(new PointerStep
            {
                Position = end,
                EventType = EventType.MouseDrag,
                ButtonHeld = true
            });
        }

        AppendHoldSteps(result, end, holdEndMs);
        result.Add(new PointerStep
        {
            Position = end,
            EventType = EventType.MouseUp,
            ButtonUp = true
        });
        return result;
    }

    private static void AppendHoldSteps(ICollection<PointerStep> steps, Vector2 position, int holdMs)
    {
        var frames = Math.Max(0, (int)Math.Ceiling(holdMs / 50f));
        for (var index = 0; index < frames; index++)
        {
            steps.Add(new PointerStep
            {
                Position = position,
                EventType = EventType.MouseDrag,
                ButtonHeld = true
            });
        }
    }

    private static int NormalizeStepCount(int durationMs, int requestedSteps)
    {
        if (requestedSteps > 0)
            return requestedSteps;
        if (durationMs <= 0)
            return 0;

        return Math.Max(1, (int)Math.Ceiling(durationMs / 50f));
    }

    private static (int Button, string Name) ParseMouseButton(string button)
    {
        var normalized = (button ?? "left").Trim().ToLowerInvariant();
        return normalized switch
        {
            "0" or "left" => (0, "left"),
            "1" or "right" => (1, "right"),
            "2" or "middle" => (2, "middle"),
            _ => throw new InvalidOperationException($"Unsupported mouse button '{button}'. Supported values are 'left', 'right', and 'middle'.")
        };
    }

    private static (EventModifiers Modifiers, string Text) ParseModifiers(IEnumerable<object> modifiers)
    {
        if (modifiers == null)
            return (EventModifiers.None, "none");

        var parsed = EventModifiers.None;
        var seen = new List<string>();
        foreach (var modifier in modifiers)
        {
            var normalized = Convert.ToString(modifier)?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || normalized == "none")
                continue;

            switch (normalized)
            {
                case "shift":
                    parsed |= EventModifiers.Shift;
                    seen.Add("shift");
                    break;
                case "ctrl":
                case "control":
                    parsed |= EventModifiers.Control;
                    seen.Add("ctrl");
                    break;
                case "alt":
                case "option":
                    parsed |= EventModifiers.Alt;
                    seen.Add("alt");
                    break;
                case "cmd":
                case "command":
                case "meta":
                    parsed |= EventModifiers.Command;
                    seen.Add("command");
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported modifier '{modifier}'. Supported values are shift, ctrl, alt, and command.");
            }
        }

        return seen.Count == 0 ? (parsed, "none") : (parsed, string.Join(",", seen.Distinct(StringComparer.Ordinal)));
    }

    private static object CreateMoveResponse(bool success, PointerTargetSnapshot target, bool persisted, string message, bool tooltipTimedOut)
    {
        return new
        {
            success,
            command = "pointer_move",
            message,
            persisted,
            tooltipTimedOut,
            target = DescribeTarget(target),
            pointer = DescribePointer(),
            activeTooltips = DescribeActiveTooltips()
        };
    }

    private static object CreateGestureResponse(PointerGestureRequest request)
    {
        return new
        {
            success = !request.TimedOut,
            command = "pointer_gesture",
            message = request.TimedOut ? "Pointer gesture timed out." : "Pointer gesture completed.",
            from = DescribeTarget(request.From),
            to = DescribeTarget(request.To),
            button = request.ButtonName,
            modifiers = request.ModifiersText,
            stepCount = request.Steps.Count,
            leavePointerAtEnd = request.LeavePointerAtEnd,
            contextMenu = DescribeContextMenu(request.ContextMenuSnapshot),
            pointer = DescribePointer(),
            activeTooltips = DescribeActiveTooltips()
        };
    }

    private static object DescribeTarget(PointerTargetSnapshot target)
    {
        if (target == null)
            return null;

        return new
        {
            kind = target.Kind,
            targetId = target.TargetId,
            label = target.Label,
            screenPosition = CreatePointPayload(target.ScreenPosition),
            rect = target.Rect == null ? null : CreateRectPayload(target.Rect),
            details = target.Details
        };
    }

    private static object DescribeContextMenu(ContextMenuSnapshot snapshot)
    {
        if (snapshot == null)
            return null;

        return new
        {
            menuId = snapshot.Id,
            provider = snapshot.Provider,
            target = snapshot.TargetLabel,
            clickCell = snapshot.ClickCell.IsValid ? new { x = snapshot.ClickCell.x, z = snapshot.ClickCell.z } : null,
            optionCount = snapshot.Options.Count,
            options = snapshot.Options.Select((option, index) => new
            {
                index = index + 1,
                label = option.Label,
                disabled = option.Disabled,
                hasAction = option.action != null
            }).ToList()
        };
    }

    private static object CreatePointPayload(Vector2 position)
    {
        return new
        {
            x = position.x,
            y = position.y
        };
    }

    private static object CreateRectPayload(UiRectSnapshot rect)
    {
        return new
        {
            x = rect.X,
            y = rect.Y,
            width = rect.Width,
            height = rect.Height
        };
    }

    private static UiRectSnapshot ToScreenSnapshot(Rect rect)
    {
        var topLeft = UI.GUIToScreenPoint(new Vector2(rect.xMin, rect.yMin));
        var bottomRight = UI.GUIToScreenPoint(new Vector2(rect.xMax, rect.yMax));
        return new UiRectSnapshot
        {
            X = Math.Min(topLeft.x, bottomRight.x),
            Y = Math.Min(topLeft.y, bottomRight.y),
            Width = Math.Abs(bottomRight.x - topLeft.x),
            Height = Math.Abs(bottomRight.y - topLeft.y)
        };
    }

    private static bool RectContains(UiRectSnapshot rect, Vector2 position)
    {
        if (rect == null)
            return false;

        return position.x >= rect.X
            && position.x <= rect.X + rect.Width
            && position.y >= rect.Y
            && position.y <= rect.Y + rect.Height;
    }

    private static bool WaitForActiveTooltip(int timeoutMs)
    {
        var started = PositiveEnvironmentTick();
        while (PositiveEnvironmentTick() - started < timeoutMs)
        {
            if (DescribeActiveTooltips().Count > 0)
                return true;

            System.Threading.Thread.Sleep(25);
        }

        return DescribeActiveTooltips().Count > 0;
    }

    private static bool TryReadTipSignal(object activeTip, out TipSignal signal)
    {
        signal = default;
        if (activeTip == null || ActiveTipSignalField == null)
            return false;

        var raw = ActiveTipSignalField.GetValue(activeTip);
        if (raw is not TipSignal tipSignal)
            return false;

        signal = tipSignal;
        return true;
    }

    private static string ResolveTipText(TipSignal signal)
    {
        try
        {
            var text = signal.textGetter != null ? signal.textGetter() : signal.text;
            return (text ?? string.Empty).TrimEnd();
        }
        catch (Exception ex)
        {
            Log.Error(ex.ToString());
            return "Error getting tip text.";
        }
    }

    private static object TryReadTipRect(object activeTip)
    {
        if (activeTip is not ActiveTip tip)
            return null;

        var rect = tip.TipRect;
        return new
        {
            x = rect.x,
            y = rect.y,
            width = rect.width,
            height = rect.height
        };
    }

    private static double? ReadDouble(FieldInfo field, object instance)
    {
        if (field?.GetValue(instance) is double value)
            return value;

        return null;
    }

    private static int? ReadInt(FieldInfo field, object instance)
    {
        if (field?.GetValue(instance) is int value)
            return value;

        return null;
    }

    private static string ReadString(IDictionary<string, object> values, string key, bool required)
    {
        if (values != null && values.TryGetValue(key, out var value) && value != null)
            return Convert.ToString(value);
        if (required)
            throw new InvalidOperationException($"Target field '{key}' is required.");

        return null;
    }

    private static int ReadInt(IDictionary<string, object> values, string key, bool required)
    {
        if (values != null && values.TryGetValue(key, out var value) && value != null)
            return Convert.ToInt32(value);
        if (required)
            throw new InvalidOperationException($"Target field '{key}' is required.");

        return 0;
    }

    private static float ReadFloat(IDictionary<string, object> values, string key, bool required)
    {
        if (values != null && values.TryGetValue(key, out var value) && value != null)
            return Convert.ToSingle(value);
        if (required)
            throw new InvalidOperationException($"Target field '{key}' is required.");

        return 0f;
    }

    private static int NormalizeTimeout(int timeoutMs)
    {
        return timeoutMs <= 0 ? 2000 : timeoutMs;
    }

    private static int PositiveEnvironmentTick()
    {
        var tick = Environment.TickCount;
        return tick >= 0 ? tick : -tick;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton), new[] { typeof(int) })]
internal static class Input_GetMouseButton_Pointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(int button, ref bool __result)
    {
        if (!RimBridgePointer.TryGetMouseButton(button, out var result))
            return true;

        __result = result;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown), new[] { typeof(int) })]
internal static class Input_GetMouseButtonDown_Pointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(int button, ref bool __result)
    {
        if (!RimBridgePointer.TryGetMouseButtonDown(button, out var result))
            return true;

        __result = result;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp), new[] { typeof(int) })]
internal static class Input_GetMouseButtonUp_Pointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(int button, ref bool __result)
    {
        if (!RimBridgePointer.TryGetMouseButtonUp(button, out var result))
            return true;

        __result = result;
        return false;
    }
}

[HarmonyPatch]
internal static class TooltipHandler_TipRegion_Pointer_Patch
{
    public sealed class InjectionState
    {
        public Event PreviousEvent;

        public int PointerToken;
    }

    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(TooltipHandler), nameof(TooltipHandler.TipRegion), [typeof(Rect), typeof(TipSignal)]);
    }

    public static void Prefix(Rect rect, TipSignal tip, ref InjectionState __state)
    {
        __state = new InjectionState();
        RimBridgePointer.RegisterTooltipRegion(rect, tip, ref __state.PreviousEvent, ref __state.PointerToken);
    }

    public static void Postfix(InjectionState __state)
    {
        if (__state != null)
            RimBridgePointer.RestoreTooltipRegionEvent(__state.PreviousEvent, __state.PointerToken);
    }
}

[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
internal static class UIRoot_Play_UIRootOnGUI_PointerInjection_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(ref RimBridgePointer.EventInjectionState __state)
    {
        RimBridgePointer.BeginGlobalEventInjection(ref __state);
    }

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(RimBridgePointer.EventInjectionState __state)
    {
        RimBridgePointer.EndGlobalEventInjection(__state);
    }
}
