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

    public List<UiLayoutElementSnapshot> Elements { get; set; } = [];
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
}

internal static class RimBridgeUiWorkbench
{
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

        public string Message { get; set; } = string.Empty;
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

    internal sealed class UiPatchControlState
    {
        public bool TrackingEnabled { get; set; }

        public bool SuppressNested { get; set; }

        public bool ShouldActivate { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly Stack<LiveSurfaceState> SurfaceStack = [];
    private static readonly Dictionary<int, UiLayoutSnapshot> RecentCaptures = [];
    private static readonly Queue<int> CaptureOrder = [];
    private static CaptureRequest _pendingCapture;
    private static ClickRequest _pendingClick;
    private static int _nextCaptureId = 1;
    private const int MaxRetainedCaptures = 8;

    public static object GetUiLayoutResponse(string surfaceId = null, int timeoutMs = 2000)
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
        return DescribeLayout(snapshot);
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

        request.Completion.Task.GetAwaiter().GetResult();
        var after = RimBridgeMainThread.Invoke(RimWorldInput.GetUiState, timeoutMs: 5000);
        return CreateClickResponse(targetId, before, after, request.Message);
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
                X = element?.Rect.X ?? surface.Rect.X,
                Y = element?.Rect.Y ?? surface.Rect.Y,
                Width = element?.Rect.Width ?? surface.Rect.Width,
                Height = element?.Rect.Height ?? surface.Rect.Height
            }
        };
        return true;
    }

    public static void AdvanceFrame(int frameCount)
    {
        CaptureRequest completedCapture = null;

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
        }

        completedCapture?.Completion.TrySetResult(completedCapture.Snapshot);
    }

    public static void BeginSurface(Window window)
    {
        if (window == null)
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
            if (capture == null && click == null)
            {
                SurfaceStack.Push(null);
                return;
            }

            var descriptor = DescribeSurface(window);
            var shouldCapture = capture != null && SurfaceMatches(capture.SurfaceTargetId, descriptor.SurfaceTargetId);
            var shouldTrackForClick = click != null && string.Equals(click.SurfaceTargetId, descriptor.SurfaceTargetId, StringComparison.Ordinal);
            if (!shouldCapture && !shouldTrackForClick)
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
                        Rect = descriptor.Rect
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
                CaptureSurface = captureSurface
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
                ShouldActivate = ShouldActivateCurrentElement(surface, kind, source, rect, label, actionable)
            };
        }
    }

    public static void EndCompoundControl(UiPatchControlState state)
    {
        if (state == null || !state.TrackingEnabled || !state.SuppressNested)
            return;

        lock (Sync)
        {
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

    public static void TryActivateCurrentControl(UiPatchControlState state, string message)
    {
        if (state == null || !state.ShouldActivate)
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

    private static string RegisterElement(
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
        var normalizedLabel = NormalizeText(label);
        var normalizedValueText = NormalizeText(valueText);
        if (ShouldSuppressGuiFallback(surface, kind, source, rect, normalizedLabel, normalizedValueText, actionable, checkedState, disabled))
            return surface.LastRegisteredElement?.TargetId;

        surface.ElementIndex++;
        var elementIndex = surface.ElementIndex;
        var rectSnapshot = ToSnapshot(rect);
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
                Rect = rectSnapshot
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

    private static bool ShouldActivateCurrentElement(LiveSurfaceState surface, string kind, string source, Rect rect, string label, bool actionable)
    {
        if (!actionable || _pendingClick == null || _pendingClick.Activated)
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

    private static CaptureRequest QueueCapture(string surfaceId)
    {
        lock (Sync)
        {
            if (_pendingCapture != null)
                throw new InvalidOperationException("A UI layout capture is already pending.");
            if (_pendingClick != null)
                throw new InvalidOperationException("Cannot start a UI layout capture while a UI target click is pending.");

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

            if (!UiLayoutTargetIds.TryParse(targetId, out var target) || target.Kind != UiLayoutTargetKind.Element)
                throw new InvalidOperationException($"Target id '{targetId}' is not a UI element target id returned by rimworld/get_ui_layout.");

            if (!TryResolveTarget(target, out var surface, out var element))
                throw new InvalidOperationException($"UI layout target '{targetId}' is no longer available. Capture a fresh layout snapshot.");
            if (element == null || !element.Actionable)
                throw new InvalidOperationException($"UI layout target '{targetId}' is not actionable.");

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

    private static (string SurfaceTargetId, string SurfaceKind, string Type, string Title, string Label, string SemanticKind, object SemanticDetails, UiRectSnapshot Rect) DescribeSurface(Window window)
    {
        var type = window.GetType().FullName ?? window.GetType().Name;
        string semanticKind = string.Empty;
        object semanticDetails = null;
        RimWorldModSettings.TryDescribeWindow(window, out semanticKind, out semanticDetails);

        if (window is MainTabWindow mainTabWindow && mainTabWindow.def != null && !string.IsNullOrWhiteSpace(mainTabWindow.def.defName))
        {
            return (
                ScreenTargetIds.CreateMainTabTargetId(mainTabWindow.def.defName),
                "main_tab",
                type,
                null,
                string.IsNullOrWhiteSpace(mainTabWindow.def.label) ? mainTabWindow.def.defName : mainTabWindow.def.LabelCap.ToString(),
                semanticKind,
                semanticDetails,
                ToSnapshot(window.windowRect));
        }

        return (
            ScreenTargetIds.CreateWindowTargetId(window.ID, type),
            "window",
            type,
            string.IsNullOrWhiteSpace(window.optionalTitle) ? null : window.optionalTitle,
            null,
            semanticKind,
            semanticDetails,
            ToSnapshot(window.windowRect));
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

    private static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    private static string BuildTimeoutMessage(string surfaceId, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(surfaceId))
            return $"Timed out waiting {timeoutMs}ms for a UI surface to draw. Open a dialog or main tab and retry.";

        return $"Timed out waiting {timeoutMs}ms for UI surface '{surfaceId}' to draw. Verify that the surface is open and retry.";
    }

    private static string BuildClickMessage(UiLayoutSurfaceSnapshot surface, UiLayoutElementSnapshot element)
    {
        var label = string.IsNullOrWhiteSpace(element.Label) ? element.Kind : element.Label;
        return $"Activated UI target '{label}' on surface '{surface.SurfaceTargetId}'.";
    }

    private static object DescribeLayout(UiLayoutSnapshot snapshot)
    {
        return new
        {
            success = true,
            captureId = snapshot.CaptureId,
            capturedFrame = snapshot.CapturedFrame,
            capturedAtUtc = snapshot.CapturedAtUtc,
            surfaceCount = snapshot.Surfaces.Count,
            surfaces = snapshot.Surfaces.Select(surface => new
            {
                captureTargetId = surface.CaptureTargetId,
                surfaceTargetId = surface.SurfaceTargetId,
                surfaceKind = surface.SurfaceKind,
                type = surface.Type,
                title = surface.Title,
                label = surface.Label,
                semanticKind = string.IsNullOrWhiteSpace(surface.SemanticKind) ? null : surface.SemanticKind,
                semanticDetails = surface.SemanticDetails,
                rect = new
                {
                    x = surface.Rect.X,
                    y = surface.Rect.Y,
                    width = surface.Rect.Width,
                    height = surface.Rect.Height
                },
                elementCount = surface.Elements.Count,
                actionableElementCount = surface.Elements.Count(element => element.Actionable),
                elements = surface.Elements.Select(element => new
                {
                    targetId = element.TargetId,
                    kind = element.Kind,
                    source = element.Source,
                    label = element.Label,
                    valueText = element.ValueText,
                    actionable = element.Actionable,
                    clipCapable = element.ClipCapable,
                    isChecked = element.Checked,
                    disabled = element.Disabled,
                    depth = element.Depth,
                    parentTargetId = element.ParentTargetId,
                    rect = new
                    {
                        x = element.Rect.X,
                        y = element.Rect.Y,
                        width = element.Rect.Width,
                        height = element.Rect.Height
                    }
                }).ToList()
            }).ToList()
        };
    }

    private static object CreateClickResponse(string targetId, UiStateSnapshot before, UiStateSnapshot after, string message)
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
            before = RimWorldInput.DescribeUiState(before),
            after = RimWorldInput.DescribeUiState(after),
            openedWindowTypes,
            closedWindowTypes
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

    public static void Prefix(Rect outRect, Vector2 scrollPosition, Rect viewRect)
    {
        RimBridgeUiWorkbench.BeginContainer("scroll_view", "widgets.begin_scroll_view", outRect);
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
    public static void Prefix(Rect rect, string text)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("text_field", "widgets.text_field", rect, valueText: text);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.TextField), new[] { typeof(Rect), typeof(string), typeof(int), typeof(Regex) })]
internal static class Widgets_TextField_Validated_UiWorkbench_Patch
{
    public static void Prefix(Rect rect, string text)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("text_field", "widgets.text_field", rect, valueText: text);
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
    public static void Prefix(Rect position, string text)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("text_field", "gui.text_field", position, valueText: text);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.TextField), new[] { typeof(Rect), typeof(string), typeof(int) })]
internal static class Gui_TextField_Limited_UiWorkbench_Patch
{
    public static void Prefix(Rect position, string text)
    {
        RimBridgeUiWorkbench.RegisterPassiveElement("text_field", "gui.text_field", position, valueText: text);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(string) })]
internal static class Gui_Button_String_UiWorkbench_Patch
{
    public static bool Prefix(Rect position, string text, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "gui.button", position, text, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{text}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) })]
