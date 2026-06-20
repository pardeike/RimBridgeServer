using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgeVirtualPointer
{
    private const int DefaultPersistentDurationMs = 15000;
    private const int MinimumPersistentDurationMs = 250;
    private const int MaximumPersistentDurationMs = 60000;

    private sealed class PointerState
    {
        public string Kind { get; set; } = string.Empty;

        public string TargetId { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public Vector2 ScreenPosition { get; set; }

        public Vector2 ScreenPositionInverted { get; set; }

        public object Details { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        public int DurationMs { get; set; }

        public bool ClearOnRealInput { get; set; } = true;
    }

    private sealed class TransientOverride
    {
        public int Token { get; set; }

        public Vector2 ScreenPosition { get; set; }

        public Vector2 ScreenPositionInverted { get; set; }
    }

    private sealed class MouseOverDiagnostic
    {
        public DateTime CapturedAtUtc { get; set; }

        public int Frame { get; set; }

        public string EventType { get; set; } = string.Empty;

        public string RawEventType { get; set; } = string.Empty;

        public UiRectSnapshot Rect { get; set; }

        public Vector2 GuiClipOriginScreen { get; set; }

        public Vector2 EventMousePosition { get; set; }

        public bool InputBlocked { get; set; }

        public bool Matched { get; set; }
    }

    private static readonly object Sync = new();
    private const int MaxMouseOverDiagnostics = 12;
    private const int MaxMouseOverMatchDiagnostics = 8;
    private static readonly List<TransientOverride> TransientOverrides = [];
    private static readonly Queue<MouseOverDiagnostic> MouseOverDiagnostics = new();
    private static readonly Queue<MouseOverDiagnostic> MouseOverMatchDiagnostics = new();
    private static PointerState _persistentPointer;
    private static int _nextToken = 1;
    private static Vector2? _lastEventMousePosition;
    private static int _lastEventMousePositionFrame;

    public static int PushTransientOverride(Vector2 screenPositionInverted)
    {
        lock (Sync)
        {
            var token = _nextToken++;
            TransientOverrides.Add(new TransientOverride
            {
                Token = token,
                ScreenPosition = InvertedToBottomLeft(screenPositionInverted),
                ScreenPositionInverted = screenPositionInverted
            });
            return token;
        }
    }

    public static void PopTransientOverride(int token)
    {
        if (token == 0)
            return;

        lock (Sync)
        {
            var index = TransientOverrides.FindLastIndex(entry => entry.Token == token);
            if (index >= 0)
                TransientOverrides.RemoveAt(index);
        }
    }

    public static void SetPersistentPointer(
        string kind,
        string targetId,
        string label,
        Vector2 screenPositionInverted,
        object details,
        int? durationMs = null,
        bool clearOnRealInput = true)
    {
        var normalizedDurationMs = NormalizePersistentDurationMs(durationMs);
        var createdAtUtc = DateTime.UtcNow;
        lock (Sync)
        {
            _persistentPointer = new PointerState
            {
                Kind = kind ?? string.Empty,
                TargetId = targetId ?? string.Empty,
                Label = label ?? string.Empty,
                ScreenPosition = InvertedToBottomLeft(screenPositionInverted),
                ScreenPositionInverted = screenPositionInverted,
                Details = details,
                CreatedAtUtc = createdAtUtc,
                ExpiresAtUtc = createdAtUtc.AddMilliseconds(normalizedDurationMs),
                DurationMs = normalizedDurationMs,
                ClearOnRealInput = clearOnRealInput
            };
            ClearDiagnosticsLocked();
        }
    }

    public static void UpdatePersistentPointerPosition(Vector2 screenPositionInverted)
    {
        lock (Sync)
        {
            if (!TryGetActivePersistentPointerLocked(out var pointer))
                return;

            pointer.ScreenPositionInverted = screenPositionInverted;
            pointer.ScreenPosition = InvertedToBottomLeft(screenPositionInverted);
        }
    }

    public static void ClearPersistentPointer()
    {
        lock (Sync)
        {
            _persistentPointer = null;
        }
    }

    public static object DescribePersistentPointer()
    {
        lock (Sync)
        {
            if (!TryGetActivePersistentPointerLocked(out var pointer))
                return null;

            return new
            {
                kind = pointer.Kind,
                targetId = string.IsNullOrWhiteSpace(pointer.TargetId) ? null : pointer.TargetId,
                label = string.IsNullOrWhiteSpace(pointer.Label) ? null : pointer.Label,
                screenPosition = new
                {
                    x = pointer.ScreenPositionInverted.x,
                    y = pointer.ScreenPositionInverted.y
                },
                expiresAtUtc = pointer.ExpiresAtUtc,
                durationMs = pointer.DurationMs,
                clearOnRealInput = pointer.ClearOnRealInput,
                details = pointer.Details,
                inputDiagnostics = CreateInputDiagnosticsLocked(pointer)
            };
        }
    }

    public static bool HasActivePersistentPointer()
    {
        lock (Sync)
        {
            return TryGetActivePersistentPointerLocked(out _);
        }
    }

    public static bool ClearExpiredPersistentPointer()
    {
        lock (Sync)
        {
            if (_persistentPointer == null || DateTime.UtcNow <= _persistentPointer.ExpiresAtUtc)
                return false;

            _persistentPointer = null;
            ClearDiagnosticsLocked();
            return true;
        }
    }

    public static bool ClearPersistentPointerForRealInput(Event currentEvent)
    {
        if (!IsRealPointerEvent(currentEvent))
            return false;

        return ClearPersistentPointerForRealInput();
    }

    public static bool ClearPersistentPointerForRealInputState()
    {
        if (!HasRealInputState())
            return false;

        return ClearPersistentPointerForRealInput();
    }

    public static bool TryGetMousePositionOnUi(out Vector2 position)
    {
        lock (Sync)
        {
            if (TryGetTransientOverride(out var transient))
            {
                position = transient.ScreenPosition;
                return true;
            }

            if (TryGetActivePersistentPointerLocked(out var pointer))
            {
                position = pointer.ScreenPosition;
                return true;
            }
        }

        position = default;
        return false;
    }

    public static bool TryGetMousePositionOnUiInverted(out Vector2 position)
    {
        lock (Sync)
        {
            if (TryGetTransientOverride(out var transient))
            {
                position = transient.ScreenPositionInverted;
                return true;
            }

            if (TryGetActivePersistentPointerLocked(out var pointer))
            {
                position = pointer.ScreenPositionInverted;
                return true;
            }
        }

        position = default;
        return false;
    }

    public static bool TryGetInputMousePosition(out Vector3 position)
    {
        if (!TryGetMousePositionOnUi(out var uiPosition))
        {
            position = default;
            return false;
        }

        position = new Vector3(uiPosition.x * Prefs.UIScale, uiPosition.y * Prefs.UIScale, 0f);
        return true;
    }

    public static bool TryGetEventMousePosition(out Vector2 position)
    {
        if (!TryGetMousePositionOnUiInverted(out var screenPositionInverted))
        {
            position = default;
            return false;
        }

        var clipOrigin = UI.GUIToScreenPoint(Vector2.zero);
        position = new Vector2(screenPositionInverted.x - clipOrigin.x, screenPositionInverted.y - clipOrigin.y);
        RecordEventMousePosition(position);
        return true;
    }

    public static void RecordMouseIsOver(Rect rect, Vector2 eventMousePosition, bool inputBlocked, bool matched)
    {
        lock (Sync)
        {
            if (!TryGetActivePersistentPointerLocked(out _))
                return;

            var currentEvent = Event.current;
            var diagnostic = new MouseOverDiagnostic
            {
                CapturedAtUtc = DateTime.UtcNow,
                Frame = Time.frameCount,
                EventType = currentEvent?.type.ToString() ?? string.Empty,
                RawEventType = currentEvent?.rawType.ToString() ?? string.Empty,
                Rect = new UiRectSnapshot
                {
                    X = rect.x,
                    Y = rect.y,
                    Width = rect.width,
                    Height = rect.height
                },
                GuiClipOriginScreen = UI.GUIToScreenPoint(Vector2.zero),
                EventMousePosition = eventMousePosition,
                InputBlocked = inputBlocked,
                Matched = matched
            };

            EnqueueBounded(MouseOverDiagnostics, diagnostic, MaxMouseOverDiagnostics);
            if (matched)
                EnqueueBounded(MouseOverMatchDiagnostics, diagnostic, MaxMouseOverMatchDiagnostics);
        }
    }

    private static bool TryGetTransientOverride(out TransientOverride transient)
    {
        transient = null;
        if (TransientOverrides.Count == 0)
            return false;

        transient = TransientOverrides[TransientOverrides.Count - 1];
        return transient != null;
    }

    private static bool TryGetActivePersistentPointerLocked(out PointerState pointer)
    {
        pointer = _persistentPointer;
        if (pointer == null)
            return false;

        if (DateTime.UtcNow <= pointer.ExpiresAtUtc)
            return true;

        _persistentPointer = null;
        ClearDiagnosticsLocked();
        pointer = null;
        return false;
    }

    private static void RecordEventMousePosition(Vector2 position)
    {
        lock (Sync)
        {
            if (!TryGetActivePersistentPointerLocked(out _))
                return;

            _lastEventMousePosition = position;
            _lastEventMousePositionFrame = Time.frameCount;
        }
    }

    private static object CreateInputDiagnosticsLocked(PointerState pointer)
    {
        return new
        {
            uiMousePosition = new
            {
                x = pointer.ScreenPosition.x,
                y = pointer.ScreenPosition.y
            },
            uiMousePositionInverted = new
            {
                x = pointer.ScreenPositionInverted.x,
                y = pointer.ScreenPositionInverted.y
            },
            eventMousePosition = _lastEventMousePosition.HasValue
                ? new
                {
                    x = _lastEventMousePosition.Value.x,
                    y = _lastEventMousePosition.Value.y,
                    frame = _lastEventMousePositionFrame
                }
                : null,
            recentMouseIsOverChecks = DescribeMouseOverDiagnostics(MouseOverDiagnostics),
            recentMouseIsOverMatches = DescribeMouseOverDiagnostics(MouseOverMatchDiagnostics)
        };
    }

    private static List<object> DescribeMouseOverDiagnostics(IEnumerable<MouseOverDiagnostic> diagnostics)
    {
        var result = new List<object>();
        foreach (var diagnostic in diagnostics)
        {
            result.Add(new
            {
                capturedAtUtc = diagnostic.CapturedAtUtc,
                frame = diagnostic.Frame,
                eventType = string.IsNullOrWhiteSpace(diagnostic.EventType) ? null : diagnostic.EventType,
                rawEventType = string.IsNullOrWhiteSpace(diagnostic.RawEventType) ? null : diagnostic.RawEventType,
                rect = new
                {
                    x = diagnostic.Rect.X,
                    y = diagnostic.Rect.Y,
                    width = diagnostic.Rect.Width,
                    height = diagnostic.Rect.Height
                },
                eventMousePosition = new
                {
                    x = diagnostic.EventMousePosition.x,
                    y = diagnostic.EventMousePosition.y
                },
                guiClipOriginScreen = new
                {
                    x = diagnostic.GuiClipOriginScreen.x,
                    y = diagnostic.GuiClipOriginScreen.y
                },
                inputBlocked = diagnostic.InputBlocked,
                matched = diagnostic.Matched
            });
        }

        return result;
    }

    private static void EnqueueBounded<T>(Queue<T> queue, T item, int maximumCount)
    {
        queue.Enqueue(item);
        while (queue.Count > maximumCount)
            queue.Dequeue();
    }

    private static void ClearDiagnosticsLocked()
    {
        MouseOverDiagnostics.Clear();
        MouseOverMatchDiagnostics.Clear();
        _lastEventMousePosition = null;
        _lastEventMousePositionFrame = 0;
    }

    private static int NormalizePersistentDurationMs(int? durationMs)
    {
        if (!durationMs.HasValue || durationMs.Value <= 0)
            return DefaultPersistentDurationMs;

        return Mathf.Clamp(durationMs.Value, MinimumPersistentDurationMs, MaximumPersistentDurationMs);
    }

    private static bool IsRealPointerEvent(Event currentEvent)
    {
        if (currentEvent == null)
            return false;

        return IsPointerEvent(currentEvent.type) || IsPointerEvent(currentEvent.rawType);
    }

    private static bool ClearPersistentPointerForRealInput()
    {
        lock (Sync)
        {
            if (!TryGetActivePersistentPointerLocked(out var pointer) || !pointer.ClearOnRealInput)
                return false;

            _persistentPointer = null;
            ClearDiagnosticsLocked();
            return true;
        }
    }

    private static bool HasRealInputState()
    {
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            return true;

        var scrollDelta = Input.mouseScrollDelta;
        return scrollDelta.sqrMagnitude > 0.0001f;
    }

    private static bool IsPointerEvent(EventType eventType)
    {
        switch (eventType)
        {
            case EventType.MouseDown:
            case EventType.MouseUp:
            case EventType.MouseMove:
            case EventType.MouseDrag:
            case EventType.ScrollWheel:
                return true;
            default:
                return false;
        }
    }

    private static Vector2 InvertedToBottomLeft(Vector2 screenPositionInverted)
    {
        var height = UI.screenHeight > 0 ? UI.screenHeight : Mathf.RoundToInt((float)Screen.height / Prefs.UIScale);
        return new Vector2(screenPositionInverted.x, height - screenPositionInverted.y);
    }
}

[HarmonyPatch(typeof(UI), nameof(UI.MousePositionOnUI), MethodType.Getter)]
internal static class UI_MousePositionOnUI_VirtualPointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref Vector2 __result)
    {
        if (!RimBridgeVirtualPointer.TryGetMousePositionOnUi(out var position))
            return true;

        __result = position;
        return false;
    }
}

