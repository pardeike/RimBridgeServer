using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HarmonyLib;
using RimBridgeServer.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class UiLayoutSnapshot
{
    public int CaptureId { get; set; }

    public int CapturedFrame { get; set; }

    public DateTime CapturedAtUtc { get; set; }

    public List<UiLayoutSurfaceSnapshot> Surfaces { get; set; } = [];
}

internal sealed class UiLayoutSurfaceSnapshot
{
    public int SurfaceIndex { get; set; }

    public string CaptureTargetId { get; set; } = string.Empty;

    public string SurfaceTargetId { get; set; } = string.Empty;

    public string SurfaceKind { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; }

    public string Label { get; set; }

    public string SemanticKind { get; set; } = string.Empty;

    public object SemanticDetails { get; set; }

    public UiRectSnapshot Rect { get; set; } = new();

    public UiRectSnapshot ScreenRect { get; set; } = new();

    public List<UiLayoutElementSnapshot> Elements { get; set; } = [];
}

internal sealed class UiLayoutScrollSnapshot
{
    public float OffsetX { get; set; }

    public float OffsetY { get; set; }

    public float MaxOffsetX { get; set; }

    public float MaxOffsetY { get; set; }

    public bool CanScrollX { get; set; }

    public bool CanScrollY { get; set; }

    public bool AtLeft { get; set; }

    public bool AtRight { get; set; }

    public bool AtTop { get; set; }

    public bool AtBottom { get; set; }

    public UiRectSnapshot ViewportRect { get; set; } = new();

    public UiRectSnapshot ViewportScreenRect { get; set; } = new();

    public UiRectSnapshot ContentRect { get; set; } = new();
}

internal sealed class UiLayoutElementSnapshot
{
    public int ElementIndex { get; set; }

    public string TargetId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Label { get; set; }

    public string ValueText { get; set; }

    public bool Actionable { get; set; }

    public bool ClipCapable { get; set; } = true;

    public bool? Checked { get; set; }

    public bool? Disabled { get; set; }

    public int Depth { get; set; }

    public string ParentTargetId { get; set; }

    public UiRectSnapshot Rect { get; set; } = new();

    public UiRectSnapshot ScreenRect { get; set; } = new();

    public UiLayoutScrollSnapshot Scroll { get; set; }
}

internal static class RimBridgeUiWorkbench
{
    private enum ClickPhase
    {
        MouseDown = 0,
        MouseUp = 1
    }

    private sealed class CaptureRequest
    {
        public int CaptureId { get; set; }

        public string SurfaceTargetId { get; set; } = string.Empty;

        public UiLayoutSnapshot Snapshot { get; set; } = new();

        public TaskCompletionSource<UiLayoutSnapshot> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HasCapturedSurface { get; set; }

        public int CapturedFrame { get; set; } = -1;
    }

    private sealed class ClickRequest
    {
        public string TargetId { get; set; } = string.Empty;

        public string SurfaceTargetId { get; set; } = string.Empty;

        public int ElementIndex { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public UiRectSnapshot Rect { get; set; } = new();

        public int Depth { get; set; }

        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Activated { get; set; }

        public int ActivatedFrame { get; set; } = -1;

        public ClickPhase Phase { get; set; } = ClickPhase.MouseDown;

        public int PhaseInjectedFrame { get; set; } = -1;

        public string Message { get; set; } = string.Empty;
    }

    private sealed class ScrollRequest
    {
        public string TargetId { get; set; } = string.Empty;

        public string SurfaceTargetId { get; set; } = string.Empty;