internal static class Gui_Button_StringStyle_UiWorkbench_Patch
{
    public static bool Prefix(Rect position, string text, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "gui.button", position, text, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{text}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(GUIContent) })]
internal static class Gui_Button_Content_UiWorkbench_Patch
{
    public static bool Prefix(Rect position, GUIContent content, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "gui.button", position, label, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Button), new[] { typeof(Rect), typeof(GUIContent), typeof(GUIStyle) })]
internal static class Gui_Button_ContentStyle_UiWorkbench_Patch
{
    public static bool Prefix(Rect position, GUIContent content, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "gui.button", position, label, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(string) })]
internal static class Gui_Toggle_String_UiWorkbench_Patch
{
    public static bool Prefix(Rect position, bool value, string text, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "gui.toggle", position, text, checkedState: value);
        if (!__state.ShouldActivate)
            return true;

        __result = !value;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{text}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(string), typeof(GUIStyle) })]
internal static class Gui_Toggle_StringStyle_UiWorkbench_Patch
{
    public static bool Prefix(Rect position, bool value, string text, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "gui.toggle", position, text, checkedState: value);
        if (!__state.ShouldActivate)
            return true;

        __result = !value;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{text}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(GUIContent) })]
internal static class Gui_Toggle_Content_UiWorkbench_Patch
{
    public static bool Prefix(Rect position, bool value, GUIContent content, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "gui.toggle", position, label, checkedState: value);
        if (!__state.ShouldActivate)
            return true;

