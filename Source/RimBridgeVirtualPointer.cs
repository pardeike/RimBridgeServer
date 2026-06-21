using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
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

    private sealed class SyntheticMouseState
    {
        public int Button { get; set; } = -1;

        public Vector2 ScreenPosition { get; set; }

        public Vector2 ScreenPositionInverted { get; set; }

        public bool ButtonHeld { get; set; }

        public int ButtonDownFrame { get; set; } = -1;

        public int ButtonUpFrame { get; set; } = -1;

        public int ExpireAfterFrame { get; set; } = -1;
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
    private static SyntheticMouseState _syntheticMouse;
    private static int _nextToken = 1;
    private static Vector2? _lastEventMousePosition;
    private static int _lastEventMousePositionFrame;
    private static readonly object LatePatchSync = new();
    private static readonly List<string> LateInputPatchFailures = [];
    private static bool _lateInputPatchesApplied;
    private static int _lateInputPatchAttemptCount;
    private static int _lateInputPatchSuccessCount;
    private static MethodInfo _unityGetMouseButtonMethod;
    private static MethodInfo _unityGetMouseButtonDownMethod;
    private static MethodInfo _unityGetMouseButtonUpMethod;
    private static MethodInfo _virtualGetMouseButtonMethod;
    private static MethodInfo _virtualGetMouseButtonDownMethod;
    private static MethodInfo _virtualGetMouseButtonUpMethod;

    private sealed class LateInputPatchTarget
    {
        public string Name { get; set; } = string.Empty;

        public MethodBase Original { get; set; }

        public Type TranspilerType { get; set; }
    }

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

    public static void UpdateTransientOverride(int token, Vector2 screenPositionInverted)
    {
        if (token == 0)
            return;

        lock (Sync)
        {
            var index = TransientOverrides.FindLastIndex(entry => entry.Token == token);
            if (index < 0)
                return;

            TransientOverrides[index].ScreenPositionInverted = screenPositionInverted;
            TransientOverrides[index].ScreenPosition = InvertedToBottomLeft(screenPositionInverted);
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

    public static void SetSyntheticMouseState(int button, Vector2 screenPositionInverted, bool buttonHeld, bool buttonDown, bool buttonUp)
    {
        lock (Sync)
        {
            _syntheticMouse = new SyntheticMouseState
            {
                Button = button,
                ScreenPosition = InvertedToBottomLeft(screenPositionInverted),
                ScreenPositionInverted = screenPositionInverted,
                ButtonHeld = buttonHeld,
                ButtonDownFrame = buttonDown ? Time.frameCount : -1,
                ButtonUpFrame = buttonUp ? Time.frameCount : -1
            };
        }
    }

    public static void ReleaseSyntheticMouseState(int button, Vector2 screenPositionInverted, int retainFrames = 1)
    {
        lock (Sync)
        {
            _syntheticMouse = new SyntheticMouseState
            {
                Button = button,
                ScreenPosition = InvertedToBottomLeft(screenPositionInverted),
                ScreenPositionInverted = screenPositionInverted,
                ButtonHeld = false,
                ButtonUpFrame = Time.frameCount,
                ExpireAfterFrame = Time.frameCount + Math.Max(0, retainFrames)
            };
        }
    }

    public static void ScheduleSyntheticMouseStateClear(int retainFrames = 1)
    {
        lock (Sync)
        {
            if (!TryGetSyntheticMouseStateLocked(out var syntheticMouse))
                return;

            syntheticMouse.ExpireAfterFrame = Math.Max(
                syntheticMouse.ExpireAfterFrame,
                Time.frameCount + Math.Max(0, retainFrames));
        }
    }

    public static void ClearSyntheticMouseState()
    {
        lock (Sync)
        {
            _syntheticMouse = null;
        }
    }

    public static void ClearExpiredSyntheticMouseState()
    {
        lock (Sync)
        {
            ClearExpiredSyntheticMouseStateLocked();
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

            if (TryGetSyntheticMouseStateLocked(out var syntheticMouse))
            {
                position = syntheticMouse.ScreenPosition;
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

            if (TryGetSyntheticMouseStateLocked(out var syntheticMouse))
            {
                position = syntheticMouse.ScreenPositionInverted;
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

    public static bool TryGetSyntheticMouseButton(int button, out bool buttonHeld)
    {
        lock (Sync)
        {
            if (!TryGetSyntheticMouseStateLocked(out var syntheticMouse) || !IsStandardMouseButton(button))
            {
                buttonHeld = false;
                return false;
            }

            buttonHeld = syntheticMouse.ButtonHeld && syntheticMouse.Button == button;
            return true;
        }
    }

    public static bool TryGetSyntheticMouseButtonDown(int button, out bool buttonDown)
    {
        lock (Sync)
        {
            if (!TryGetSyntheticMouseStateLocked(out var syntheticMouse) || !IsStandardMouseButton(button))
            {
                buttonDown = false;
                return false;
            }

            buttonDown = syntheticMouse.Button == button && syntheticMouse.ButtonDownFrame == Time.frameCount;
            return true;
        }
    }

    public static bool TryGetSyntheticMouseButtonUp(int button, out bool buttonUp)
    {
        lock (Sync)
        {
            if (!TryGetSyntheticMouseStateLocked(out var syntheticMouse) || !IsStandardMouseButton(button))
            {
                buttonUp = false;
                return false;
            }

            buttonUp = syntheticMouse.Button == button
                && syntheticMouse.ButtonUpFrame >= 0
                && Time.frameCount >= syntheticMouse.ButtonUpFrame
                && Time.frameCount <= syntheticMouse.ButtonUpFrame + 1;
            return true;
        }
    }

    public static bool GetMouseButton(int button)
    {
        return TryGetSyntheticMouseButton(button, out var buttonHeld)
            ? buttonHeld
            : Input.GetMouseButton(button);
    }

    public static bool GetMouseButtonDown(int button)
    {
        return TryGetSyntheticMouseButtonDown(button, out var buttonDown)
            ? buttonDown
            : Input.GetMouseButtonDown(button);
    }

    public static bool GetMouseButtonUp(int button)
    {
        return TryGetSyntheticMouseButtonUp(button, out var buttonUp)
            ? buttonUp
            : Input.GetMouseButtonUp(button);
    }

    public static IEnumerable<CodeInstruction> RedirectInputMouseButtonCalls(IEnumerable<CodeInstruction> instructions)
    {
        var unityGetMouseButtonMethod = UnityGetMouseButtonMethod();
        var unityGetMouseButtonDownMethod = UnityGetMouseButtonDownMethod();
        var unityGetMouseButtonUpMethod = UnityGetMouseButtonUpMethod();
        var virtualGetMouseButtonMethod = VirtualGetMouseButtonMethod();
        var virtualGetMouseButtonDownMethod = VirtualGetMouseButtonDownMethod();
        var virtualGetMouseButtonUpMethod = VirtualGetMouseButtonUpMethod();

        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method)
            {
                if (method == unityGetMouseButtonMethod)
                    instruction.operand = virtualGetMouseButtonMethod;
                else if (method == unityGetMouseButtonDownMethod)
                    instruction.operand = virtualGetMouseButtonDownMethod;
                else if (method == unityGetMouseButtonUpMethod)
                    instruction.operand = virtualGetMouseButtonUpMethod;
            }

            yield return instruction;
        }
    }

    public static void ApplyLateInputPatches()
    {
        lock (LatePatchSync)
        {
            if (_lateInputPatchesApplied)
                return;

            _lateInputPatchesApplied = true;
            LateInputPatchFailures.Clear();
            var targets = CreateLateInputPatchTargets();
            _lateInputPatchAttemptCount = targets.Count;
            _lateInputPatchSuccessCount = 0;
            var harmony = new Harmony("pardeike.rimbridgeserver.virtual-input-late");

            foreach (var target in targets)
            {
                try
                {
                    if (target.Original == null)
                        throw new MissingMethodException(target.Name);

                    var transpiler = AccessTools.Method(target.TranspilerType, "Transpiler");
                    if (transpiler == null)
                        throw new MissingMethodException(target.TranspilerType.FullName, "Transpiler");

                    harmony.Patch(target.Original, transpiler: new HarmonyMethod(transpiler));
                    _lateInputPatchSuccessCount++;
                }
                catch (Exception ex)
                {
                    var failure = $"{target.Name}: {ex.GetType().Name}: {ex.Message}";
                    LateInputPatchFailures.Add(failure);
                    Log.Error($"[RimBridge] LATE_INPUT_PATCH_FAILURE: {failure}");
                }
            }

            if (LateInputPatchFailures.Count == 0)
                Log.Message($"[RimBridge] Applied {_lateInputPatchSuccessCount} late virtual-input patch classes.");
            else
                Log.Warning($"[RimBridge] Applied {_lateInputPatchSuccessCount} of {_lateInputPatchAttemptCount} late virtual-input patch classes. Failed: {LateInputPatchFailures.Count}.");
        }
    }

    public static object DescribeLateInputPatchStatus()
    {
        lock (LatePatchSync)
        {
            return new
            {
                applied = _lateInputPatchesApplied,
                attemptCount = _lateInputPatchAttemptCount,
                successCount = _lateInputPatchSuccessCount,
                failureCount = LateInputPatchFailures.Count,
                failures = LateInputPatchFailures.ToArray()
            };
        }
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

    private static bool TryGetSyntheticMouseStateLocked(out SyntheticMouseState syntheticMouse)
    {
        ClearExpiredSyntheticMouseStateLocked();
        syntheticMouse = _syntheticMouse;
        return syntheticMouse != null;
    }

    private static void ClearExpiredSyntheticMouseStateLocked()
    {
        if (_syntheticMouse == null || _syntheticMouse.ExpireAfterFrame < 0)
            return;

        if (Time.frameCount > _syntheticMouse.ExpireAfterFrame)
            _syntheticMouse = null;
    }

    private static bool IsStandardMouseButton(int button)
    {
        return button >= 0 && button <= 2;
    }

    private static List<LateInputPatchTarget> CreateLateInputPatchTargets()
    {
        return
        [
            new LateInputPatchTarget
            {
                Name = "Verse.CameraDriver.CameraDriverOnGUI",
                Original = AccessTools.Method(typeof(CameraDriver), nameof(CameraDriver.CameraDriverOnGUI)),
                TranspilerType = typeof(CameraDriver_CameraDriverOnGUI_VirtualPointer_Patch)
            },
            new LateInputPatchTarget
            {
                Name = "Verse.CameraDriver.Update",
                Original = AccessTools.Method(typeof(CameraDriver), nameof(CameraDriver.Update)),
                TranspilerType = typeof(CameraDriver_Update_VirtualPointer_Patch)
            },
            new LateInputPatchTarget
            {
                Name = "RimWorld.Planet.WorldCameraDriver.WorldCameraDriverOnGUI",
                Original = AccessTools.Method(typeof(WorldCameraDriver), nameof(WorldCameraDriver.WorldCameraDriverOnGUI)),
                TranspilerType = typeof(WorldCameraDriver_WorldCameraDriverOnGUI_VirtualPointer_Patch)
            },
            new LateInputPatchTarget
            {
                Name = "RimWorld.Selector.HandleMapClicks",
                Original = AccessTools.Method(typeof(Selector), "HandleMapClicks"),
                TranspilerType = typeof(Selector_HandleMapClicks_VirtualPointer_Patch)
            },
            new LateInputPatchTarget
            {
                Name = "Verse.UnityGUIBugsFixer.MouseDrag",
                Original = AccessTools.Method(typeof(UnityGUIBugsFixer), nameof(UnityGUIBugsFixer.MouseDrag)),
                TranspilerType = typeof(UnityGUIBugsFixer_MouseDrag_VirtualPointer_Patch)
            },
            new LateInputPatchTarget
            {
                Name = "Verse.UnityGUIBugsFixer.IsLeftMouseButtonPressed",
                Original = AccessTools.Method(typeof(UnityGUIBugsFixer), nameof(UnityGUIBugsFixer.IsLeftMouseButtonPressed)),
                TranspilerType = typeof(UnityGUIBugsFixer_IsLeftMouseButtonPressed_VirtualPointer_Patch)
            }
        ];
    }

    private static MethodInfo UnityGetMouseButtonMethod()
    {
        return _unityGetMouseButtonMethod ??= AccessTools.Method(typeof(Input), nameof(Input.GetMouseButton), [typeof(int)]);
    }

    private static MethodInfo UnityGetMouseButtonDownMethod()
    {
        return _unityGetMouseButtonDownMethod ??= AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonDown), [typeof(int)]);
    }

    private static MethodInfo UnityGetMouseButtonUpMethod()
    {
        return _unityGetMouseButtonUpMethod ??= AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonUp), [typeof(int)]);
    }

    private static MethodInfo VirtualGetMouseButtonMethod()
    {
        return _virtualGetMouseButtonMethod ??= AccessTools.Method(typeof(RimBridgeVirtualPointer), nameof(GetMouseButton));
    }

    private static MethodInfo VirtualGetMouseButtonDownMethod()
    {
        return _virtualGetMouseButtonDownMethod ??= AccessTools.Method(typeof(RimBridgeVirtualPointer), nameof(GetMouseButtonDown));
    }

    private static MethodInfo VirtualGetMouseButtonUpMethod()
    {
        return _virtualGetMouseButtonUpMethod ??= AccessTools.Method(typeof(RimBridgeVirtualPointer), nameof(GetMouseButtonUp));
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

internal static class CameraDriver_CameraDriverOnGUI_VirtualPointer_Patch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return RimBridgeVirtualPointer.RedirectInputMouseButtonCalls(instructions);
    }
}

internal static class CameraDriver_Update_VirtualPointer_Patch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return RimBridgeVirtualPointer.RedirectInputMouseButtonCalls(instructions);
    }
}

internal static class WorldCameraDriver_WorldCameraDriverOnGUI_VirtualPointer_Patch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return RimBridgeVirtualPointer.RedirectInputMouseButtonCalls(instructions);
    }
}

internal static class Selector_HandleMapClicks_VirtualPointer_Patch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return RimBridgeVirtualPointer.RedirectInputMouseButtonCalls(instructions);
    }
}

internal static class UnityGUIBugsFixer_MouseDrag_VirtualPointer_Patch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return RimBridgeVirtualPointer.RedirectInputMouseButtonCalls(instructions);
    }
}

internal static class UnityGUIBugsFixer_IsLeftMouseButtonPressed_VirtualPointer_Patch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return RimBridgeVirtualPointer.RedirectInputMouseButtonCalls(instructions);
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