        public int ElementIndex { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public UiRectSnapshot Rect { get; set; } = new();

        public int Depth { get; set; }

        public float DeltaX { get; set; }

        public float DeltaY { get; set; }

        public float? TargetX { get; set; }

        public float? TargetY { get; set; }

        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Applied { get; set; }

        public int AppliedFrame { get; set; } = -1;

        public UiLayoutScrollSnapshot Before { get; set; }

        public UiLayoutScrollSnapshot After { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    internal enum TextEntryPhase
    {
        FocusDown = 0,
        FocusUp = 1,
        SelectAll = 2,
        TypeChar = 3,
        Instant = 4,
        Done = 5
    }

    private sealed class TextEntryRequest
    {
        public string TargetId { get; set; } = string.Empty;

        public string SurfaceTargetId { get; set; } = string.Empty;

        public int ElementIndex { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public UiRectSnapshot Rect { get; set; } = new();

        public int Depth { get; set; }

        public string Text { get; set; } = string.Empty;

        public string ControlName { get; set; }

        public bool Typed { get; set; }

        public bool ClearFirst { get; set; }

        public int MinCharIntervalMs { get; set; }

        public int JitterPercent { get; set; }

        public TextEntryPhase Phase { get; set; } = TextEntryPhase.FocusDown;

        public int PhaseInjectedFrame { get; set; } = -1;

        public int TypedCount { get; set; }

        public int NextCharAtTicks { get; set; }

        public int CapturedKeyboardControl { get; set; }

        public string ObservedValue { get; set; }

        public string OriginalValue { get; set; } = string.Empty;

        public bool OriginalValueKnown { get; set; }

        public int RetryStrikes { get; set; }

        public int LastMatchedFrame { get; set; } = -1;

        public System.Random Jitter { get; } = new();

        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Message { get; set; } = string.Empty;
    }

    internal sealed class TextFieldPatchState
    {
        public bool EventOverridden { get; set; }

        public Event PreviousEvent { get; set; }

        public bool OwnsInjection { get; set; }

        public TextEntryPhase PhaseAtInjection { get; set; }

        public string ExpectedValue { get; set; }
    }

    private sealed class HoverRequest
    {
        public string TargetId { get; set; } = string.Empty;

        public string SurfaceTargetId { get; set; } = string.Empty;

        public int ElementIndex { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public UiRectSnapshot Rect { get; set; } = new();

        public int Depth { get; set; }

        public string Anchor { get; set; } = "center";

        public float OffsetX { get; set; }

        public float OffsetY { get; set; }
    }

    private sealed class SurfaceDescriptor
    {
        public string SurfaceTargetId { get; set; } = string.Empty;

        public string SurfaceKind { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Title { get; set; }

        public string Label { get; set; }

        public string SemanticKind { get; set; } = string.Empty;

        public object SemanticDetails { get; set; }

        public UiRectSnapshot Rect { get; set; } = new();

        public UiRectSnapshot ScreenRect { get; set; }
    }

    private sealed class RegisteredElementFingerprint
    {
        public int ElementIndex { get; set; }

        public string TargetId { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Label { get; set; }

        public string ValueText { get; set; }

        public bool Actionable { get; set; }

        public bool? Checked { get; set; }

        public bool? Disabled { get; set; }

        public UiRectSnapshot Rect { get; set; } = new();
    }

    private sealed class LiveSurfaceState
    {
        public string SurfaceTargetId { get; set; } = string.Empty;

        public bool TrackingEnabled { get; set; }

        public UiLayoutSurfaceSnapshot CaptureSurface { get; set; }

        public int ElementIndex { get; set; }

        public int CompoundDepth { get; set; }

        public Stack<string> ContainerTargetIds { get; } = new();

        public RegisteredElementFingerprint LastRegisteredElement { get; set; }
    }

    private sealed class ActiveInteractionContext
    {
        public UiRectSnapshot Rect { get; set; } = new();

        public bool ShouldActivate { get; set; }
    }

    internal sealed class UiPatchControlState
    {
        public bool TrackingEnabled { get; set; }

        public bool SuppressNested { get; set; }

        public bool ShouldActivate { get; set; }

        public bool ShouldHover { get; set; }

        public Event PreviousEvent { get; set; }

        public bool EventOverridden { get; set; }

        public int TransientPointerToken { get; set; }

        public bool? InitialCheckedState { get; set; }

        public bool RegisteredActiveInteraction { get; set; }

        public UiRectSnapshot InteractionRect { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly Stack<LiveSurfaceState> SurfaceStack = [];
    private static readonly Dictionary<int, UiLayoutSnapshot> RecentCaptures = [];
    private static readonly Queue<int> CaptureOrder = [];
    private static CaptureRequest _pendingCapture;
    private static ClickRequest _pendingClick;
    private static ScrollRequest _pendingScroll;
    private static TextEntryRequest _pendingText;
    private static bool _focusNextTextField;
    private static HoverRequest _hoveredElement;
    private static ActiveInteractionContext _activeInteraction;
    private static int _nextCaptureId = 1;
    private const int MaxRetainedCaptures = 8;
    internal const string SelectionGizmosSurfaceId = "selection-gizmos";

    public static object GetUiLayoutResponse(string surfaceId = null, int timeoutMs = 2000, bool includeOffscreen = false, int offset = 0)
    {
        timeoutMs = timeoutMs <= 0 ? 2000 : timeoutMs;

        CaptureRequest request;
        try
        {
            request = RimBridgeMainThread.Invoke(() => QueueCapture(surfaceId), timeoutMs: 5000);
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                message = ex.Message
            };
        }

        if (!request.Completion.Task.Wait(timeoutMs))
        {
            RimBridgeMainThread.Invoke(() => CancelCapture(request.CaptureId), timeoutMs: 5000);
            return new
            {
                success = false,
                message = BuildTimeoutMessage(surfaceId, timeoutMs)
            };
        }

        var snapshot = request.Completion.Task.GetAwaiter().GetResult();
        return DescribeLayout(snapshot, includeOffscreen, offset);
    }

    public static object ClickUiTargetResponse(string targetId, int timeoutMs = 2000)
    {
        timeoutMs = timeoutMs <= 0 ? 2000 : timeoutMs;
        var before = RimBridgeMainThread.Invoke(RimWorldInput.GetUiState, timeoutMs: 5000);

        ClickRequest request;
        try
        {
            request = RimBridgeMainThread.Invoke(() => QueueClick(targetId), timeoutMs: 5000);
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                command = "click_ui_target",
                changed = false,
                message = ex.Message,
                targetId,
                before = RimWorldInput.DescribeUiState(before),
                after = RimWorldInput.DescribeUiState(before)
            };
        }

        if (!request.Completion.Task.Wait(timeoutMs))
        {
            RimBridgeMainThread.Invoke(() => CancelClick(targetId), timeoutMs: 5000);
            return new
            {
                success = false,
                command = "click_ui_target",
                changed = false,
                message = $"Timed out waiting {timeoutMs}ms for UI target '{targetId}' to be redrawn. Capture a fresh layout and retry.",
                targetId,
                before = RimWorldInput.DescribeUiState(before),
                after = RimWorldInput.DescribeUiState(before)
            };
        }

        var activated = request.Completion.Task.GetAwaiter().GetResult();
        ContextMenuSnapshot openedMenu = null;
        var after = RimBridgeMainThread.Invoke(() =>
        {
            openedMenu = RimBridgeContextMenus.AdoptOpenMenu("ui-click");
            return RimWorldInput.GetUiState();
        }, timeoutMs: 5000);
        if (!activated)
        {
            return new
            {
                success = false,
                command = "click_ui_target",
                changed = false,
                message = request.Message,
                targetId,
                before = RimWorldInput.DescribeUiState(before),
                after = RimWorldInput.DescribeUiState(after)
            };
        }

        return CreateClickResponse(targetId, before, after, request.Message, openedMenu);
    }

    public static object SetTextFieldResponse(
        string targetId,
        string text,
        string mode = "typed",
        float charsPerSecond = 9f,
        int jitterPercent = 35,
        bool clearFirst = true,
        string controlName = null,
        int timeoutMs = 0)
    {
        var requestedText = text ?? string.Empty;
        var normalizedMode = (mode ?? "typed").Trim().ToLowerInvariant();
        if (!string.Equals(normalizedMode, "typed", StringComparison.Ordinal) && !string.Equals(normalizedMode, "instant", StringComparison.Ordinal))
            return CreateTextEntryFailure(targetId, requestedText, normalizedMode, $"Unsupported mode '{mode}'. Supported modes are typed and instant.");

        var typed = string.Equals(normalizedMode, "typed", StringComparison.Ordinal);
        if (typed && requestedText.IndexOfAny(TextEntryDisallowedCharacters) >= 0)
            return CreateTextEntryFailure(targetId, requestedText, normalizedMode, "typed mode only supports single-line text without tab or newline characters.");
        if (typed && requestedText.Length == 0)
            return CreateTextEntryFailure(targetId, requestedText, normalizedMode, "typed mode needs at least one character. Use mode instant to clear a field.");

        var effectiveCps = charsPerSecond <= 0f ? 9f : Mathf.Clamp(charsPerSecond, 0.5f, 60f);
        var minCharIntervalMs = Mathf.RoundToInt(1000f / effectiveCps);
        var effectiveJitterPercent = Mathf.Clamp(jitterPercent, 0, 90);
        var effectiveTimeoutMs = timeoutMs > 0
            ? timeoutMs
            : typed ? 4000 + Mathf.CeilToInt(requestedText.Length * minCharIntervalMs * 2f) : 4000;

        TextEntryRequest request;
        try
        {
            request = RimBridgeMainThread.Invoke(() => QueueTextEntry(targetId, requestedText, typed, minCharIntervalMs, effectiveJitterPercent, clearFirst, controlName), timeoutMs: 5000);
        }
        catch (Exception ex)
        {
            return CreateTextEntryFailure(targetId, requestedText, normalizedMode, ex.Message);
        }

        if (!request.Completion.Task.Wait(effectiveTimeoutMs))
        {
            RimBridgeMainThread.Invoke(() => CancelTextEntry(targetId), timeoutMs: 5000);
            return CreateTextEntryFailure(targetId, requestedText, normalizedMode, $"Timed out waiting {effectiveTimeoutMs}ms for the text entry to complete. Last observed field value: '{request.ObservedValue ?? "<never drawn>"}'.");
        }

        var succeeded = request.Completion.Task.GetAwaiter().GetResult();
        return new
        {
            success = succeeded,
            command = "set_text_field",
            targetId,
            mode = normalizedMode,
            requestedText,
            finalValue = request.ObservedValue,
            typedCharacters = request.TypedCount,
            message = request.Message
        };
    }

    private static readonly char[] TextEntryDisallowedCharacters = ['\n', '\r', '\t'];

    private static object CreateTextEntryFailure(string targetId, string requestedText, string mode, string message)
    {
        return new
        {
            success = false,
            command = "set_text_field",
            targetId,
            mode,
            requestedText,
            finalValue = (string)null,
            typedCharacters = 0,
            message
        };
    }

    private static TextEntryRequest QueueTextEntry(string targetId, string text, bool typed, int minCharIntervalMs, int jitterPercent, bool clearFirst, string controlName)
    {
        lock (Sync)
        {
            if (_pendingText != null)
                throw new InvalidOperationException("A text entry is already pending.");
            if (_pendingClick != null)
                throw new InvalidOperationException("Cannot enter text while a UI target click is pending.");
            if (_pendingScroll != null)
                throw new InvalidOperationException("Cannot enter text while a UI target scroll is pending.");
            if (_pendingCapture != null)
                throw new InvalidOperationException("Cannot enter text while a UI layout capture is pending.");

            if (!UiLayoutTargetIds.TryParse(targetId, out var target) || target.Kind != UiLayoutTargetKind.Element)
                throw new InvalidOperationException($"Target id '{targetId}' is not a UI element target id returned by rimworld/get_ui_layout.");

            if (!TryResolveTarget(target, out var surface, out var element))
                throw new InvalidOperationException($"UI layout target '{targetId}' is no longer available. Capture a fresh layout snapshot.");
            if (element == null || !string.Equals(element.Kind, "text_field", StringComparison.Ordinal))
                throw new InvalidOperationException($"UI layout target '{targetId}' is not a text_field target; rimworld/set_text_field only drives text fields.");
            if (!string.Equals(element.Source, "widgets.text_field", StringComparison.Ordinal)
                && !string.Equals(element.Source, "gui.text_field", StringComparison.Ordinal))
                throw new InvalidOperationException($"UI layout target '{targetId}' is reported by {element.Source}, which rimworld/set_text_field cannot drive. A single-line labelled entry also reports its input box as a widgets.text_field element; target that one. Multi-line text areas are not supported.");

            var request = new TextEntryRequest
            {
                TargetId = targetId,
                SurfaceTargetId = surface.SurfaceTargetId,
                ElementIndex = element.ElementIndex,
                Kind = element.Kind,
                Source = element.Source,
                Rect = element.Rect,
                Depth = element.Depth,
                Text = text,
                ControlName = string.IsNullOrWhiteSpace(controlName) ? null : controlName.Trim(),
                Typed = typed,
                ClearFirst = clearFirst,
                MinCharIntervalMs = minCharIntervalMs,
                JitterPercent = jitterPercent,
                Phase = typed ? TextEntryPhase.FocusDown : TextEntryPhase.Instant,
                LastMatchedFrame = Time.frameCount
            };
            _pendingText = request;
            return request;
        }
    }

    private static void CancelTextEntry(string targetId)
    {
        lock (Sync)
        {
            if (_pendingText == null || !string.Equals(_pendingText.TargetId, targetId, StringComparison.Ordinal))
                return;

            var request = _pendingText;
            _pendingText = null;
            request.Completion.TrySetCanceled();
        }
    }

    private static bool ShouldDriveTextEntryForCurrentElement(LiveSurfaceState surface, string source, Rect rect)
    {
        if (_pendingText == null || _pendingText.Phase == TextEntryPhase.Done)
            return false;
        if (!string.Equals(_pendingText.SurfaceTargetId, surface.SurfaceTargetId, StringComparison.Ordinal))
            return false;
        if (!string.Equals(_pendingText.Source, source, StringComparison.Ordinal))
            return false;

        if (_pendingText.ElementIndex == surface.ElementIndex)
            return true;

        return _pendingText.Depth == surface.ContainerTargetIds.Count
            && RectApproximatelyEquals(_pendingText.Rect, rect, 1f);
    }

    public static TextFieldPatchState BeginTextField(string source, Rect rect, string label, ref string text)
    {
        lock (Sync)
        {
            var surface = GetCurrentSurface();
            if (surface == null || !surface.TrackingEnabled)
                return null;
            if (surface.CompoundDepth > 0)
                return null;

            RegisterElement(surface, "text_field", source, rect, label, text, actionable: false, checkedState: null, disabled: null);

            if (!ShouldDriveTextEntryForCurrentElement(surface, source, rect))
                return null;

            var request = _pendingText;
            request.LastMatchedFrame = Time.frameCount;
            if (!request.OriginalValueKnown)
            {
                request.OriginalValue = text ?? string.Empty;
                request.OriginalValueKnown = true;
            }

            if (request.PhaseInjectedFrame == Time.frameCount)
                return null;

            // Never hijack a real Layout pass for event injection: IMGUI
            // assigns control ids differently under Layout, so a focus grab
            // or keystroke delivered there can bind to an id the Repaint
            // passes will not recognise.
            if (request.Phase != TextEntryPhase.Instant && Event.current?.type == EventType.Layout)
                return null;

            switch (request.Phase)
            {
                case TextEntryPhase.Instant:
                {
                    text = request.Text;
                    request.PhaseInjectedFrame = Time.frameCount;
                    return new TextFieldPatchState
                    {
                        OwnsInjection = true,
                        PhaseAtInjection = TextEntryPhase.Instant,
                        ExpectedValue = request.Text
                    };
                }

                case TextEntryPhase.FocusDown:
                case TextEntryPhase.FocusUp:
                {
                    if (request.ControlName != null)
                    {
                        // Named controls get RimWorld's own programmatic focus
                        // path: id-safe, and the owning mod's focused-control
                        // checks keep working because the name is its own.
                        GUI.FocusControl(request.ControlName);
                        request.PhaseInjectedFrame = Time.frameCount;
                        return new TextFieldPatchState
                        {
                            OwnsInjection = true,
                            PhaseAtInjection = request.Phase
                        };
                    }

                    // Unnamed fields are focused by control id rather than a
                    // synthetic click: the DoTextField prefix hands this draw's
                    // id to GUIUtility.keyboardControl, and Unity's own
                    // DetectFocusChange then runs the field's OnFocus.
                    _focusNextTextField = true;
                    request.PhaseInjectedFrame = Time.frameCount;
                    return new TextFieldPatchState
                    {
                        OwnsInjection = true,
                        PhaseAtInjection = request.Phase
                    };
                }

                case TextEntryPhase.SelectAll:
                {
                    if (FocusHeld(request)
                        && GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) is TextEditor editor)
                    {
                        if (request.ClearFirst)
                            editor.SelectAll();
                        else
                            editor.MoveTextEnd();
                    }

                    request.Phase = TextEntryPhase.TypeChar;
                    request.NextCharAtTicks = Environment.TickCount;
                    goto case TextEntryPhase.TypeChar;
                }

                case TextEntryPhase.TypeChar:
                {
                    if (request.TypedCount >= request.Text.Length)
                        return null;
                    if (unchecked(Environment.TickCount - request.NextCharAtTicks) < 0)
                        return null;
                    if (!FocusHeld(request))
                        return null;

                    var currentEvent = Event.current;
                    var injectedEvent = currentEvent == null ? new Event() : new Event(currentEvent);
                    injectedEvent.type = EventType.KeyDown;
                    injectedEvent.keyCode = KeyCode.None;
                    injectedEvent.character = request.Text[request.TypedCount];
                    injectedEvent.modifiers = EventModifiers.None;
                    request.PhaseInjectedFrame = Time.frameCount;
                    var expectedPrefix = request.Text.Substring(0, request.TypedCount + 1);
                    var state = new TextFieldPatchState
                    {
                        OwnsInjection = true,
                        PhaseAtInjection = TextEntryPhase.TypeChar,
                        EventOverridden = true,
                        PreviousEvent = currentEvent,
                        ExpectedValue = request.ClearFirst ? expectedPrefix : request.OriginalValue + expectedPrefix
                    };
                    Event.current = injectedEvent;
                    return state;
                }
            }

            return null;
        }
    }

    public static void EndTextField(TextFieldPatchState state, ref string result)
    {
        if (state == null)
            return;

        if (state.EventOverridden)
            Event.current = state.PreviousEvent;

        if (!state.OwnsInjection)
            return;

        _focusNextTextField = false;

        TextEntryRequest failed = null;
        lock (Sync)
        {
            var request = _pendingText;
            if (request == null)
                return;

            var previousObserved = request.ObservedValue;
            request.ObservedValue = result;

            switch (state.PhaseAtInjection)
            {
                case TextEntryPhase.FocusDown:
                case TextEntryPhase.FocusUp:
                    if (GUIUtility.keyboardControl != 0
                        && (request.ControlName == null
                            || string.Equals(GUI.GetNameOfFocusedControl(), request.ControlName, StringComparison.Ordinal)))
                        request.CapturedKeyboardControl = GUIUtility.keyboardControl;
                    break;

                case TextEntryPhase.Instant:
                    if (string.Equals(result, state.ExpectedValue, StringComparison.Ordinal))
                    {
                        request.Phase = TextEntryPhase.Done;
                        request.Message = GUIUtility.keyboardControl == 0
                            ? "Replaced the text field value through the live widget path."
                            : "Replaced the text field value through the live widget path. A control currently holds keyboard focus; focused fields can override instant values on the next frame.";
                    }
                    else
                    {
                        request.RetryStrikes++;
                        if (request.RetryStrikes >= 3)
                        {
                            request.Message = $"The text field did not accept the instant value; it reports '{result}'. The field may be validated or focus-held.";
                            failed = request;
                            _pendingText = null;
                        }
                    }
                    break;

                case TextEntryPhase.TypeChar:
                    if (string.Equals(result, state.ExpectedValue, StringComparison.Ordinal))
                    {
                        request.TypedCount++;
                        request.RetryStrikes = 0;
                        request.NextCharAtTicks = unchecked(Environment.TickCount + NextCharDelayMs(request));
                        if (request.TypedCount >= request.Text.Length)
                        {
                            request.Phase = TextEntryPhase.Done;
                            request.Message = $"Typed {request.TypedCount} characters into the text field through the live widget path.";
                        }
                    }
                    else if (!string.Equals(result, previousObserved, StringComparison.Ordinal))
                    {
                        request.Message = $"The text field transformed the input unexpectedly at character {request.TypedCount}; it reports '{result}' where '{state.ExpectedValue}' was expected. The field may normalize or validate its content.";
                        failed = request;
                        _pendingText = null;
                    }
                    else
                    {
                        request.RetryStrikes++;
                        if (request.RetryStrikes >= 3)
                        {
                            request.Message = $"The text field rejected character '{request.Text[request.TypedCount]}' at position {request.TypedCount}; it reports '{result}'. The field may be validated against a pattern or length limit.";
                            failed = request;
                            _pendingText = null;
                        }
                    }
                    break;
            }
        }

        failed?.Completion.TrySetResult(false);
    }

    private static bool FocusHeld(TextEntryRequest request)
    {
        if (GUIUtility.keyboardControl == 0)
            return false;
        if (request.ControlName != null)
            return string.Equals(GUI.GetNameOfFocusedControl(), request.ControlName, StringComparison.Ordinal);

        return GUIUtility.keyboardControl == request.CapturedKeyboardControl;
    }

    public static void FocusPendingTextField(int controlId)
    {
        if (!_focusNextTextField)
            return;

        _focusNextTextField = false;
        GUIUtility.keyboardControl = controlId;
    }

    private static int NextCharDelayMs(TextEntryRequest request)
    {
        var baseMs = request.MinCharIntervalMs;
        if (request.JitterPercent <= 0)
            return baseMs;

        var spread = baseMs * request.JitterPercent / 100;
        return Math.Max(15, baseMs - spread + request.Jitter.Next(spread * 2 + 1));
    }

    public static object ScrollUiTargetResponse(
        string targetId,
        float deltaY = 0f,
        float deltaX = 0f,
        float? targetY = null,
        float? targetX = null,
        int timeoutMs = 2000)
    {
        timeoutMs = timeoutMs <= 0 ? 2000 : timeoutMs;

        ScrollRequest request;
        try
        {
            request = RimBridgeMainThread.Invoke(() => QueueScroll(targetId, deltaX, deltaY, targetX, targetY), timeoutMs: 5000);
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                command = "scroll_ui_target",
                changed = false,
                message = ex.Message,
                targetId
            };
        }

        if (!request.Completion.Task.Wait(timeoutMs))
        {
            RimBridgeMainThread.Invoke(() => CancelScroll(targetId), timeoutMs: 5000);
            return new
            {
                success = false,
                command = "scroll_ui_target",
                changed = false,
                message = $"Timed out waiting {timeoutMs}ms for scroll target '{targetId}' to be redrawn. Capture a fresh layout and retry.",
                targetId
            };
        }

        var applied = request.Completion.Task.GetAwaiter().GetResult();
        if (!applied)
        {
            return new
            {
                success = false,
                command = "scroll_ui_target",
                changed = false,
                message = request.Message,
                targetId,
                before = DescribeScroll(request.Before),
                after = DescribeScroll(request.After)
            };
        }

        return CreateScrollResponse(request);
    }

    public static object SetHoverTargetResponse(string targetId, string anchor = "center", float offsetX = 0f, float offsetY = 0f, int? durationMs = null, int settleMs = RimWorldHover.DefaultHoverSettleMs)
    {
        try
        {
            var description = RimBridgeMainThread.Invoke(() => SetHoveredElement(targetId, anchor, offsetX, offsetY, durationMs), timeoutMs: 5000);
            RimWorldHover.SettleHoverIfRequested(settleMs);
            return new
            {
                success = true,
                command = "set_hover_target",
                message = $"Hover target '{targetId}' is active.",
                hoverTarget = description,
                settleMs = RimWorldHover.NormalizeHoverSettleMs(settleMs)
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                command = "set_hover_target",
                message = ex.Message
            };
        }
    }

    public static void ClearHoveredElement()
    {
        lock (Sync)
        {
            _hoveredElement = null;
        }
    }

    public static object DescribeHoveredElement()
    {
        lock (Sync)
        {
            if (_hoveredElement == null)
                return null;

            return new
            {
                kind = "ui_element",
                targetId = _hoveredElement.TargetId,
                surfaceTargetId = _hoveredElement.SurfaceTargetId,
                elementIndex = _hoveredElement.ElementIndex,
                elementKind = _hoveredElement.Kind,
                source = _hoveredElement.Source,
                label = string.IsNullOrWhiteSpace(_hoveredElement.Label) ? null : _hoveredElement.Label,
                anchor = _hoveredElement.Anchor,
                offsetX = _hoveredElement.OffsetX,
                offsetY = _hoveredElement.OffsetY
            };
        }
    }

    public static bool TryResolveClipArea(string targetId, out RimWorldTargeting.ScreenTargetClipArea clipArea, out string error)
    {
        clipArea = null;
        error = string.Empty;

        if (!UiLayoutTargetIds.TryParse(targetId, out var target))
            return false;

        if (!TryResolveTarget(target, out var surface, out var element))
        {
            error = $"UI layout target '{targetId}' is no longer available. Capture a fresh layout snapshot.";
            return true;
        }

        var label = element?.Label;
        if (string.IsNullOrWhiteSpace(label))
            label = surface.Label;
        if (string.IsNullOrWhiteSpace(label))
            label = surface.Title;
        if (string.IsNullOrWhiteSpace(label))
            label = surface.Type;

        clipArea = new RimWorldTargeting.ScreenTargetClipArea
        {
            TargetId = targetId,
            TargetKind = target.Kind == UiLayoutTargetKind.Surface ? "ui_surface" : "ui_element",
            Label = label ?? string.Empty,
            Rect = new UiRectSnapshot
            {
                X = element?.ScreenRect?.X ?? surface.ScreenRect.X,
                Y = element?.ScreenRect?.Y ?? surface.ScreenRect.Y,
                Width = element?.ScreenRect?.Width ?? surface.ScreenRect.Width,
                Height = element?.ScreenRect?.Height ?? surface.ScreenRect.Height
            }
        };
        return true;
    }

    public static void AdvanceFrame(int frameCount)
    {
        CaptureRequest completedCapture = null;
        ClickRequest completedClick = null;
        ScrollRequest completedScroll = null;
        TextEntryRequest completedText = null;
        TextEntryRequest failedText = null;

        lock (Sync)
        {
            if (_pendingCapture != null
                && _pendingCapture.HasCapturedSurface
                && _pendingCapture.CapturedFrame >= 0
                && frameCount > _pendingCapture.CapturedFrame)
            {
                completedCapture = _pendingCapture;
                completedCapture.Snapshot.CapturedFrame = completedCapture.CapturedFrame;
                completedCapture.Snapshot.CapturedAtUtc = DateTime.UtcNow;
                RetainCapture(completedCapture.Snapshot);
                _pendingCapture = null;
            }

            if (_pendingClick != null
                && !_pendingClick.Activated
                && _pendingClick.PhaseInjectedFrame >= 0
                && frameCount > _pendingClick.PhaseInjectedFrame)
            {
                if (_pendingClick.Phase == ClickPhase.MouseDown)
                {
                    _pendingClick.Phase = ClickPhase.MouseUp;
                    _pendingClick.PhaseInjectedFrame = -1;
                }
                else
                {
                    completedClick = _pendingClick;
                    completedClick.Message = $"UI target '{_pendingClick.TargetId}' did not react to a synthetic click through the live RimWorld widget path.";
                    _pendingClick = null;
                }
            }

            if (_pendingScroll != null
                && _pendingScroll.Applied
                && _pendingScroll.AppliedFrame >= 0
                && frameCount > _pendingScroll.AppliedFrame)
            {
                completedScroll = _pendingScroll;
                _pendingScroll = null;
            }

            _focusNextTextField = false;
            if (_pendingText != null)
            {
                var textRequest = _pendingText;
                if (textRequest.Phase == TextEntryPhase.Done
                    && textRequest.PhaseInjectedFrame >= 0
                    && frameCount > textRequest.PhaseInjectedFrame)
                {
                    completedText = textRequest;
                    _pendingText = null;
                }
                else if (textRequest.PhaseInjectedFrame >= 0 && frameCount > textRequest.PhaseInjectedFrame)
                {
                    if (textRequest.Phase == TextEntryPhase.FocusDown)
                    {
                        textRequest.Phase = TextEntryPhase.FocusUp;
                        textRequest.PhaseInjectedFrame = -1;
                    }
                    else if (textRequest.Phase == TextEntryPhase.FocusUp)
                    {
                        if (textRequest.CapturedKeyboardControl != 0)
                        {
                            textRequest.Phase = TextEntryPhase.SelectAll;
                        }
                        else
                        {
                            textRequest.RetryStrikes++;
                            textRequest.Phase = TextEntryPhase.FocusDown;
                            if (textRequest.RetryStrikes >= 6)
                            {
                                textRequest.Message = "The text field did not take keyboard focus.";
                                failedText = textRequest;
                                _pendingText = null;
                            }
                        }

                        textRequest.PhaseInjectedFrame = -1;
                    }
                    else
                    {
                        textRequest.PhaseInjectedFrame = -1;
                    }
                }

                if (_pendingText != null && textRequest.LastMatchedFrame >= 0 && frameCount - textRequest.LastMatchedFrame > 240)
                {
                    textRequest.Message = "The target text field stopped drawing before the text entry completed.";
                    failedText = textRequest;
                    _pendingText = null;
                }
            }
        }

        completedCapture?.Completion.TrySetResult(completedCapture.Snapshot);
        completedClick?.Completion.TrySetResult(false);
        completedScroll?.Completion.TrySetResult(true);
        completedText?.Completion.TrySetResult(true);
        failedText?.Completion.TrySetResult(false);
    }

    public static void BeginSurface(Window window)
    {
        BeginSurface(window == null ? null : DescribeSurface(window));
    }

    public static void BeginSelectedGizmosSurface()
    {
        if (!TryCreateSelectedGizmosSurface(out var descriptor))
        {
            BeginSurface((SurfaceDescriptor)null);
            return;
        }

        BeginSurface(descriptor);
    }

    public static void BeginInspectTabsSurface(IInspectPane pane)
    {
        if (pane == null)
        {
            BeginSurface((SurfaceDescriptor)null);
            return;
        }

        if (!TryCreateInspectTabsSurface(pane, out var descriptor))
        {
            BeginSurface((SurfaceDescriptor)null);
            return;
        }

        BeginSurface(descriptor);
    }

    private static bool TryCreateSelectedGizmosSurface(out SurfaceDescriptor descriptor)
    {
        try
        {
            descriptor = new SurfaceDescriptor
            {
                SurfaceTargetId = SelectionGizmosSurfaceId,
                SurfaceKind = "selection_gizmos",
                Type = typeof(GizmoGridDrawer).FullName ?? nameof(GizmoGridDrawer),
                Label = "Selected gizmos",
                SemanticKind = "selection_gizmos",
                SemanticDetails = DescribeSelectedGizmosSurface(),
                Rect = ToSnapshot(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight))
            };
            return true;
        }
        catch
        {
            descriptor = null;
            return false;
        }
    }

    private static bool TryCreateInspectTabsSurface(IInspectPane pane, out SurfaceDescriptor descriptor)
    {
        try
        {
            descriptor = new SurfaceDescriptor
            {
                SurfaceTargetId = ScreenTargetIds.CreateMainTabTargetId(MainButtonDefOf.Inspect.defName),
                SurfaceKind = "main_tab",
                Type = typeof(MainTabWindow_Inspect).FullName ?? nameof(MainTabWindow_Inspect),
                Label = MainButtonDefOf.Inspect.LabelCap.ToString(),
                SemanticKind = "inspect_tabs",
                SemanticDetails = DescribeInspectTabsSurface(pane),
                Rect = ToSnapshot(CreateInspectTabsSurfaceRect(pane))
            };
            return true;
        }
        catch
        {
            descriptor = null;
            return false;
        }
    }

    private static void BeginSurface(SurfaceDescriptor descriptor)
    {
        if (descriptor == null)
        {
            lock (Sync)
            {
                SurfaceStack.Push(null);
            }

            return;
        }

        lock (Sync)
        {
            var capture = _pendingCapture;
            var click = _pendingClick;
            var scroll = _pendingScroll;
            var hover = _hoveredElement;
            var text = _pendingText;
            if (capture == null && click == null && scroll == null && hover == null && text == null)
            {
                SurfaceStack.Push(null);
                return;
            }

            var shouldCapture = capture != null
                && Event.current?.type == EventType.Repaint
                && SurfaceMatches(capture.SurfaceTargetId, descriptor.SurfaceTargetId);
            var shouldTrackForClick = click != null && string.Equals(click.SurfaceTargetId, descriptor.SurfaceTargetId, StringComparison.Ordinal);
            var shouldTrackForScroll = scroll != null && string.Equals(scroll.SurfaceTargetId, descriptor.SurfaceTargetId, StringComparison.Ordinal);
            var shouldTrackForHover = hover != null && string.Equals(hover.SurfaceTargetId, descriptor.SurfaceTargetId, StringComparison.Ordinal);
            var shouldTrackForText = text != null && string.Equals(text.SurfaceTargetId, descriptor.SurfaceTargetId, StringComparison.Ordinal);
            if (!shouldCapture && !shouldTrackForClick && !shouldTrackForScroll && !shouldTrackForHover && !shouldTrackForText)
            {
                SurfaceStack.Push(null);
                return;
            }

            UiLayoutSurfaceSnapshot captureSurface = null;
            if (shouldCapture)
            {
                captureSurface = capture.Snapshot.Surfaces.FirstOrDefault(surface =>
                    string.Equals(surface.SurfaceTargetId, descriptor.SurfaceTargetId, StringComparison.Ordinal));
                if (captureSurface == null)
                {
                    var surfaceIndex = capture.Snapshot.Surfaces.Count + 1;
                    captureSurface = new UiLayoutSurfaceSnapshot
                    {
                        SurfaceIndex = surfaceIndex,
                        CaptureTargetId = UiLayoutTargetIds.CreateSurfaceTargetId(capture.CaptureId, surfaceIndex),
                        SurfaceTargetId = descriptor.SurfaceTargetId,
                        SurfaceKind = descriptor.SurfaceKind,
                        Type = descriptor.Type,
                        Title = descriptor.Title,
                        Label = descriptor.Label,
                        SemanticKind = descriptor.SemanticKind,
                        SemanticDetails = descriptor.SemanticDetails,
                        Rect = descriptor.Rect,
                        ScreenRect = descriptor.ScreenRect ?? descriptor.Rect
                    };
                    capture.Snapshot.Surfaces.Add(captureSurface);
                }

                capture.HasCapturedSurface = true;
                capture.CapturedFrame = Time.frameCount;
            }

            SurfaceStack.Push(new LiveSurfaceState
            {
                SurfaceTargetId = descriptor.SurfaceTargetId,
                TrackingEnabled = true,
                CaptureSurface = captureSurface,
                ElementIndex = captureSurface?.Elements.Count ?? 0
            });
        }
    }

    public static void EndSurface()
    {
        lock (Sync)
        {
            if (SurfaceStack.Count > 0)
                SurfaceStack.Pop();
        }
    }

    public static UiPatchControlState BeginCompoundControl(
        string kind,
        string source,
        Rect rect,
        string label = null,
        string valueText = null,
        bool actionable = true,
        bool? checkedState = null,
        bool? disabled = null)
    {
        lock (Sync)
        {
            var surface = GetCurrentSurface();
            if (surface == null || !surface.TrackingEnabled)
                return new UiPatchControlState();
            if (surface.CompoundDepth > 0)
            {
                return new UiPatchControlState
                {
                    TrackingEnabled = true
                };
            }

            RegisterElement(surface, kind, source, rect, label, valueText, actionable, checkedState, disabled);
            surface.CompoundDepth++;

            return new UiPatchControlState
            {
                TrackingEnabled = true,
                SuppressNested = true,
                ShouldActivate = ShouldActivateCurrentElement(surface, kind, source, rect, label, actionable, disabled),
                ShouldHover = ShouldHoverCurrentElement(surface, kind, source, rect, label)
            };
        }
    }

    public static void EndCompoundControl(UiPatchControlState state)
    {
        if (state == null || !state.TrackingEnabled || !state.SuppressNested)
            return;

        lock (Sync)
        {
            RestorePatchedEvent(state);

            var surface = GetCurrentSurface();
            if (surface == null || surface.CompoundDepth <= 0)
                return;

            surface.CompoundDepth--;
        }
    }

    public static void RegisterPassiveElement(
        string kind,
        string source,
        Rect rect,
        string label = null,
        string valueText = null,
        bool? checkedState = null,
        bool? disabled = null)
    {
        lock (Sync)
        {
            var surface = GetCurrentSurface();
            if (surface == null || !surface.TrackingEnabled)
                return;
            if (surface.CompoundDepth > 0)
                return;

            RegisterElement(surface, kind, source, rect, label, valueText, actionable: false, checkedState, disabled);
        }
    }

    public static void BeginContainer(string kind, string source, Rect rect, string label = null)
    {
        lock (Sync)
        {
            var surface = GetCurrentSurface();
            if (surface == null || !surface.TrackingEnabled)
                return;
            if (surface.CompoundDepth > 0)
                return;

            var captureTargetId = RegisterElement(surface, kind, source, rect, label, null, actionable: false, checkedState: null, disabled: null);
            if (!string.IsNullOrWhiteSpace(captureTargetId))
                surface.ContainerTargetIds.Push(captureTargetId);
        }
    }

    public static void BeginScrollViewContainer(Rect viewportRect, ref Vector2 scrollPosition, Rect contentRect)
    {
        lock (Sync)
        {
            var surface = GetCurrentSurface();
            if (surface == null || !surface.TrackingEnabled)
                return;
            if (surface.CompoundDepth > 0)
                return;

            var scroll = CreateScrollSnapshot(scrollPosition, viewportRect, contentRect);
            var captureTargetId = RegisterElement(
                surface,
                "scroll_view",
                "widgets.begin_scroll_view",
                viewportRect,
                label: null,
                valueText: null,
                actionable: false,
                checkedState: null,
                disabled: null,
                scroll);

            TryApplyPendingScroll(surface, viewportRect, contentRect, scroll, ref scrollPosition);

            if (!string.IsNullOrWhiteSpace(captureTargetId))
                surface.ContainerTargetIds.Push(captureTargetId);
        }
    }

    public static void EndContainer()
    {
        lock (Sync)
        {
            var surface = GetCurrentSurface();
            if (surface == null || surface.ContainerTargetIds.Count == 0)
                return;

            surface.ContainerTargetIds.Pop();
        }
    }

    public static void PrepareControlInteraction(UiPatchControlState state, Rect rect)
    {
        if (state == null || (!state.ShouldActivate && !state.ShouldHover))
            return;

        lock (Sync)
        {
            if (state.EventOverridden)
                return;
            if (state.ShouldActivate && _pendingClick != null && _pendingClick.PhaseInjectedFrame == Time.frameCount)
                return;

            var pointerInverted = UI.GUIToScreenPoint(rect.center);
            var currentEvent = Event.current;
            var injectedEvent = currentEvent == null ? new Event() : new Event(currentEvent);
            injectedEvent.mousePosition = rect.center;

            if (state.ShouldActivate && _pendingClick != null)
            {
                injectedEvent.type = _pendingClick.Phase == ClickPhase.MouseDown ? EventType.MouseDown : EventType.MouseUp;
                injectedEvent.button = 0;
                injectedEvent.clickCount = 1;
                _pendingClick.PhaseInjectedFrame = Time.frameCount;
            }

            state.PreviousEvent = currentEvent;
            state.EventOverridden = true;
            state.TransientPointerToken = RimBridgeVirtualPointer.PushTransientOverride(pointerInverted);
            state.RegisteredActiveInteraction = true;
            state.InteractionRect = ToSnapshot(rect);
            _activeInteraction = new ActiveInteractionContext
            {
                Rect = ToSnapshot(rect),
                ShouldActivate = state.ShouldActivate
            };
            Event.current = injectedEvent;

            if (state.ShouldHover)
            {
                var hover = _hoveredElement;
                if (hover != null)
                    RimBridgeVirtualPointer.UpdatePersistentPointerPosition(RimWorldHover.ResolveRectPoint(ToScreenSnapshot(rect), hover.Anchor, hover.OffsetX, hover.OffsetY));
                else
                    RimBridgeVirtualPointer.UpdatePersistentPointerPosition(pointerInverted);
            }
        }
    }

    public static void ObserveControlResult(UiPatchControlState state, bool activated, string message)
    {
        if (state == null || !state.ShouldActivate || !activated)
            return;

        ClickRequest completedClick = null;
        lock (Sync)
        {
            if (_pendingClick == null || _pendingClick.Activated)
                return;

            _pendingClick.Activated = true;
            _pendingClick.ActivatedFrame = Time.frameCount;
            _pendingClick.Message = message;
            completedClick = _pendingClick;
            _pendingClick = null;
        }

        completedClick?.Completion.TrySetResult(true);
    }

    public static void OverrideButtonResultIfPending(UiPatchControlState state, ref bool result)
    {
        if (result || state == null || !state.ShouldActivate)
            return;

        lock (Sync)
        {
            if (_pendingClick == null || _pendingClick.Activated)
                return;
            if (_activeInteraction == null || !_activeInteraction.ShouldActivate)
                return;
            if (_pendingClick.Phase != ClickPhase.MouseUp || _pendingClick.PhaseInjectedFrame != Time.frameCount)
                return;
            if (!RectApproximatelyEquals(_activeInteraction.Rect, state.InteractionRect, 1f))
                return;

            result = true;
        }
    }

    public static void OverrideDraggableResultIfPending(Rect rect, ref Widgets.DraggableResult result)
    {
        lock (Sync)
        {
            if (_pendingClick == null || _pendingClick.Activated)
                return;
            if (_activeInteraction == null || !_activeInteraction.ShouldActivate)
                return;
            if (_pendingClick.Phase != ClickPhase.MouseUp || _pendingClick.PhaseInjectedFrame != Time.frameCount)
                return;
            if (!RectApproximatelyEquals(_activeInteraction.Rect, rect, 1f))
                return;

            result = Widgets.DraggableResult.Pressed;
        }
    }

    private static string RegisterElement(
        LiveSurfaceState surface,
        string kind,
        string source,
        Rect rect,
        string label,
        string valueText,
        bool actionable,
        bool? checkedState,
        bool? disabled,
        UiLayoutScrollSnapshot scroll = null)
    {
        var normalizedLabel = NormalizeText(label);
        var normalizedValueText = NormalizeText(valueText);
        if (ShouldSuppressGuiFallback(surface, kind, source, rect, normalizedLabel, normalizedValueText, actionable, checkedState, disabled))
            return surface.LastRegisteredElement?.TargetId;

        surface.ElementIndex++;
        var elementIndex = surface.ElementIndex;
        var rectSnapshot = ToSnapshot(rect);
        var screenRectSnapshot = ToScreenSnapshot(rect);
        string targetId = null;

        if (surface.CaptureSurface != null)
        {
            targetId = UiLayoutTargetIds.CreateElementTargetId(
                _pendingCapture?.CaptureId ?? surface.CaptureSurface.SurfaceIndex,
                surface.CaptureSurface.SurfaceIndex,
                elementIndex);
            if (surface.CaptureSurface.Elements.Any(existing =>
                string.Equals(existing.TargetId, targetId, StringComparison.Ordinal)))
            {
                surface.LastRegisteredElement = new RegisteredElementFingerprint
                {
                    ElementIndex = elementIndex,
                    TargetId = targetId,
                    Kind = kind,
                    Source = source,
                    Label = normalizedLabel,
                    ValueText = normalizedValueText,
                    Actionable = actionable,
                    Checked = checkedState,
                    Disabled = disabled,
                    Rect = rectSnapshot
                };
                return targetId;
            }

            surface.CaptureSurface.Elements.Add(new UiLayoutElementSnapshot
            {
                ElementIndex = elementIndex,
                TargetId = targetId,
                Kind = kind,
                Source = source,
                Label = normalizedLabel,
                ValueText = normalizedValueText,
                Actionable = actionable,
                    Checked = checkedState,
                    Disabled = disabled,
                    Depth = surface.ContainerTargetIds.Count,
                    ParentTargetId = surface.ContainerTargetIds.Count == 0 ? null : surface.ContainerTargetIds.Peek(),
                    Rect = rectSnapshot,
                    ScreenRect = screenRectSnapshot,
                    Scroll = scroll
                });
        }

        surface.LastRegisteredElement = new RegisteredElementFingerprint
        {
            ElementIndex = elementIndex,
            TargetId = targetId ?? string.Empty,
            Kind = kind,
            Source = source,
            Label = normalizedLabel,
            ValueText = normalizedValueText,
            Actionable = actionable,
            Checked = checkedState,
            Disabled = disabled,
            Rect = rectSnapshot
        };

        return targetId;
    }

    private static bool ShouldActivateCurrentElement(LiveSurfaceState surface, string kind, string source, Rect rect, string label, bool actionable, bool? disabled)
    {
        if (!actionable || disabled == true || _pendingClick == null || _pendingClick.Activated)
            return false;
        if (!string.Equals(_pendingClick.SurfaceTargetId, surface.SurfaceTargetId, StringComparison.Ordinal))
            return false;
        if (!string.Equals(_pendingClick.Kind, kind, StringComparison.Ordinal))
            return false;
        if (!string.Equals(_pendingClick.Source, source, StringComparison.Ordinal))
            return false;

        var expectedLabel = NormalizeText(_pendingClick.Label);
        var actualLabel = NormalizeText(label);
        if (!string.Equals(expectedLabel, actualLabel, StringComparison.Ordinal))
            return false;

        if (_pendingClick.ElementIndex == surface.ElementIndex)
            return true;

        return _pendingClick.Depth == surface.ContainerTargetIds.Count
            && RectApproximatelyEquals(_pendingClick.Rect, rect, 1f);
    }

    private static bool ShouldHoverCurrentElement(LiveSurfaceState surface, string kind, string source, Rect rect, string label)
    {
        if (_hoveredElement == null)
            return false;
        if (!string.Equals(_hoveredElement.SurfaceTargetId, surface.SurfaceTargetId, StringComparison.Ordinal))
            return false;
        if (!string.Equals(_hoveredElement.Kind, kind, StringComparison.Ordinal))
            return false;
        if (!string.Equals(_hoveredElement.Source, source, StringComparison.Ordinal))
            return false;

        var expectedLabel = NormalizeText(_hoveredElement.Label);
        var actualLabel = NormalizeText(label);
        if (!string.Equals(expectedLabel, actualLabel, StringComparison.Ordinal))
            return false;

        if (_hoveredElement.ElementIndex == surface.ElementIndex)
            return true;

        return _hoveredElement.Depth == surface.ContainerTargetIds.Count
            && RectApproximatelyEquals(_hoveredElement.Rect, rect, 1f);
    }

    private static bool ShouldScrollCurrentElement(LiveSurfaceState surface, Rect rect)
    {
        if (_pendingScroll == null || _pendingScroll.Applied)
            return false;
        if (!string.Equals(_pendingScroll.SurfaceTargetId, surface.SurfaceTargetId, StringComparison.Ordinal))
            return false;
        if (!string.Equals(_pendingScroll.Kind, "scroll_view", StringComparison.Ordinal))
            return false;
        if (!string.Equals(_pendingScroll.Source, "widgets.begin_scroll_view", StringComparison.Ordinal))
            return false;

        if (_pendingScroll.ElementIndex == surface.ElementIndex)
            return true;

        return _pendingScroll.Depth == surface.ContainerTargetIds.Count
            && RectApproximatelyEquals(_pendingScroll.Rect, rect, 1f);
    }

    private static void TryApplyPendingScroll(
        LiveSurfaceState surface,
        Rect viewportRect,
        Rect contentRect,
        UiLayoutScrollSnapshot before,
        ref Vector2 scrollPosition)
    {
        if (!ShouldScrollCurrentElement(surface, viewportRect))
            return;

        var maxOffsetX = Math.Max(0f, contentRect.width - viewportRect.width);
        var maxOffsetY = Math.Max(0f, contentRect.height - viewportRect.height);
        var requestedX = _pendingScroll.TargetX ?? (before.OffsetX + _pendingScroll.DeltaX);
        var requestedY = _pendingScroll.TargetY ?? (before.OffsetY + _pendingScroll.DeltaY);
        var next = new Vector2(
            Mathf.Clamp(requestedX, 0f, maxOffsetX),
            Mathf.Clamp(requestedY, 0f, maxOffsetY));

        scrollPosition = next;
        _pendingScroll.Before = before;
        _pendingScroll.After = CreateScrollSnapshot(next, viewportRect, contentRect);
        _pendingScroll.Applied = true;
        _pendingScroll.AppliedFrame = Time.frameCount;
        _pendingScroll.Message = BuildScrollMessage(_pendingScroll);
    }

    private static CaptureRequest QueueCapture(string surfaceId)
    {
        lock (Sync)
        {
            if (_pendingCapture != null)
                throw new InvalidOperationException("A UI layout capture is already pending.");
            if (_pendingClick != null)
                throw new InvalidOperationException("Cannot start a UI layout capture while a UI target click is pending.");
            if (_pendingScroll != null)
                throw new InvalidOperationException("Cannot start a UI layout capture while a UI target scroll is pending.");
            if (_pendingText != null)
                throw new InvalidOperationException("Cannot start a UI layout capture while a text entry is pending.");

            var captureId = _nextCaptureId++;
            var request = new CaptureRequest
            {
                CaptureId = captureId,
                SurfaceTargetId = surfaceId?.Trim() ?? string.Empty,
                Snapshot = new UiLayoutSnapshot
                {
                    CaptureId = captureId
                }
            };
            _pendingCapture = request;
            return request;
        }
    }

    private static void CancelCapture(int captureId)
    {
        lock (Sync)
        {
            if (_pendingCapture == null || _pendingCapture.CaptureId != captureId)
                return;

            var request = _pendingCapture;
            _pendingCapture = null;
            request.Completion.TrySetCanceled();
        }
    }

    private static ClickRequest QueueClick(string targetId)
    {
        lock (Sync)
        {
            if (_pendingClick != null)
                throw new InvalidOperationException("A UI target click is already pending.");
            if (_pendingScroll != null)
                throw new InvalidOperationException("Cannot click a UI target while a UI target scroll is pending.");
            if (_pendingText != null)
                throw new InvalidOperationException("Cannot click a UI target while a text entry is pending.");

            if (!UiLayoutTargetIds.TryParse(targetId, out var target) || target.Kind != UiLayoutTargetKind.Element)
                throw new InvalidOperationException($"Target id '{targetId}' is not a UI element target id returned by rimworld/get_ui_layout.");

            if (!TryResolveTarget(target, out var surface, out var element))
                throw new InvalidOperationException($"UI layout target '{targetId}' is no longer available. Capture a fresh layout snapshot.");
            if (element == null || !element.Actionable)
                throw new InvalidOperationException($"UI layout target '{targetId}' is not actionable.");
            if (element.Disabled == true)
                throw new InvalidOperationException($"UI layout target '{targetId}' is currently disabled and cannot be clicked.");

            var request = new ClickRequest
            {
                TargetId = targetId,
                SurfaceTargetId = surface.SurfaceTargetId,
                ElementIndex = element.ElementIndex,
                Kind = element.Kind,
                Source = element.Source,
                Label = element.Label ?? string.Empty,
                Rect = element.Rect,
                Depth = element.Depth,
                Message = BuildClickMessage(surface, element)
            };
            _pendingClick = request;
            return request;
        }
    }

    private static ScrollRequest QueueScroll(string targetId, float deltaX, float deltaY, float? targetX, float? targetY)
    {
        lock (Sync)
        {
            if (_pendingScroll != null)
                throw new InvalidOperationException("A UI target scroll is already pending.");
            if (_pendingClick != null)
                throw new InvalidOperationException("Cannot scroll a UI target while a UI target click is pending.");
            if (_pendingText != null)
                throw new InvalidOperationException("Cannot scroll a UI target while a text entry is pending.");

            if (!UiLayoutTargetIds.TryParse(targetId, out var target) || target.Kind != UiLayoutTargetKind.Element)
                throw new InvalidOperationException($"Target id '{targetId}' is not a UI element target id returned by rimworld/get_ui_layout.");

            if (!TryResolveTarget(target, out var surface, out var element))
                throw new InvalidOperationException($"UI layout target '{targetId}' is no longer available. Capture a fresh layout snapshot.");
            if (element == null || element.Scroll == null || !string.Equals(element.Kind, "scroll_view", StringComparison.Ordinal))
                throw new InvalidOperationException($"UI layout target '{targetId}' is not a scroll view target.");
            if (Math.Abs(deltaX) < 0.001f && Math.Abs(deltaY) < 0.001f && !targetX.HasValue && !targetY.HasValue)
                throw new InvalidOperationException("Provide a non-zero deltaX/deltaY or a targetX/targetY offset.");

            var request = new ScrollRequest
            {
                TargetId = targetId,
                SurfaceTargetId = surface.SurfaceTargetId,
                ElementIndex = element.ElementIndex,
                Kind = element.Kind,
                Source = element.Source,
                Label = element.Label ?? string.Empty,
                Rect = element.Rect,
                Depth = element.Depth,
                DeltaX = deltaX,
                DeltaY = deltaY,
                TargetX = targetX,
                TargetY = targetY,
                Before = element.Scroll
            };
            _pendingScroll = request;
            return request;
        }
    }

    private static object SetHoveredElement(string targetId, string anchor, float offsetX, float offsetY, int? durationMs)
    {
        lock (Sync)
        {
            if (!UiLayoutTargetIds.TryParse(targetId, out var target) || target.Kind != UiLayoutTargetKind.Element)
                throw new InvalidOperationException($"Target id '{targetId}' is not a UI element target id returned by rimworld/get_ui_layout.");

            if (!TryResolveTarget(target, out var surface, out var element))
                throw new InvalidOperationException($"UI layout target '{targetId}' is no longer available. Capture a fresh layout snapshot.");
            if (element == null)
                throw new InvalidOperationException($"UI layout target '{targetId}' is no longer available. Capture a fresh layout snapshot.");

            _hoveredElement = new HoverRequest
            {
                TargetId = targetId,
                SurfaceTargetId = surface.SurfaceTargetId,
                ElementIndex = element.ElementIndex,
                Kind = element.Kind,
                Source = element.Source,
                Label = element.Label ?? string.Empty,
                Rect = element.Rect,
                Depth = element.Depth,
                Anchor = string.IsNullOrWhiteSpace(anchor) ? "center" : anchor,
                OffsetX = offsetX,
                OffsetY = offsetY
            };

            var pointerRect = element.ScreenRect ?? element.Rect;
            var pointer = RimWorldHover.ResolveRectPoint(pointerRect, anchor, offsetX, offsetY);
            RimBridgeVirtualPointer.SetPersistentPointer(
                kind: "ui_element",
                targetId,
                label: string.IsNullOrWhiteSpace(element.Label) ? element.Kind : element.Label,
                screenPositionInverted: pointer,
                details: new
                {
                    surfaceTargetId = surface.SurfaceTargetId,
                    elementKind = element.Kind,
                    source = element.Source,
                    actionable = element.Actionable,
                    anchor = string.IsNullOrWhiteSpace(anchor) ? "center" : anchor,
                    offsetX,
                    offsetY,
                    rect = new
                    {
                        x = pointerRect.X,
                        y = pointerRect.Y,
                        width = pointerRect.Width,
                        height = pointerRect.Height
                    }
                },
                durationMs: durationMs);

            return DescribeHoveredElement();
        }
    }

    private static void CancelClick(string targetId)
    {
        lock (Sync)
        {
            if (_pendingClick == null || !string.Equals(_pendingClick.TargetId, targetId, StringComparison.Ordinal))
                return;

            var request = _pendingClick;
            _pendingClick = null;
            request.Completion.TrySetCanceled();
        }
    }

    private static void CancelScroll(string targetId)
    {
        lock (Sync)
        {
            if (_pendingScroll == null || !string.Equals(_pendingScroll.TargetId, targetId, StringComparison.Ordinal))
                return;

            var request = _pendingScroll;
            _pendingScroll = null;
            request.Completion.TrySetCanceled();
        }
    }

    private static bool TryResolveTarget(
        UiLayoutTargetReference target,
        out UiLayoutSurfaceSnapshot surface,
        out UiLayoutElementSnapshot element)
    {
        surface = null;
        element = null;

        if (!RecentCaptures.TryGetValue(target.CaptureId, out var snapshot))
            return false;
        if (target.SurfaceIndex <= 0 || target.SurfaceIndex > snapshot.Surfaces.Count)
            return false;

        surface = snapshot.Surfaces[target.SurfaceIndex - 1];
        if (target.Kind == UiLayoutTargetKind.Surface)
            return true;

        element = surface.Elements.FirstOrDefault(existing =>
            string.Equals(existing.TargetId, target.TargetId, StringComparison.Ordinal));
        if (element != null)
            return true;

        if (target.ElementIndex <= 0 || target.ElementIndex > surface.Elements.Count)
            return false;

        element = surface.Elements[target.ElementIndex - 1];
        return true;
    }

    private static void RetainCapture(UiLayoutSnapshot snapshot)
    {
        RecentCaptures[snapshot.CaptureId] = snapshot;
        CaptureOrder.Enqueue(snapshot.CaptureId);
        while (CaptureOrder.Count > MaxRetainedCaptures)
        {
            var removedId = CaptureOrder.Dequeue();
            RecentCaptures.Remove(removedId);
        }
    }

    private static LiveSurfaceState GetCurrentSurface()
    {
        return SurfaceStack.Count == 0 ? null : SurfaceStack.Peek();
    }

    private static void RestorePatchedEvent(UiPatchControlState state)
    {
        if (state == null || !state.EventOverridden)
            return;

        if (state.TransientPointerToken != 0)
            RimBridgeVirtualPointer.PopTransientOverride(state.TransientPointerToken);

        if (state.RegisteredActiveInteraction)
            _activeInteraction = null;

        Event.current = state.PreviousEvent;
        state.EventOverridden = false;
        state.TransientPointerToken = 0;
        state.PreviousEvent = null;
        state.RegisteredActiveInteraction = false;
    }

    private static bool SurfaceMatches(string requestedSurfaceId, string actualSurfaceId)
    {
        return string.IsNullOrWhiteSpace(requestedSurfaceId)
            || string.Equals(requestedSurfaceId, actualSurfaceId, StringComparison.Ordinal);
    }

    private static bool ShouldSuppressGuiFallback(
        LiveSurfaceState surface,
        string kind,
        string source,
        Rect rect,
        string label,
        string valueText,
        bool actionable,
        bool? checkedState,
        bool? disabled)
    {
        if (surface.LastRegisteredElement == null)
            return false;
        if (!source.StartsWith("gui.", StringComparison.Ordinal))
            return false;

        var previous = surface.LastRegisteredElement;
        if (previous.Source.StartsWith("gui.", StringComparison.Ordinal))
            return false;

        return string.Equals(previous.Kind, kind, StringComparison.Ordinal)
            && string.Equals(previous.Label, label, StringComparison.Ordinal)
            && string.Equals(previous.ValueText, valueText, StringComparison.Ordinal)
            && previous.Actionable == actionable
            && previous.Checked == checkedState
            && previous.Disabled == disabled
            && RectApproximatelyEquals(previous.Rect, rect, 0.5f);
    }

    private static bool RectApproximatelyEquals(UiRectSnapshot snapshot, Rect rect, float tolerance)
    {
        if (snapshot == null)
            return false;

        return Math.Abs(snapshot.X - rect.x) <= tolerance
            && Math.Abs(snapshot.Y - rect.y) <= tolerance
            && Math.Abs(snapshot.Width - rect.width) <= tolerance
            && Math.Abs(snapshot.Height - rect.height) <= tolerance;
    }

    private static bool RectApproximatelyEquals(UiRectSnapshot first, UiRectSnapshot second, float tolerance)
    {
        if (first == null || second == null)
            return false;

        return Math.Abs(first.X - second.X) <= tolerance
            && Math.Abs(first.Y - second.Y) <= tolerance
            && Math.Abs(first.Width - second.Width) <= tolerance
            && Math.Abs(first.Height - second.Height) <= tolerance;
    }

    private static SurfaceDescriptor DescribeSurface(Window window)
    {
        var type = window.GetType().FullName ?? window.GetType().Name;
        string semanticKind = string.Empty;
        object semanticDetails = null;
        RimWorldModSettings.TryDescribeWindow(window, out semanticKind, out semanticDetails);

        if (window is MainTabWindow mainTabWindow && mainTabWindow.def != null && !string.IsNullOrWhiteSpace(mainTabWindow.def.defName))
        {
            return new SurfaceDescriptor
            {
                SurfaceTargetId = ScreenTargetIds.CreateMainTabTargetId(mainTabWindow.def.defName),
                SurfaceKind = "main_tab",
                Type = type,
                Label = string.IsNullOrWhiteSpace(mainTabWindow.def.label) ? mainTabWindow.def.defName : mainTabWindow.def.LabelCap.ToString(),
                SemanticKind = semanticKind,
                SemanticDetails = semanticDetails,
                Rect = ToSnapshot(window.windowRect)
            };
        }

        return new SurfaceDescriptor
        {
            SurfaceTargetId = ScreenTargetIds.CreateWindowTargetId(window.ID, type),
            SurfaceKind = "window",
            Type = type,
            Title = string.IsNullOrWhiteSpace(window.optionalTitle) ? null : window.optionalTitle,
            SemanticKind = semanticKind,
            SemanticDetails = semanticDetails,
            Rect = ToSnapshot(window.windowRect)
        };
    }

    private static object DescribeSelectedGizmosSurface()
    {
        var selectedCount = SafeMapSelectedObjectCount();
        return new
        {
            selectedCount
        };
    }

    private static object DescribeInspectTabsSurface(IInspectPane pane)
    {
        var openTabType = pane is MainTabWindow_Inspect inspectPane ? inspectPane.OpenTabType : null;
        var tabs = pane.CurTabs?
            .Where(tab => tab != null && SafeBool(() => tab.IsVisible) && !SafeBool(() => tab.Hidden))
            .Select((tab, index) => new
            {
                ordinal = index + 1,
                label = TranslateTabLabel(tab),
                labelKey = string.IsNullOrWhiteSpace(tab.labelKey) ? null : tab.labelKey,
                tutorTag = string.IsNullOrWhiteSpace(tab.tutorTag) ? null : tab.tutorTag,
                type = tab.GetType().FullName ?? tab.GetType().Name,
                isOpen = openTabType != null && tab.GetType() == openTabType
            })
            .ToList()
            ?? [];

        return new
        {
            selectedCount = SafeMapSelectedObjectCount(),
            tabCount = tabs.Count,
            tabs
        };
    }

    private static int SafeMapSelectedObjectCount()
    {
        return SafeMapSelector()?.SelectedObjectsListForReading?.Count ?? 0;
    }

    private static Selector SafeMapSelector()
    {
        return (Find.UIRoot as UIRoot_Play)?.mapUI?.selector;
    }

    private static Rect CreateInspectTabsSurfaceRect(IInspectPane pane)
    {
        var paneWidth = (float)UI.screenWidth;
        var paneTopY = (float)UI.screenHeight;

        try
        {
            paneWidth = InspectPaneUtility.PaneWidthFor(pane);
        }
        catch
        {
            // Some modded inspect panes can throw while selection is changing.
        }

        try
        {
            paneTopY = pane.PaneTopY;
        }
        catch
        {
            // Keep a conservative full-width fallback if the pane is in flux.
        }

        var tabsTopY = Mathf.Max(0f, paneTopY - 30f);
        var bottomY = (float)UI.screenHeight;
        var inspectWindow = MainButtonDefOf.Inspect?.TabWindow as MainTabWindow_Inspect;
        if (inspectWindow != null)
            bottomY = Mathf.Max(bottomY, inspectWindow.windowRect.yMax);

        return new Rect(0f, tabsTopY, Mathf.Max(1f, paneWidth), Mathf.Max(30f, bottomY - tabsTopY));
    }

    private static string TranslateTabLabel(InspectTabBase tab)
    {
        if (tab == null || string.IsNullOrWhiteSpace(tab.labelKey))
            return null;

        try
        {
            return tab.labelKey.Translate().ToString();
        }
        catch
        {
            return tab.labelKey;
        }
    }

    private static bool SafeBool(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return false;
        }
    }

    private static UiRectSnapshot ToSnapshot(Rect rect)
    {
        return new UiRectSnapshot
        {
            X = rect.x,
            Y = rect.y,
            Width = rect.width,
            Height = rect.height
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

    private static UiLayoutScrollSnapshot CreateScrollSnapshot(Vector2 scrollPosition, Rect viewportRect, Rect contentRect)
    {
        var maxOffsetX = Math.Max(0f, contentRect.width - viewportRect.width);
        var maxOffsetY = Math.Max(0f, contentRect.height - viewportRect.height);
        var offsetX = Mathf.Clamp(scrollPosition.x, 0f, maxOffsetX);
        var offsetY = Mathf.Clamp(scrollPosition.y, 0f, maxOffsetY);

        return new UiLayoutScrollSnapshot
        {
            OffsetX = offsetX,
            OffsetY = offsetY,
            MaxOffsetX = maxOffsetX,
            MaxOffsetY = maxOffsetY,
            CanScrollX = maxOffsetX > 0.5f,
            CanScrollY = maxOffsetY > 0.5f,
            AtLeft = offsetX <= 0.5f,
            AtRight = offsetX >= maxOffsetX - 0.5f,
            AtTop = offsetY <= 0.5f,
            AtBottom = offsetY >= maxOffsetY - 0.5f,
            ViewportRect = ToSnapshot(viewportRect),
            ViewportScreenRect = ToScreenSnapshot(viewportRect),
            ContentRect = ToSnapshot(contentRect)
        };
    }

    private static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static string BuildTimeoutMessage(string surfaceId, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(surfaceId))
            return $"Timed out waiting {timeoutMs}ms for a UI surface to draw. Open a dialog, main tab, or selected gizmo grid and retry.";

        return $"Timed out waiting {timeoutMs}ms for UI surface '{surfaceId}' to draw. Verify that the surface is open and retry.";
    }

    private static string BuildClickMessage(UiLayoutSurfaceSnapshot surface, UiLayoutElementSnapshot element)
    {
        var label = string.IsNullOrWhiteSpace(element.Label) ? element.Kind : element.Label;
        return $"Activated UI target '{label}' on surface '{surface.SurfaceTargetId}'.";
    }

    private static string BuildScrollMessage(ScrollRequest request)
    {
        var before = request.Before;
        var after = request.After;
        if (before == null || after == null)
            return $"Scrolled UI target '{request.TargetId}'.";

        return $"Scrolled UI target '{request.TargetId}' from ({before.OffsetX:0.##}, {before.OffsetY:0.##}) to ({after.OffsetX:0.##}, {after.OffsetY:0.##}).";
    }

    private static object DescribeScroll(UiLayoutScrollSnapshot scroll)
    {
        if (scroll == null)
            return null;

        return new
        {
            offsetX = scroll.OffsetX,
            offsetY = scroll.OffsetY,
            maxOffsetX = scroll.MaxOffsetX,
            maxOffsetY = scroll.MaxOffsetY,
            canScrollX = scroll.CanScrollX,
            canScrollY = scroll.CanScrollY,
            atLeft = scroll.AtLeft,
            atRight = scroll.AtRight,
            atTop = scroll.AtTop,
            atBottom = scroll.AtBottom,
            viewportRect = new
            {
                x = scroll.ViewportRect.X,
                y = scroll.ViewportRect.Y,
                width = scroll.ViewportRect.Width,
                height = scroll.ViewportRect.Height
            },
            viewportScreenRect = new
            {
                x = scroll.ViewportScreenRect.X,
                y = scroll.ViewportScreenRect.Y,
                width = scroll.ViewportScreenRect.Width,
                height = scroll.ViewportScreenRect.Height
            },
            contentRect = new
            {
                x = scroll.ContentRect.X,
                y = scroll.ContentRect.Y,
                width = scroll.ContentRect.Width,
                height = scroll.ContentRect.Height
            }
        };
    }

    private static object DescribeLayout(UiLayoutSnapshot snapshot, bool includeOffscreen = false, int offset = 0)
    {
        var entries = new List<(UiLayoutSurfaceSnapshot Surface, UiLayoutElementSnapshot Element)>();
        var surfaceStats = new Dictionary<UiLayoutSurfaceSnapshot, (int Offscreen, int Filler)>();
        foreach (var surface in snapshot.Surfaces)
        {
            var offscreen = 0;
            var filler = 0;
            var scrollByContentRoot = FindScrollContentRoots(surface.Elements);
            var byId = surface.Elements
                .Where(element => string.IsNullOrEmpty(element.TargetId) == false)
                .GroupBy(element => element.TargetId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (var element in surface.Elements)
            {
                var scrollView = FindEnclosingScrollView(element, byId, scrollByContentRoot);
                var outside = scrollView != null && !Intersects(element.ScreenRect, scrollView.Scroll.ViewportScreenRect);

                if (IsLayoutFiller(element))
                {
                    filler++;
                    continue;
                }

                if (!includeOffscreen && outside)
                {
                    offscreen++;
                    continue;
                }

                entries.Add((surface, element));
            }

            surfaceStats[surface] = (offscreen, filler);
        }

        var described = ResponseBudget.TakePage(entries, offset, entry => DescribeLayoutElement(entry.Element), out var page);
        var pageSurfaces = entries.Skip(Math.Max(0, offset)).Take(described.Count).Select(entry => entry.Surface).ToList();

        return new
        {
            success = true,
            captureId = snapshot.CaptureId,
            capturedFrame = snapshot.CapturedFrame,
            surfaceCount = snapshot.Surfaces.Count,
            page,
            surfaces = snapshot.Surfaces.Select(surface =>
            {
                var elements = described.Where((_, index) => ReferenceEquals(pageSurfaces[index], surface)).ToList();
                var stats = surfaceStats[surface];
                return new
                {
                    captureTargetId = surface.CaptureTargetId,
                    surfaceTargetId = surface.SurfaceTargetId,
                    surfaceKind = surface.SurfaceKind,
                    type = surface.Type,
                    title = surface.Title,
                    label = surface.Label,
                    semanticKind = string.IsNullOrWhiteSpace(surface.SemanticKind) ? null : surface.SemanticKind,
                    semanticDetails = surface.SemanticDetails,
                    screenRect = DescribeRect(surface.ScreenRect),
                    elementCount = surface.Elements.Count,
                    actionableElementCount = surface.Elements.Count(element => element.Actionable),
                    offscreenElementCount = stats.Offscreen,
                    omittedLayoutFillerCount = stats.Filler,
                    elements
                };
            }).ToList(),
            offscreenHint = includeOffscreen || surfaceStats.Values.All(stats => stats.Offscreen == 0)
                ? null
                : "Elements scrolled out of view are omitted; scroll with rimworld/scroll_ui_target or pass includeOffscreen=true."
        };
    }

    // Spacers and empty layout slots carry geometry only: no label, value, or action.
    private static bool IsLayoutFiller(UiLayoutElementSnapshot element)
    {
        return element.Actionable == false
            && element.Scroll == null
            && string.IsNullOrEmpty(element.Label)
            && string.IsNullOrEmpty(element.ValueText)
            && (element.Kind == "spacing" || element.Kind == "slot");
    }

    // Listings draw their scroll content as the sibling directly after the scroll view; that
    // sibling (same depth and parent) is the content root for everything parented under it.
    private static Dictionary<string, UiLayoutElementSnapshot> FindScrollContentRoots(List<UiLayoutElementSnapshot> elements)
    {
        var roots = new Dictionary<string, UiLayoutElementSnapshot>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < elements.Count; index++)
        {
            var scrollView = elements[index];
            var next = elements[index + 1];
            if (scrollView.Scroll != null
                && next.Depth == scrollView.Depth
                && string.Equals(next.ParentTargetId, scrollView.ParentTargetId, StringComparison.Ordinal)
                && string.IsNullOrEmpty(next.TargetId) == false)
            {
                roots[next.TargetId] = scrollView;
            }
        }

        return roots;
    }

    private static UiLayoutElementSnapshot FindEnclosingScrollView(
        UiLayoutElementSnapshot element,
        Dictionary<string, UiLayoutElementSnapshot> byId,
        Dictionary<string, UiLayoutElementSnapshot> scrollByContentRoot)
    {
        var parentId = element.ParentTargetId;
        for (var guard = 0; guard < 64 && string.IsNullOrEmpty(parentId) == false; guard++)
        {
            if (scrollByContentRoot.TryGetValue(parentId, out var scrollView))
                return scrollView;
            if (!byId.TryGetValue(parentId, out var parent))
                return null;
            if (parent.Scroll != null)
                return parent;
            parentId = parent.ParentTargetId;
        }

        return null;
    }

    private static bool Intersects(UiRectSnapshot a, UiRectSnapshot b)
    {
        return a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    }

    private static object DescribeRect(UiRectSnapshot rect)
    {
        return new
        {
            x = Math.Round(rect.X),
            y = Math.Round(rect.Y),
            width = Math.Round(rect.Width),
            height = Math.Round(rect.Height)
        };
    }

    private static object DescribeLayoutElement(UiLayoutElementSnapshot element)
    {
        return new
        {
            targetId = element.TargetId,
            kind = element.Kind,
            source = element.Source,
            label = element.Label,
            valueText = element.ValueText,
            actionable = element.Actionable,
            clipCapable = element.ClipCapable ? (bool?)null : false,
            isChecked = element.Checked,
            disabled = element.Disabled,
            depth = element.Depth,
            parentTargetId = element.ParentTargetId,
            screenRect = DescribeRect(element.ScreenRect),
            scroll = DescribeScroll(element.Scroll)
        };
    }

    private static object CreateScrollResponse(ScrollRequest request)
    {
        var before = request.Before;
        var after = request.After;
        var changed = before == null
            || after == null
            || Math.Abs(before.OffsetX - after.OffsetX) > 0.001f
            || Math.Abs(before.OffsetY - after.OffsetY) > 0.001f;

        return new
        {
            success = true,
            command = "scroll_ui_target",
            changed,
            message = changed ? request.Message : request.Message + " Scroll offset did not change.",
            targetId = request.TargetId,
            before = DescribeScroll(before),
            after = DescribeScroll(after)
        };
    }

    private static object CreateClickResponse(string targetId, UiStateSnapshot before, UiStateSnapshot after, string message, ContextMenuSnapshot openedMenu = null)
    {
        var beforeIds = new HashSet<string>(before.Windows.Select(window => window.Type + "#" + window.Id), StringComparer.Ordinal);
        var afterIds = new HashSet<string>(after.Windows.Select(window => window.Type + "#" + window.Id), StringComparer.Ordinal);

        var openedWindowTypes = after.Windows
            .Where(window => !beforeIds.Contains(window.Type + "#" + window.Id))
            .Select(window => window.Type)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var closedWindowTypes = before.Windows
            .Where(window => !afterIds.Contains(window.Type + "#" + window.Id))
            .Select(window => window.Type)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var changed = before.WindowCount != after.WindowCount
            || before.MainTabOpen != after.MainTabOpen
            || before.OpenMainTabId != after.OpenMainTabId
            || before.FocusedWindowType != after.FocusedWindowType
            || openedWindowTypes.Count > 0
            || closedWindowTypes.Count > 0;

        return new
        {
            success = true,
            command = "click_ui_target",
            changed,
            message = changed ? message : message + " UI state did not change.",
            targetId,
            after = new
            {
                windowCount = after.WindowCount,
                topWindowType = after.TopWindowType,
                focusedWindowType = after.FocusedWindowType,
                floatMenuOpen = after.FloatMenuOpen,
                openMainTabId = after.OpenMainTabId,
                activeDebugTool = after.ActiveDebugTool
            },
            openedWindowTypes,
            closedWindowTypes,
            openedMenu = RimBridgeContextMenus.DescribeMenu(openedMenu)
        };
    }
}

[HarmonyPatch(typeof(Window), nameof(Window.InnerWindowOnGUI))]
internal static class Window_InnerWindowOnGUI_UiWorkbench_Patch
{
    public static void Prefix(Window __instance)
    {
        RimBridgeUiWorkbench.BeginSurface(__instance);
    }

    public static void Postfix()
    {
        RimBridgeUiWorkbench.EndSurface();
    }
}

[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.DoTabs), new[] { typeof(IInspectPane) })]
internal static class InspectPaneUtility_DoTabs_UiWorkbench_Patch
{
    public static void Prefix(IInspectPane pane)
    {
        RimBridgeUiWorkbench.BeginInspectTabsSurface(pane);
    }