[HarmonyPatch(typeof(UI), nameof(UI.MousePosUIInvertedUseEventIfCan), MethodType.Getter)]
internal static class UI_MousePosUIInvertedUseEventIfCan_VirtualPointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref Vector2 __result)
    {
        if (!RimBridgeVirtualPointer.TryGetMousePositionOnUiInverted(out var position))
            return true;

        __result = position;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.mousePosition), MethodType.Getter)]
internal static class Input_MousePosition_VirtualPointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref Vector3 __result)
    {
        if (!RimBridgeVirtualPointer.TryGetInputMousePosition(out var position))
            return true;

        __result = position;
        return false;
    }
}

[HarmonyPatch(typeof(Event), nameof(Event.mousePosition), MethodType.Getter)]
internal static class Event_MousePosition_VirtualPointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(ref Vector2 __result)
    {
        if (!RimBridgeVirtualPointer.TryGetEventMousePosition(out var position))
            return true;

        __result = position;
        return false;
    }
}

[HarmonyPatch(typeof(Mouse), nameof(Mouse.IsOver), new[] { typeof(Rect) })]
internal static class Mouse_IsOver_VirtualPointer_Patch
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(Rect rect, ref bool __result)
    {
        if (!RimBridgeVirtualPointer.TryGetEventMousePosition(out var position))
            return true;

        var inputBlocked = Mouse.IsInputBlockedNow;
        __result = rect.Contains(position) && !inputBlocked;
        RimBridgeVirtualPointer.RecordMouseIsOver(rect, position, inputBlocked, __result);
        return false;
    }
}