        __result = !value;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(GUI), nameof(GUI.Toggle), new[] { typeof(Rect), typeof(bool), typeof(GUIContent), typeof(GUIStyle) })]
internal static class Gui_Toggle_ContentStyle_UiWorkbench_Patch
{
    public static bool Prefix(Rect position, bool value, GUIContent content, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var label = content?.text;
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "gui.toggle", position, label, checkedState: value);
        if (!__state.ShouldActivate)
            return true;

        __result = !value;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
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

    public static bool Prefix(Rect rect, string label, ref bool checkOn, bool disabled, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "widgets.checkbox_labeled", rect, label, checkedState: checkOn, disabled: disabled);
        if (!__state.ShouldActivate)
            return true;

        checkOn = !checkOn;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
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

    public static bool Prefix(Vector2 topLeft, ref bool checkOn, float size, bool disabled, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl(
            "checkbox",
            "widgets.checkbox",
            new Rect(topLeft.x, topLeft.y, size, size),
            checkedState: checkOn,
            disabled: disabled);
        if (!__state.ShouldActivate)
            return true;

        checkOn = !checkOn;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, "Activated a checkbox UI target on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
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

    public static bool Prefix(float x, float y, ref bool checkOn, float size, bool disabled, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl(
            "checkbox",
            "widgets.checkbox",
            new Rect(x, y, size, size),
            checkedState: checkOn,
            disabled: disabled);
        if (!__state.ShouldActivate)
            return true;

        checkOn = !checkOn;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, "Activated a checkbox UI target on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.RadioButtonLabeled), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool) })]
internal static class Widgets_RadioButtonLabeled_UiWorkbench_Patch
{
    public static bool Prefix(Rect rect, string labelText, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("radio_button", "widgets.radio_button_labeled", rect, labelText, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{labelText}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonInvisible), new[] { typeof(Rect), typeof(bool) })]
internal static class Widgets_ButtonInvisible_UiWorkbench_Patch
{
    public static bool Prefix(Rect butRect, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "widgets.button_invisible", butRect, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, "Activated an invisible button UI target on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(Nullable<TextAnchor>) })]
internal static class Widgets_ButtonText_UiWorkbench_Patch
{
    public static bool Prefix(Rect rect, string label, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "widgets.button_text", rect, label, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonText), new[] { typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(Color), typeof(bool), typeof(Nullable<TextAnchor>) })]
internal static class Widgets_ButtonTextColored_UiWorkbench_Patch
{
    public static bool Prefix(Rect rect, string label, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "widgets.button_text", rect, label, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonImage), new[] { typeof(Rect), typeof(Texture2D), typeof(bool), typeof(string) })]
internal static class Widgets_ButtonImage_UiWorkbench_Patch
{
    public static bool Prefix(Rect butRect, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("icon_button", "widgets.button_image", butRect, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, "Activated an icon button UI target on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonImage), new[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(bool), typeof(string) })]
internal static class Widgets_ButtonImageColored_UiWorkbench_Patch
{
    public static bool Prefix(Rect butRect, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("icon_button", "widgets.button_image", butRect, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, "Activated an icon button UI target on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Widgets), nameof(Widgets.ButtonImage), new[] { typeof(Rect), typeof(Texture2D), typeof(Color), typeof(Color), typeof(bool), typeof(string) })]
internal static class Widgets_ButtonImageHoverColored_UiWorkbench_Patch
{
    public static bool Prefix(Rect butRect, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        __state = RimBridgeUiWorkbench.BeginCompoundControl("icon_button", "widgets.button_image", butRect, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, "Activated an icon button UI target on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
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

    public static bool Prefix(Listing_Standard __instance, string label, ref bool checkOn, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "listing_standard.checkbox_labeled", rect, label, checkedState: checkOn);
        if (!__state.ShouldActivate)
            return true;

        checkOn = !checkOn;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
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

    public static bool Prefix(Listing_Standard __instance, string label, ref bool checkOn, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        __state = RimBridgeUiWorkbench.BeginCompoundControl("checkbox", "listing_standard.checkbox_labeled", rect, label, checkedState: checkOn);
        if (!__state.ShouldActivate)
            return true;

        checkOn = !checkOn;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonText), new[] { typeof(string), typeof(string), typeof(float) })]
internal static class ListingStandard_ButtonText_UiWorkbench_Patch
{
    public static bool Prefix(Listing_Standard __instance, string label, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "listing_standard.button_text", rect, label, actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{label}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        RimBridgeUiWorkbench.EndCompoundControl(__state);
    }
}

[HarmonyPatch(typeof(Listing_Standard), nameof(Listing_Standard.ButtonTextLabeled), new[] { typeof(string), typeof(string), typeof(TextAnchor), typeof(string), typeof(string) })]
internal static class ListingStandard_ButtonTextLabeled_UiWorkbench_Patch
{
    public static bool Prefix(Listing_Standard __instance, string label, string buttonLabel, ref bool __result, ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
        var rect = new Rect(__instance.listingRect.x + __instance.curX, __instance.listingRect.y + __instance.curY, __instance.ColumnWidth, Text.LineHeight);
        __state = RimBridgeUiWorkbench.BeginCompoundControl("button", "listing_standard.button_text_labeled", rect, $"{label}: {buttonLabel}", actionable: true);
        if (!__state.ShouldActivate)
            return true;

        __result = true;
        RimBridgeUiWorkbench.TryActivateCurrentControl(__state, $"Activated UI target '{buttonLabel}' on the current surface.");
        return false;
    }

    public static void Postfix(ref RimBridgeUiWorkbench.UiPatchControlState __state)
    {
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