    public static void Postfix()
    {
        RimBridgeUiWorkbench.EndSurface();
    }
}

[HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid))]
internal static class GizmoGridDrawer_DrawGizmoGrid_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix()
    {
        RimBridgeUiWorkbench.BeginSelectedGizmosSurface();
    }

    [HarmonyPriority(Priority.Last)]
    public static void Postfix()
    {
        RimBridgeUiWorkbench.EndSurface();
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.BeginGroup), new[] { typeof(Rect) })]
internal static class Widgets_BeginGroup_UiWorkbench_Patch
{
    public static void Prefix(Rect rect)
    {
        RimBridgeUiWorkbench.BeginContainer("group", "widgets.begin_group", rect);
    }

    public static void Postfix()
    {
        RimBridgeUiWorkbench.EndContainer();
    }
}

[HarmonyPatch]
internal static class Widgets_BeginScrollView_UiWorkbench_Patch
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Widgets), nameof(Widgets.BeginScrollView), [typeof(Rect), typeof(Vector2).MakeByRefType(), typeof(Rect), typeof(bool)]);
    }

    public static void Prefix(Rect outRect, ref Vector2 scrollPosition, Rect viewRect)
    {
        RimBridgeUiWorkbench.BeginScrollViewContainer(outRect, ref scrollPosition, viewRect);
    }

    public static void Postfix()
    {
        RimBridgeUiWorkbench.EndContainer();
    }
}

