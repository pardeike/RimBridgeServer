using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal sealed class ViewCapabilityModule
{
    private const float MinimumPublicCameraRootSize = 8f;
    // Allow screenshot_cell_rect to fit tiny map rects more tightly than the public camera zoom floor.
    private const float MinimumScreenshotCameraRootSize = 0.1f;
    private const float MaximumCameraRootSize = 140f;
    private const int DefaultScreenshotCellPadding = 4;

    private sealed class PendingScreenshotCapture
    {
        public string ExpectedPath { get; set; } = string.Empty;

        public string OutputPath { get; set; } = string.Empty;

        public string OutputFileName { get; set; } = string.Empty;

        public object ScreenTargets { get; set; }

        public bool RestoreSuppressMessage { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;

        public string ClipTargetId { get; set; } = string.Empty;

        public string ClipTargetKind { get; set; } = string.Empty;

        public string ClipTargetLabel { get; set; } = string.Empty;

        public int ClipPadding { get; set; }

        public UiRectSnapshot ClipRect { get; set; }
    }

    private sealed class ScreenshotCropResult
    {
        public bool Success { get; set; }

        public ScreenshotPixelRect ClipRect { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;
    }

    private sealed class ScreenshotCaptureResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string SourcePath { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string RequestedFileName { get; set; } = string.Empty;

        public string ScreenshotFolder { get; set; } = string.Empty;

        public long? SizeBytes { get; set; }

        public DateTime? CapturedAtUtc { get; set; }

        public bool SuppressMessage { get; set; }

        public bool Clipped { get; set; }

        public string ClipTargetId { get; set; } = string.Empty;

        public string ClipTargetKind { get; set; } = string.Empty;

        public string ClipTargetLabel { get; set; } = string.Empty;

        public int ClipPadding { get; set; }

        public ScreenshotPixelRect? ClipRect { get; set; }

        public object ScreenTargets { get; set; }

        public string ExpectedPath { get; set; } = string.Empty;
    }

    private sealed class CameraSnapshot
    {
        public IntVec3 MapPosition { get; set; }

        public float RootSize { get; set; }

        public FloatRange SizeRange { get; set; }

        public object Payload { get; set; }
    }

    private sealed class MapCellRectSnapshot
    {
        public int X { get; set; }

        public int Z { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public int MaxX { get; set; }

        public int MaxZ { get; set; }
    }

    private sealed class CellRectFrameResult
    {
        public bool Success { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;

        public MapCellRectSnapshot RequestedRect { get; set; }

        public MapCellRectSnapshot PaddedRect { get; set; }

        public int PaddingCells { get; set; }

        public bool FullyVisible { get; set; }

        public float AppliedRootSize { get; set; }

        public int PreparedFrameCount { get; set; }

        public object Camera { get; set; }
    }

    public object GetCameraState()
    {
        return RimWorldState.DescribeCamera();
    }

    public object GetScreenTargets()
    {
        return RimWorldTargeting.GetScreenTargetsResponse();
    }

    public object GetMapTargetInfo(string thingId = null, string pawnName = null, string pawnId = null)
    {
        var hasThingId = !string.IsNullOrWhiteSpace(thingId);
        var hasPawnSelector = !string.IsNullOrWhiteSpace(pawnName) || !string.IsNullOrWhiteSpace(pawnId);
        if (hasThingId == hasPawnSelector)
        {
            return new
            {
                success = false,
                message = "Provide either thingId or pawnName/pawnId."
            };
        }

        var thing = hasThingId
            ? RimWorldState.ResolveCurrentMapThing(thingId)
            : RimWorldState.ResolveCurrentMapPawn(pawnName, pawnId);

        if (!thing.Spawned || !thing.Position.IsValid)
        {
            return new
            {
                success = false,
                message = $"Target '{(hasThingId ? thingId?.Trim() : pawnId?.Trim() ?? pawnName?.Trim())}' is not currently spawned on the active map."
            };
        }

        var occupiedRect = thing.OccupiedRect();
        return new
        {
            success = true,
            kind = thing is Pawn ? "pawn" : "thing",
            label = thing.LabelCap.ToString(),
            thingId = RimWorldState.GetThingId(thing),
            pawnId = thing is Pawn pawn ? RimWorldState.GetThingId(pawn) : null,
            mapId = RimWorldState.GetMapId(thing.MapHeld),
            mapIndex = thing.MapHeld?.Index,
            position = new
            {
                x = thing.Position.x,
                z = thing.Position.z
            },
            cellRect = CreateMapCellRectPayload(occupiedRect),
            target = thing is Pawn targetPawn ? RimWorldState.DescribePawn(targetPawn) : RimWorldState.DescribeThing(thing)
        };
    }

    public object JumpCameraToPawn(string pawnName = null, string pawnId = null)
    {
        var pawn = RimWorldState.ResolveCurrentMapPawn(pawnName, pawnId);
        Find.CameraDriver.JumpToCurrentMapLoc(pawn.Position);
        Find.Selector.ClearSelection();
        Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
        return new
        {
            success = true,
            target = pawn.Name?.ToStringShort ?? pawn.LabelShort,
            targetPawn = RimWorldState.DescribePawn(pawn),
            camera = RimWorldState.DescribeCamera()
        };
    }

    public object JumpCameraToCell(int x, int z)
    {
        var cell = new IntVec3(x, 0, z);
        var map = RimWorldState.CurrentMapOrThrow();
        if (!cell.InBounds(map))
            return new { success = false, message = $"Cell ({x}, {z}) is out of bounds for the current map." };

        Find.CameraDriver.JumpToCurrentMapLoc(cell);
        return new { success = true, cell = new { x, z }, camera = RimWorldState.DescribeCamera() };
    }

    public object MoveCamera(float deltaX, float deltaZ)
    {
        var driver = Find.CameraDriver;
        var current = driver.MapPosition;
        var target = new IntVec3(
            Mathf.RoundToInt(current.x + deltaX),
            0,
            Mathf.RoundToInt(current.z + deltaZ));

        var map = RimWorldState.CurrentMapOrThrow();
        target.x = Mathf.Clamp(target.x, 0, map.Size.x - 1);
        target.z = Mathf.Clamp(target.z, 0, map.Size.z - 1);
        driver.JumpToCurrentMapLoc(target);

        return new { success = true, cell = new { x = target.x, z = target.z }, camera = RimWorldState.DescribeCamera() };
    }

    public object ZoomCamera(float delta)
    {
        var driver = Find.CameraDriver;
        var newSize = Mathf.Clamp(driver.RootSize + delta, MinimumPublicCameraRootSize, MaximumCameraRootSize);
        driver.SetRootSize(newSize);

        return new { success = true, rootSize = driver.RootSize, camera = RimWorldState.DescribeCamera() };
    }

    public object SetCameraZoom(float rootSize)
    {
        var driver = Find.CameraDriver;
        driver.SetRootSize(Mathf.Clamp(rootSize, MinimumPublicCameraRootSize, MaximumCameraRootSize));

        return new { success = true, rootSize = driver.RootSize, camera = RimWorldState.DescribeCamera() };
    }

    public object FramePawns(string pawnNamesCsv = null, string pawnIdsCsv = null)
    {
        List<Pawn> pawns;
        if (string.IsNullOrWhiteSpace(pawnNamesCsv) && string.IsNullOrWhiteSpace(pawnIdsCsv))
        {
            pawns = Find.Selector.SelectedPawns.Where(pawn => pawn.Spawned).ToList();
        }
        else
        {
            pawns = RimWorldState.ParseNames(pawnNamesCsv)
                .Select(name => RimWorldState.ResolveCurrentMapPawn(name))
                .Concat(RimWorldState.ParseNames(pawnIdsCsv)
                    .Select(id => RimWorldState.ResolveCurrentMapPawn(pawnId: id)))
                .Where(pawn => pawn.Spawned)
                .Distinct()
                .ToList();
        }

        if (pawns.Count == 0)
            return new { success = false, message = "No spawned pawns were available to frame." };

        var center = new Vector3(
            (float)pawns.Average(pawn => pawn.Position.x) + 0.5f,
            0f,
            (float)pawns.Average(pawn => pawn.Position.z) + 0.5f);
        var size = RimWorldState.ComputeFrameRootSize(pawns);

        Find.CameraDriver.SetRootPosAndSize(center, size);

        return new
        {
            success = true,
            framedPawns = pawns.Select(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShort).ToList(),
            framedPawnDetails = pawns.Select(RimWorldState.DescribePawn).ToList(),
            rootSize = Find.CameraDriver.RootSize,
            camera = RimWorldState.DescribeCamera()
        };
    }

    public object FrameCellRect(
        int x,
        int z,
        int width = 1,
        int height = 1,
        int paddingCells = DefaultScreenshotCellPadding)
    {
        RimBridgeMainThread.Invoke(ApplyScreenshotCameraConfig, timeoutMs: 5000);
        var frame = RimBridgeMainThread.Invoke(() => FrameCellRectForScreenshot(x, z, width, height, paddingCells), timeoutMs: 5000);
        if (!frame.Success)
        {
            return new
            {
                success = false,
                message = frame.ErrorMessage,
                requestedRect = CreateRequestedRectPayload(x, z, width, height),
                paddingCells = Math.Max(paddingCells, 0)
            };
        }

        if (!WaitForFrameAdvance(frame.PreparedFrameCount, requiredAdvancedFrames: 2, timeoutMs: 2500, out var renderedCamera))
        {
            return new
            {
                success = false,
                message = "Timed out waiting for the reframed camera to render.",
                requestedRect = CreateMapCellRectPayload(frame.RequestedRect),
                paddedRect = CreateMapCellRectPayload(frame.PaddedRect),
                paddingCells = frame.PaddingCells,
                fullyVisible = frame.FullyVisible,
                appliedRootSize = frame.AppliedRootSize,
                preparedFrameCount = frame.PreparedFrameCount,
                camera = frame.Camera,
                renderedCamera
            };
        }

        return new
        {
            success = true,
            requestedRect = CreateMapCellRectPayload(frame.RequestedRect),
            paddedRect = CreateMapCellRectPayload(frame.PaddedRect),
            paddingCells = frame.PaddingCells,
            fullyVisible = frame.FullyVisible,
            appliedRootSize = frame.AppliedRootSize,
            preparedFrameCount = frame.PreparedFrameCount,
            camera = renderedCamera
        };
    }

    public object ScreenshotCellRect(
        int x,
        int z,
        int width = 1,
        int height = 1,
        int paddingCells = DefaultScreenshotCellPadding,
        string fileName = null,
        bool includeTargets = true,
        bool suppressMessage = true,
        bool doNotResetCamera = false)
    {
        CameraSnapshot cameraBefore = null;
        CellRectFrameResult frame = null;
        ScreenshotCaptureResult capture = null;
        object cameraRestored = null;
        object cameraAfterCapture = null;
        var cameraWasReset = false;
        string restoreError = string.Empty;

        try
        {
            cameraBefore = RimBridgeMainThread.Invoke(CaptureCameraSnapshot, timeoutMs: 5000);
            RimBridgeMainThread.Invoke(ApplyScreenshotCameraConfig, timeoutMs: 5000);
            frame = RimBridgeMainThread.Invoke(() => FrameCellRectForScreenshot(x, z, width, height, paddingCells), timeoutMs: 5000);
            if (!frame.Success)
            {
                return new
                {
                    success = false,
                    message = frame.ErrorMessage,
                    requestedRect = CreateRequestedRectPayload(x, z, width, height),
                    paddingCells = Math.Max(paddingCells, 0),
                    cameraBefore = cameraBefore?.Payload
                };
            }

            if (!WaitForFrameAdvance(frame.PreparedFrameCount, requiredAdvancedFrames: 2, timeoutMs: 2500, out var renderedCamera))
            {
                return new
                {
                    success = false,
                    message = "Timed out waiting for the reframed camera to render before capturing the screenshot.",
                    requestedRect = CreateMapCellRectPayload(frame.RequestedRect),
                    paddedRect = CreateMapCellRectPayload(frame.PaddedRect),
                    paddingCells = frame.PaddingCells,
                    fullyVisible = frame.FullyVisible,
                    appliedRootSize = frame.AppliedRootSize,
                    preparedFrameCount = frame.PreparedFrameCount,
                    cameraBefore = cameraBefore?.Payload,
                    cameraDuringCapture = frame.Camera,
                    renderedCamera
                };
            }

            capture = CaptureScreenshotInternal(fileName, includeTargets, suppressMessage, clipTargetId: null, clipPadding: 0);
        }
        finally
        {
            var shouldRestoreCamera = cameraBefore != null && (!doNotResetCamera || frame == null || !frame.Success);
            if (shouldRestoreCamera)
            {
                try
                {
                    cameraRestored = RimBridgeMainThread.Invoke(() =>
                    {
                        RestoreCamera(cameraBefore);
                        return RimWorldState.DescribeCamera();
                    }, timeoutMs: 5000);
                    cameraWasReset = true;
                }
                catch (Exception ex)
                {
                    restoreError = ex.Message;
                }
            }
            else if (frame?.Success == true)
            {
                cameraAfterCapture = RimBridgeMainThread.Invoke(RimWorldState.DescribeCamera, timeoutMs: 5000);
            }
        }

        if (!string.IsNullOrWhiteSpace(restoreError))
        {
            return new
            {
                success = false,
                message = capture?.Success == true
                    ? $"Captured the screenshot but failed to restore the camera: {restoreError}"
                    : $"Failed to restore the camera after the screenshot attempt: {restoreError}",
                captureSucceeded = capture?.Success == true,
                path = capture?.Path,
                sourcePath = string.IsNullOrWhiteSpace(capture?.SourcePath) ? null : capture.SourcePath,
                fileName = capture?.FileName,
                requestedFileName = capture?.RequestedFileName,
                screenshotFolder = capture?.ScreenshotFolder,
                sizeBytes = capture?.SizeBytes,
                capturedAtUtc = capture?.CapturedAtUtc,
                doNotResetCamera,
                cameraWasReset,
                requestedRect = CreateMapCellRectPayload(frame?.RequestedRect),
                paddedRect = CreateMapCellRectPayload(frame?.PaddedRect),
                paddingCells = frame?.PaddingCells ?? Math.Max(paddingCells, 0),
                fullyVisible = frame?.FullyVisible ?? false,
                appliedRootSize = frame?.AppliedRootSize,
                cameraBefore = cameraBefore?.Payload,
                cameraDuringCapture = frame?.Camera,
                cameraAfterCapture
            };
        }

        if (capture == null)
        {
            return new
            {
                success = false,
                message = "The screenshot capture did not complete.",
                doNotResetCamera,
                cameraWasReset,
                requestedRect = CreateMapCellRectPayload(frame?.RequestedRect),
                paddedRect = CreateMapCellRectPayload(frame?.PaddedRect),
                paddingCells = frame?.PaddingCells ?? Math.Max(paddingCells, 0),
                fullyVisible = frame?.FullyVisible ?? false,
                appliedRootSize = frame?.AppliedRootSize,
                cameraBefore = cameraBefore?.Payload,
                cameraDuringCapture = frame?.Camera,
                cameraAfterCapture,
                cameraRestored
            };
        }

        if (!capture.Success)
        {
            return new
            {
                success = false,
                message = capture.Message,
                path = string.IsNullOrWhiteSpace(capture.Path) ? null : capture.Path,
                sourcePath = string.IsNullOrWhiteSpace(capture.SourcePath) ? null : capture.SourcePath,
                fileName = capture.FileName,
                requestedFileName = capture.RequestedFileName,
                screenshotFolder = capture.ScreenshotFolder,
                sizeBytes = capture.SizeBytes,
                capturedAtUtc = capture.CapturedAtUtc,
                suppressMessage = capture.SuppressMessage,
                expectedPath = string.IsNullOrWhiteSpace(capture.ExpectedPath) ? null : capture.ExpectedPath,
                doNotResetCamera,
                cameraWasReset,
                requestedRect = CreateMapCellRectPayload(frame?.RequestedRect),
                paddedRect = CreateMapCellRectPayload(frame?.PaddedRect),
                paddingCells = frame?.PaddingCells ?? Math.Max(paddingCells, 0),
                fullyVisible = frame?.FullyVisible ?? false,
                appliedRootSize = frame?.AppliedRootSize,
                cameraBefore = cameraBefore?.Payload,
                cameraDuringCapture = frame?.Camera,
                cameraAfterCapture,
                cameraRestored
            };
        }

        return new
        {
            success = true,
            path = capture.Path,
            sourcePath = null as string,
            fileName = capture.FileName,
            requestedFileName = capture.RequestedFileName,
            screenshotFolder = capture.ScreenshotFolder,
            sizeBytes = capture.SizeBytes,
            capturedAtUtc = capture.CapturedAtUtc,
            suppressMessage = capture.SuppressMessage,
            clipped = false,
            doNotResetCamera,
            cameraWasReset,
            requestedRect = CreateMapCellRectPayload(frame.RequestedRect),
            paddedRect = CreateMapCellRectPayload(frame.PaddedRect),
            paddingCells = frame.PaddingCells,
            fullyVisible = frame.FullyVisible,
            appliedRootSize = frame.AppliedRootSize,
            cameraBefore = cameraBefore?.Payload,
            cameraDuringCapture = frame.Camera,
            cameraAfterCapture,
            cameraRestored
        };
    }

    public object TakeScreenshot(string fileName = null, bool includeTargets = true, bool suppressMessage = true, string clipTargetId = null, int clipPadding = 8)
    {
        return CreateScreenshotResponse(CaptureScreenshotInternal(fileName, includeTargets, suppressMessage, clipTargetId, clipPadding));
    }

    private static ScreenshotCaptureResult CaptureScreenshotInternal(string fileName, bool includeTargets, bool suppressMessage, string clipTargetId, int clipPadding)
    {
        var safeName = RimWorldState.SanitizeName(fileName, "rimbridge");
        var capture = RimBridgeMainThread.Invoke(() =>
        {
            var screenTargets = includeTargets ? RimWorldTargeting.CreateScreenTargetsPayload() : null;
            RimWorldTargeting.ScreenTargetClipArea clipArea = null;
            if (!string.IsNullOrWhiteSpace(clipTargetId)
                && !RimWorldTargeting.TryResolveClipArea(clipTargetId, out clipArea, out var clipError))
            {
                return new PendingScreenshotCapture
                {
                    ErrorMessage = clipError
                };
            }

            var restoreSuppressMessage = ScreenshotTaker.suppressMessage;
            if (suppressMessage)
                ScreenshotTaker.suppressMessage = true;

            ScreenshotTaker.TakeNonSteamShot(safeName);
            var outputFileName = string.IsNullOrWhiteSpace(clipTargetId) ? safeName : safeName + "__clip";
            return new PendingScreenshotCapture
            {
                ExpectedPath = Path.Combine(GenFilePaths.ScreenshotFolderPath, safeName + ".png"),
                OutputPath = Path.Combine(GenFilePaths.ScreenshotFolderPath, outputFileName + ".png"),
                OutputFileName = outputFileName,
                ScreenTargets = screenTargets,
                RestoreSuppressMessage = restoreSuppressMessage,
                ClipTargetId = clipTargetId ?? string.Empty,
                ClipTargetKind = clipArea?.TargetKind ?? string.Empty,
                ClipTargetLabel = clipArea?.Label ?? string.Empty,
                ClipPadding = Math.Max(clipPadding, 0),
                ClipRect = clipArea?.Rect == null ? null : new UiRectSnapshot
                {
                    X = clipArea.Rect.X,
                    Y = clipArea.Rect.Y,
                    Width = clipArea.Rect.Width,
                    Height = clipArea.Rect.Height
                }
            };
        }, timeoutMs: 5000);

        if (!string.IsNullOrWhiteSpace(capture.ErrorMessage))
        {
            return new ScreenshotCaptureResult
            {
                Success = false,
                Message = capture.ErrorMessage
            };
        }

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(capture.ExpectedPath))
                {
                    var info = new FileInfo(capture.ExpectedPath);
                    if (info.Length > 0)
                    {
                        ScreenshotPixelRect? clipRect = null;
                        string clipPath = null;
                        if (!string.IsNullOrWhiteSpace(capture.ClipTargetId))
                        {
                            if (!TryCropScreenshot(capture.ExpectedPath, capture.ClipRect, capture.OutputPath, capture.ClipTargetId, capture.ClipPadding, out var clippedRect, out var clipError))
                            {
                                return new ScreenshotCaptureResult
                                {
                                    Success = false,
                                    Message = clipError,
                                    Path = capture.ExpectedPath,
                                    SourcePath = capture.ExpectedPath,
                                    FileName = safeName,
                                    RequestedFileName = safeName,
                                    ScreenshotFolder = GenFilePaths.ScreenshotFolderPath,
                                    SuppressMessage = suppressMessage
                                };
                            }

                            clipRect = clippedRect;
                            clipPath = capture.OutputPath;
                            info = new FileInfo(clipPath);
                        }

                        return new ScreenshotCaptureResult
                        {
                            Success = true,
                            Path = clipPath ?? capture.ExpectedPath,
                            SourcePath = clipPath == null ? string.Empty : capture.ExpectedPath,
                            FileName = clipPath == null ? safeName : capture.OutputFileName,
                            RequestedFileName = safeName,
                            ScreenshotFolder = GenFilePaths.ScreenshotFolderPath,
                            SizeBytes = info.Length,
                            CapturedAtUtc = DateTime.UtcNow,
                            SuppressMessage = suppressMessage,
                            Clipped = clipRect.HasValue,
                            ClipTargetId = capture.ClipTargetId,
                            ClipTargetKind = capture.ClipTargetKind,
                            ClipTargetLabel = capture.ClipTargetLabel,
                            ClipPadding = clipRect.HasValue ? capture.ClipPadding : 0,
                            ClipRect = clipRect,
                            ScreenTargets = capture.ScreenTargets
                        };
                    }
                }

                Thread.Sleep(100);
            }

            return new ScreenshotCaptureResult
            {
                Success = false,
                Message = "Timed out waiting for RimWorld to finish writing the screenshot file.",
                RequestedFileName = safeName,
                ScreenshotFolder = GenFilePaths.ScreenshotFolderPath,
                SuppressMessage = suppressMessage,
                ExpectedPath = capture.ExpectedPath
            };
        }
        finally
        {
            if (suppressMessage)
            {
                RimBridgeMainThread.Invoke(() =>
                {
                    ScreenshotTaker.suppressMessage = capture.RestoreSuppressMessage;
                }, timeoutMs: 5000);
            }
        }
    }

    private static object CreateScreenshotResponse(ScreenshotCaptureResult capture)
    {
        return new
        {
            success = capture.Success,
            message = string.IsNullOrWhiteSpace(capture.Message) ? null : capture.Message,
            path = string.IsNullOrWhiteSpace(capture.Path) ? null : capture.Path,
            sourcePath = string.IsNullOrWhiteSpace(capture.SourcePath) ? null : capture.SourcePath,
            fileName = string.IsNullOrWhiteSpace(capture.FileName) ? null : capture.FileName,
            requestedFileName = string.IsNullOrWhiteSpace(capture.RequestedFileName) ? null : capture.RequestedFileName,
            screenshotFolder = string.IsNullOrWhiteSpace(capture.ScreenshotFolder) ? null : capture.ScreenshotFolder,
            sizeBytes = capture.SizeBytes,
            capturedAtUtc = capture.CapturedAtUtc,
            suppressMessage = capture.SuppressMessage,
            clipped = capture.Clipped,
            clipTargetId = string.IsNullOrWhiteSpace(capture.ClipTargetId) ? null : capture.ClipTargetId,
            clipTargetKind = string.IsNullOrWhiteSpace(capture.ClipTargetKind) ? null : capture.ClipTargetKind,
            clipTargetLabel = string.IsNullOrWhiteSpace(capture.ClipTargetLabel) ? null : capture.ClipTargetLabel,
            clipPadding = capture.ClipPadding,
            clipRect = capture.ClipRect.HasValue
                ? new
                {
                    x = capture.ClipRect.Value.X,
                    y = capture.ClipRect.Value.Y,
                    width = capture.ClipRect.Value.Width,
                    height = capture.ClipRect.Value.Height
                }
                : null,
            screenTargets = capture.ScreenTargets,
            expectedPath = string.IsNullOrWhiteSpace(capture.ExpectedPath) ? null : capture.ExpectedPath
        };
    }

    private static CameraSnapshot CaptureCameraSnapshot()
    {
        var driver = Find.CameraDriver;
        return new CameraSnapshot
        {
            MapPosition = driver.MapPosition,
            RootSize = driver.RootSize,
            SizeRange = driver.config.sizeRange,
            Payload = RimWorldState.DescribeCamera()
        };
    }

    private static void RestoreCamera(CameraSnapshot snapshot)
    {
        Find.CameraDriver.config.sizeRange = snapshot.SizeRange;
        Find.CameraDriver.SetRootPosAndSize(new Vector3(snapshot.MapPosition.x, 0f, snapshot.MapPosition.z), snapshot.RootSize);
    }

    private static void ApplyScreenshotCameraConfig()
    {
        var driver = Find.CameraDriver;
        var range = driver.config.sizeRange;
        if (range.min > MinimumScreenshotCameraRootSize)
            range.min = MinimumScreenshotCameraRootSize;
        if (range.max < range.min + 1f)
            range.max = range.min + 1f;
        driver.config.sizeRange = range;
    }

    private static CellRectFrameResult FrameCellRectForScreenshot(int x, int z, int width, int height, int paddingCells)
    {
        var requestedRect = CreateRequestedRectSnapshot(x, z, width, height);
        var normalizedPadding = Math.Max(paddingCells, 0);
        if (width <= 0 || height <= 0)
        {
            return new CellRectFrameResult
            {
                Success = false,
                ErrorMessage = "width and height must both be greater than zero.",
                RequestedRect = requestedRect,
                PaddingCells = normalizedPadding
            };
        }

        var map = RimWorldState.CurrentMapOrThrow();
        if (x < 0 || z < 0 || requestedRect.MaxX >= map.Size.x || requestedRect.MaxZ >= map.Size.z)
        {
            return new CellRectFrameResult
            {
                Success = false,
                ErrorMessage = $"Requested rect ({x}, {z}, {width}, {height}) is out of bounds for the current map.",
                RequestedRect = requestedRect,
                PaddingCells = normalizedPadding
            };
        }

        var paddedRect = new MapCellRectSnapshot
        {
            X = Math.Max(0, x - normalizedPadding),
            Z = Math.Max(0, z - normalizedPadding),
            MaxX = Math.Min(map.Size.x - 1, requestedRect.MaxX + normalizedPadding),
            MaxZ = Math.Min(map.Size.z - 1, requestedRect.MaxZ + normalizedPadding)
        };
        paddedRect.Width = paddedRect.MaxX - paddedRect.X + 1;
        paddedRect.Height = paddedRect.MaxZ - paddedRect.Z + 1;

        var center = new Vector3(
            (paddedRect.X + paddedRect.MaxX + 1) / 2f,
            0f,
            (paddedRect.Z + paddedRect.MaxZ + 1) / 2f);
        var driver = Find.CameraDriver;
        var fittedRootSize = ComputeRootSizeForCellRect(paddedRect);
        driver.SetRootPosAndSize(center, fittedRootSize);

        return new CellRectFrameResult
        {
            Success = true,
            RequestedRect = requestedRect,
            PaddedRect = paddedRect,
            PaddingCells = normalizedPadding,
            FullyVisible = DoesRootSizeContainRect(paddedRect, driver.RootSize),
            AppliedRootSize = driver.RootSize,
            PreparedFrameCount = Time.frameCount,
            Camera = RimWorldState.DescribeCamera()
        };
    }

    private static bool WaitForFrameAdvance(int preparedFrameCount, int requiredAdvancedFrames, int timeoutMs, out object renderedCamera)
    {
        renderedCamera = null;
        var requiredFrame = preparedFrameCount + Math.Max(requiredAdvancedFrames, 1);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs <= 0 ? 2500 : timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            var snapshot = RimBridgeMainThread.Invoke(() => new
            {
                frameCount = Time.frameCount,
                camera = RimWorldState.DescribeCamera()
            }, timeoutMs: 5000);

            renderedCamera = snapshot.camera;
            if (snapshot.frameCount >= requiredFrame)
                return true;

            Thread.Sleep(50);
        }

        return false;
    }

    private static float ComputeRootSizeForCellRect(MapCellRectSnapshot rect)
    {
        var screenHeight = Math.Max(UI.screenHeight, 1);
        var aspect = Mathf.Max((float)UI.screenWidth / screenHeight, 0.01f);
        var requiredForHeight = rect.Height / 2f;
        var requiredForWidth = rect.Width / (2f * aspect);
        return Mathf.Clamp(Mathf.Max(requiredForHeight, requiredForWidth), MinimumScreenshotCameraRootSize, MaximumCameraRootSize);
    }

    private static bool DoesRootSizeContainRect(MapCellRectSnapshot rect, float rootSize)
    {
        return rootSize + 0.0001f >= ComputeRootSizeForCellRect(rect);
    }

    private static MapCellRectSnapshot CreateRequestedRectSnapshot(int x, int z, int width, int height)
    {
        return new MapCellRectSnapshot
        {
            X = x,
            Z = z,
            Width = width,
            Height = height,
            MaxX = width <= 0 ? x - 1 : x + width - 1,
            MaxZ = height <= 0 ? z - 1 : z + height - 1
        };
    }

    private static object CreateRequestedRectPayload(int x, int z, int width, int height)
    {
        return CreateMapCellRectPayload(CreateRequestedRectSnapshot(x, z, width, height));
    }

    private static object CreateMapCellRectPayload(CellRect rect)
    {
        return CreateMapCellRectPayload(new MapCellRectSnapshot
        {
            X = rect.minX,
            Z = rect.minZ,
            Width = rect.Width,
            Height = rect.Height,
            MaxX = rect.maxX,
            MaxZ = rect.maxZ
        });
    }

    private static object CreateMapCellRectPayload(MapCellRectSnapshot rect)
    {
        if (rect == null)
            return null;

        return new
        {
            x = rect.X,
            z = rect.Z,
            width = rect.Width,
            height = rect.Height,
            maxX = rect.MaxX,
            maxZ = rect.MaxZ
        };
    }

    private static bool TryCropScreenshot(
        string sourcePath,
        UiRectSnapshot clipArea,
        string outputPath,
        string clipTargetId,
        int clipPadding,
        out ScreenshotPixelRect clipRect,
        out string error)
    {
        if (!File.Exists(sourcePath))
        {
            clipRect = default(ScreenshotPixelRect);
            error = $"Screenshot source '{sourcePath}' does not exist.";
            return false;
        }

        var cropResult = RimBridgeMainThread.Invoke(() =>
        {
            if (clipArea == null)
            {
                return new ScreenshotCropResult
                {
                    Success = false,
                    ErrorMessage = $"Target id '{clipTargetId}' did not resolve to a valid clip rect."
                };
            }

            var bytes = File.ReadAllBytes(sourcePath);
            var sourceTexture = new Texture2D(2, 2, TextureFormat.ARGB32, mipChain: false);
            Texture2D clippedTexture = null;

            try
            {
                if (!sourceTexture.LoadImage(bytes))
                {
                    return new ScreenshotCropResult
                    {
                        Success = false,
                        ErrorMessage = $"RimWorld could not decode screenshot '{sourcePath}'."
                    };
                }

                if (!ScreenshotClipMath.TryCreatePixelRect(
                        clipArea.X,
                        clipArea.Y,
                        clipArea.Width,
                        clipArea.Height,
                        UI.screenWidth,
                        UI.screenHeight,
                        sourceTexture.width,
                        sourceTexture.height,
                        clipPadding,
                        out var computedClipRect))
                {
                    return new ScreenshotCropResult
                    {
                        Success = false,
                        ErrorMessage = $"Target id '{clipTargetId}' did not resolve to a valid clip rect."
                    };
                }

                var colors = sourceTexture.GetPixels(
                    computedClipRect.X,
                    computedClipRect.BottomLeftY(sourceTexture.height),
                    computedClipRect.Width,
                    computedClipRect.Height);
                clippedTexture = new Texture2D(computedClipRect.Width, computedClipRect.Height, TextureFormat.ARGB32, mipChain: false);
                clippedTexture.SetPixels(colors);
                clippedTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                File.WriteAllBytes(outputPath, clippedTexture.EncodeToPNG());
                return new ScreenshotCropResult
                {
                    Success = true,
                    ClipRect = computedClipRect
                };
            }
            finally
            {
                UnityEngine.Object.Destroy(sourceTexture);
                if (clippedTexture != null)
                    UnityEngine.Object.Destroy(clippedTexture);
            }
        }, timeoutMs: 5000);

        clipRect = cropResult.ClipRect;
        error = cropResult.ErrorMessage;
        return cropResult.Success;
    }
}