[HarmonyPatch(typeof(Listing), nameof(Listing.Begin), new[] { typeof(Rect) })]
internal static class Listing_Begin_UiWorkbench_Patch
{
    public static void Prefix(Rect rect)
    {
        RimBridgeUiWorkbench.BeginContainer("listing", "listing.begin", rect);
    }
}

[HarmonyPatch(typeof(Listing), nameof(Listing.End))]
internal static class Listing_End_UiWorkbench_Patch
{
    public static void Postfix()
    {
        RimBridgeUiWorkbench.EndContainer();
    }
}

[HarmonyPatch(typeof(Listing), nameof(Listing.GetRect), new[] { typeof(float), typeof(float) })]
internal static class Listing_GetRect_UiWorkbench_Patch
{
    public static void Postfix(Rect __result)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("slot", "listing.get_rect", __result);
    }
}

[HarmonyPatch(typeof(Listing), nameof(Listing.Gap), new[] { typeof(float) })]
internal static class Listing_Gap_UiWorkbench_Patch
{
    public static void Prefix(Listing __instance, out float __state)
    {
        __state = __instance.curY;
    }

    public static void Postfix(Listing __instance, float __state)
    {
        var height = Math.Max(__instance.curY - __state, 0f);
        if (height <= 0f)
            return;

        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __state, __instance.ColumnWidth, height);
        RimBridgeUiWorkbench.RegisterPassiveElement("spacing", "listing.gap", rect, valueText: height.ToString("0.##"));
    }
}

[HarmonyPatch(typeof(Listing), nameof(Listing.GapLine), new[] { typeof(float) })]
internal static class Listing_GapLine_UiWorkbench_Patch
{
    public static void Prefix(Listing __instance, out float __state)
    {
        __state = __instance.curY;
    }

    public static void Postfix(Listing __instance, float __state)
    {
        var height = Math.Max(__instance.curY - __state, 0f);
        if (height <= 0f)
            return;

        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __state, __instance.ColumnWidth, height);
        RimBridgeUiWorkbench.RegisterPassiveElement("gap_line", "listing.gap_line", rect, valueText: height.ToString("0.##"));
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(string) })]
internal static class Widgets_Label_String_UiWorkbench_Patch
{
    public static void Prefix(Rect rect, string label)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("label", "widgets.label", rect, label);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(TaggedString) })]
internal static class Widgets_Label_TaggedString_UiWorkbench_Patch
{
    public static void Prefix(Rect rect, TaggedString label)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("label", "widgets.label", rect, label.ToString());
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField), new[] { typeof(Rect), typeof(string) })]
internal static class Widgets_TextField_UiWorkbench_Patch
{
    public static void Prefix(Rect rect, ref string text, ref RimBridgeUiWorkbench.TextFieldPatchState __state)
    {
        __state = RimBridgeUiWorkbench.BeginTextField("widgets.text_field", rect, null, ref text);
    }

    public static void Postfix(ref string __result, RimBridgeUiWorkbench.TextFieldPatchState __state)
    {
        RimBridgeUiWorkbench.EndTextField(__state, ref __result);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField), new[] { typeof(Rect), typeof(string), typeof(int), typeof(Regex) })]
internal static class Widgets_TextField_Validated_UiWorkbench_Patch
{
    public static void Prefix(Rect rect, ref string text, ref RimBridgeUiWorkbench.TextFieldPatchState __state)
    {
        __state = RimBridgeUiWorkbench.BeginTextField("widgets.text_field", rect, null, ref text);
    }

    public static void Postfix(ref string __result, RimBridgeUiWorkbench.TextFieldPatchState __state)
    {
        RimBridgeUiWorkbench.EndTextField(__state, ref __result);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.TextEntryLabeled), new[] { typeof(Rect), typeof(string), typeof(string) })]
internal static class Widgets_TextEntryLabeled_UiWorkbench_Patch
{
    public static void Prefix(Rect rect, string label, string text)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("text_field", "widgets.text_entry_labeled", rect, label, text);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(string) })]
internal static class Gui_Label_String_UiWorkbench_Patch
{
    public static void Prefix(Rect position, string text)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("label", "gui.label", position, text);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) })]
internal static class Gui_Label_StringStyle_UiWorkbench_Patch
{
    public static void Prefix(Rect position, string text)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("label", "gui.label", position, text);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(GUIContent) })]
internal static class Gui_Label_Content_UiWorkbench_Patch
{
    public static void Prefix(Rect position, GUIContent content)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("label", "gui.label", position, content?.text);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) })]
internal static class Gui_Label_ContentStyle_UiWorkbench_Patch
{
    public static void Prefix(Rect position, GUIContent content)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("label", "gui.label", position, content?.text);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.TextField), new[] { typeof(Rect), typeof(string) })]
internal static class Gui_TextField_UiWorkbench_Patch
{
    public static void Prefix(Rect position, ref string text, ref RimBridgeUiWorkbench.TextFieldPatchState __state)
    {
        __state = RimBridgeUiWorkbench.BeginTextField("gui.text_field", position, null, ref text);
    }

    public static void Postfix(ref string __result, RimBridgeUiWorkbench.TextFieldPatchState __state)
    {
        RimBridgeUiWorkbench.EndTextField(__state, ref __result);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.TextField), new[] { typeof(Rect), typeof(string), typeof(int) })]
internal static class Gui_TextField_Limited_UiWorkbench_Patch
{
    public static void Prefix(Rect position, ref string text, ref RimBridgeUiWorkbench.TextFieldPatchState __state)
    {
        __state = RimBridgeUiWorkbench.BeginTextField("gui.text_field", position, null, ref text);
    }

    public static void Postfix(ref string __result, RimBridgeUiWorkbench.TextFieldPatchState __state)
    {
        RimBridgeUiWorkbench.EndTextField(__state, ref __result);
    }
}

[HarmonyPatch(typeof(GUI), "DoTextField", new[] { typeof(Rect), typeof(int), typeof(GUIContent), typeof(bool), typeof(int), typeof(GUIStyle), typeof(string), typeof(char) })]
internal static class Gui_DoTextField_UiWorkbench_Patch
{
    public static void Prefix(int id)
    {
        RimBridgeUiWorkbench.FocusPendingTextField(id);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(string) })]
internal static class Gui_Button_String_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect position, string text, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "gui.button", position, text, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, position);
    }

    public static void Postfix(string text, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, $"Activated UI target '{(string.IsNullOrWhiteSpace(text) ? "button" : text)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) })]
internal static class Gui_Button_StringStyle_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect position, string text, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "gui.button", position, text, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, position);
    }

    public static void Postfix(string text, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, $"Activated UI target '{(string.IsNullOrWhiteSpace(text) ? "button" : text)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(GUIContent) })]
internal static class Gui_Button_Content_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect position, GUIContent content, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "gui.button", position, label, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, position);
    }

    public static void Postfix(GUIContent content, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "button" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) })]
internal static class Gui_Button_ContentStyle_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect position, GUIContent content, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "gui.button", position, label, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, position);
    }

    public static void Postfix(GUIContent content, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "button" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(string) })]
internal static class Gui_Toggle_String_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect position, bool value, string text, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "gui.toggle", position, text, checkedState: value);
        __state.InitialCheckedState = value;
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, position);
    }

    public static void Postfix(string text, bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.ObserveControlResult(
            __state,
            __state.InitialCheckedState.HasValue && __result != __state.InitialCheckedState.Value,
            $"Activated UI target '{(string.IsNullOrWhiteSpace(text) ? "checkbox" : text)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(string), typeof(GUIStyle) })]
internal static class Gui_Toggle_StringStyle_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect position, bool value, string text, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "gui.toggle", position, text, checkedState: value);
        __state.InitialCheckedState = value;
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, position);
    }

    public static void Postfix(string text, bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.ObserveControlResult(
            __state,
            __state.InitialCheckedState.HasValue && __result != __state.InitialCheckedState.Value,
            $"Activated UI target '{(string.IsNullOrWhiteSpace(text) ? "checkbox" : text)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(GUIContent) })]
internal static class Gui_Toggle_Content_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect position, bool value, GUIContent content, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "gui.toggle", position, label, checkedState: value);
        __state.InitialCheckedState = value;
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, position);
    }

    public static void Postfix(GUIContent content, bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        RimBridgeUiWorkbench.ObserveControlResult(
            __state,
            __state.InitialCheckedState.HasValue && __result != __state.InitialCheckedState.Value,
            $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "checkbox" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(GUIContent), typeof(GUIStyle) })]
internal static class Gui_Toggle_ContentStyle_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect position, bool value, GUIContent content, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "gui.toggle", position, label, checkedState: value);
        __state.InitialCheckedState = value;
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, position);
    }

    public static void Postfix(GUIContent content, bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        RimBridgeUiWorkbench.ObserveControlResult(
            __state,
            __state.InitialCheckedState.HasValue && __result != __state.InitialCheckedState.Value,
            $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "checkbox" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch]
internal static class Widgets_CheckboxLabeled_UiWorkbench_Patch
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Widgets), nameof(Widgets.CheckboxLabeled), [typeof(Rect), typeof(string), typeof(bool).MakeByRefType(), typeof(bool), typeof(Texture2D), typeof(Texture2D), typeof(bool), typeof(bool)]);
    }

    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect rect, string label, ref bool checkOn, bool disabled, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "widgets.checkbox_labeled", rect, label, checkedState: checkOn, disabled: disabled);
        __state.InitialCheckedState = checkOn;
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, rect);
    }

    public static void Postfix(string label, bool checkOn, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.ObserveControlResult(
            __state,
            __state.InitialCheckedState.HasValue && checkOn != __state.InitialCheckedState.Value,
            $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "checkbox" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch]
internal static class Widgets_Checkbox_Vector_UiWorkbench_Patch
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Widgets), nameof(Widgets.Checkbox), [typeof(Vector2), typeof(bool).MakeByRefType(), typeof(float), typeof(bool), typeof(bool), typeof(Texture2D), typeof(Texture2D)]);
    }

    [HarmonyPriority(Priority.First)]
    public static void Prefix(Vector2 topLeft, ref bool checkOn, float size, bool disabled, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl(
            "checkbox",
            "widgets.checkbox",
            new Rect(topLeft.x, topLeft.y, size, size),
            checkedState: checkOn,
            disabled: disabled);
        __state.InitialCheckedState = checkOn;
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, new Rect(topLeft.x, topLeft.y, size, size));
    }

    public static void Postfix(bool checkOn, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.ObserveControlResult(
            __state,
            __state.InitialCheckedState.HasValue && checkOn != __state.InitialCheckedState.Value,
            "Activated a checkbox UI target on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch]
internal static class Widgets_Checkbox_Floats_UiWorkbench_Patch
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Widgets), nameof(Widgets.Checkbox), [typeof(float), typeof(float), typeof(bool).MakeByRefType(), typeof(float), typeof(bool), typeof(bool), typeof(Texture2D), typeof(Texture2D)]);
    }

    [HarmonyPriority(Priority.First)]
    public static void Prefix(float x, float y, ref bool checkOn, float size, bool disabled, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl(
            "checkbox",
            "widgets.checkbox",
            new Rect(x, y, size, size),
            checkedState: checkOn,
            disabled: disabled);
        __state.InitialCheckedState = checkOn;
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, new Rect(x, y, size, size));
    }

    public static void Postfix(bool checkOn, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.ObserveControlResult(
            __state,
            __state.InitialCheckedState.HasValue && checkOn != __state.InitialCheckedState.Value,
            "Activated a checkbox UI target on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.RadioButtonLabeled), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool) })]
internal static class Widgets_RadioButtonLabeled_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect rect, string labelText, bool disabled, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("radio_button", "widgets.radio_button_labeled", rect, labelText, actionable: true, disabled: disabled);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, rect);
    }

    public static void Postfix(string labelText, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, $"Activated UI target '{(string.IsNullOrWhiteSpace(labelText) ? "radio button" : labelText)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonInvisible), new[] { typeof(Rect), typeof(bool) })]
internal static class Widgets_ButtonInvisible_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect butRect, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "widgets.button_invisible", butRect, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, butRect);
    }

    public static void Postfix(ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, "Activated an invisible button UI target on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonInvisibleDraggable), new[] { typeof(Rect), typeof(bool) })]
internal static class Widgets_ButtonInvisibleDraggable_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect butRect, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "widgets.button_invisible_draggable", butRect, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, butRect);
    }

    public static void Postfix(Rect butRect, ref Widgets.DraggableResult __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideDraggableResultIfPending(butRect, ref __result);
        var activated = __result == Widgets.DraggableResult.Pressed || __result == Widgets.DraggableResult.DraggedThenPressed;
        RimBridgeUiWorkbench.ObserveControlResult(__state, activated, "Activated a draggable invisible button UI target on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(Nullable<TextAnchor>) })]
internal static class Widgets_ButtonText_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect rect, string label, bool active, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "widgets.button_text", rect, label, actionable: true, disabled: !active);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, rect);
    }

    public static void Postfix(string label, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "button" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(Nullable<TextAnchor>) })]
internal static class Widgets_ButtonTextColored_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect rect, string label, bool active, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "widgets.button_text", rect, label, actionable: true, disabled: !active);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, rect);
    }

    public static void Postfix(string label, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "button" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonImage), new[] { typeof(Rect), typeof(Texture2D), typeof(bool), typeof(string) })]
internal static class Widgets_ButtonImage_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect butRect, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("icon_button", "widgets.button_image", butRect, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, butRect);
    }

    public static void Postfix(ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, "Activated an icon button UI target on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonImage), new[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(bool), typeof(string) })]
internal static class Widgets_ButtonImageColored_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect butRect, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("icon_button", "widgets.button_image", butRect, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, butRect);
    }

    public static void Postfix(ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, "Activated an icon button UI target on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonImage), new[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(Color), typeof(bool), typeof(string) })]
internal static class Widgets_ButtonImageHoverColored_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Rect butRect, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("icon_button", "widgets.button_image", butRect, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, butRect);
    }

    public static void Postfix(ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, "Activated an icon button UI target on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.Label), new[] { typeof(string), typeof(float), typeof(Nullable<TipSignal>) })]
internal static class ListingStandard_Label_String_UiWorkbench_Patch
{
    public static void Prefix(Listing_Standard __instance, string label)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        RimBridgeUiWorkbench.RegisterPassiveElement("label", "listing_standard.label", rect, label);
    }
}

[HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.Label), new[] { typeof(TaggedString), typeof(float), typeof(string) })]
internal static class ListingStandard_Label_TaggedString_UiWorkbench_Patch
{
    public static void Prefix(Listing_Standard __instance, TaggedString label)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        RimBridgeUiWorkbench.RegisterPassiveElement("label", "listing_standard.label", rect, label.ToString());
    }
}

[HarmonyPatch]
internal static class ListingStandard_CheckboxLabeled_Basic_UiWorkbench_Patch
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Listing_Standard), nameof(Listing_Standard.CheckboxLabeled), [typeof(string), typeof(bool).MakeByRefType(), typeof(float)]);
    }

    [HarmonyPriority(Priority.First)]
    public static void Prefix(Listing_Standard __instance, string label, ref bool checkOn, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "listing_standard.checkbox_labeled", rect, label, checkedState: checkOn);
        __state.InitialCheckedState = checkOn;
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, rect);
    }

    public static void Postfix(string label, bool checkOn, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.ObserveControlResult(
            __state,
            __state.InitialCheckedState.HasValue && checkOn != __state.InitialCheckedState.Value,
            $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "checkbox" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch]
internal static class ListingStandard_CheckboxLabeled_Tooltip_UiWorkbench_Patch
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Listing_Standard), nameof(Listing_Standard.CheckboxLabeled), [typeof(string), typeof(bool).MakeByRefType(), typeof(string), typeof(float), typeof(float)]);
    }

    [HarmonyPriority(Priority.First)]
    public static void Prefix(Listing_Standard __instance, string label, ref bool checkOn, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "listing_standard.checkbox_labeled", rect, label, checkedState: checkOn);
        __state.InitialCheckedState = checkOn;
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, rect);
    }

    public static void Postfix(string label, bool checkOn, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.ObserveControlResult(
            __state,
            __state.InitialCheckedState.HasValue && checkOn != __state.InitialCheckedState.Value,
            $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "checkbox" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonText), new[] { typeof(string), typeof(string), typeof(float) })]
internal static class ListingStandard_ButtonText_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Listing_Standard __instance, string label, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "listing_standard.button_text", rect, label, actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, rect);
    }

    public static void Postfix(string label, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, $"Activated UI target '{(string.IsNullOrWhiteSpace(label) ? "button" : label)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonTextLabeled), new[] { typeof(string), typeof(string), typeof(TextAnchor), typeof(string), typeof(string) })]
internal static class ListingStandard_ButtonTextLabeled_UiWorkbench_Patch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(Listing_Standard __instance, string label, string buttonLabel, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "listing_standard.button_text_labeled", rect, $"{label}: {buttonLabel}", actionable: true);
        RimBridgeUiWorkbench.PrepareControlInteraction(__state, rect);
    }

    public static void Postfix(string buttonLabel, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.OverrideButtonResultIfPending(__state, ref __result);
        RimBridgeUiWorkbench.ObserveControlResult(__state, __result, $"Activated UI target '{(string.IsNullOrWhiteSpace(buttonLabel) ? "button" : buttonLabel)}' on the current surface.");
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.TextEntryLabeled), new[] { typeof(string), typeof(string), typeof(int) })]
internal static class ListingStandard_TextEntryLabeled_UiWorkbench_Patch
{
    public static void Prefix(Listing_Standard __instance, string label, string text)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        RimBridgeUiWorkbench.RegisterPassiveElement("text_field", "listing_standard.text_entry_labeled", rect, label, text);
    }
}

[HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.SliderLabeled), new[] { typeof(string), typeof(float), typeof(float), typeof(float), typeof(float), typeof(string) })]
internal static class ListingStandard_SliderLabeled_UiWorkbench_Patch
{
    public static void Prefix(Listing_Standard __instance, string label, float val, float min, float max)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        RimBridgeUiWorkbench.RegisterPassiveElement("slider", "listing_standard.slider_labeled", rect, label, $"{val:0.##} [{min:0.##}, {max:0.##}]");
    }
}
