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

    public object GetCameraState()
    {
        return RimWorldState.DescribeCamera();
    }

    public object GetScreenTargets()
    {
        return RimWorldTargeting.GetScreenTargetsResponse();
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
        var newSize = Mathf.Clamp(driver.RootSize + delta, 8f, 140f);
        driver.SetRootSize(newSize);

        return new { success = true, rootSize = driver.RootSize, camera = RimWorldState.DescribeCamera() };
    }

    public object SetCameraZoom(float rootSize)
    {
        var driver = Find.CameraDriver;
        driver.SetRootSize(Mathf.Clamp(rootSize, 8f, 140f));

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

    public object TakeScreenshot(string fileName = null, bool includeTargets = true, bool suppressMessage = true, string clipTargetId = null, int clipPadding = 8)
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
            return new
            {
                success = false,
                message = capture.ErrorMessage
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
                                return new
                                {
                                    success = false,
                                    message = clipError,
                                    path = capture.ExpectedPath,
                                    sourcePath = capture.ExpectedPath,
                                    fileName = safeName
                                };
                            }

                            clipRect = clippedRect;
                            clipPath = capture.OutputPath;
                            info = new FileInfo(clipPath);
                        }

                        return new
                        {
                            success = true,
                            path = clipPath ?? capture.ExpectedPath,
                            sourcePath = string.IsNullOrWhiteSpace(clipPath) ? null : capture.ExpectedPath,
                            fileName = string.IsNullOrWhiteSpace(clipPath) ? safeName : capture.OutputFileName,
                            requestedFileName = safeName,
                            screenshotFolder = GenFilePaths.ScreenshotFolderPath,
                            sizeBytes = info.Length,
                            capturedAtUtc = DateTime.UtcNow,
                            suppressMessage,
                            clipped = clipRect.HasValue,
                            clipTargetId = string.IsNullOrWhiteSpace(capture.ClipTargetId) ? null : capture.ClipTargetId,
                            clipTargetKind = string.IsNullOrWhiteSpace(capture.ClipTargetKind) ? null : capture.ClipTargetKind,
                            clipTargetLabel = string.IsNullOrWhiteSpace(capture.ClipTargetLabel) ? null : capture.ClipTargetLabel,
                            clipPadding = clipRect.HasValue ? capture.ClipPadding : 0,
                            clipRect = clipRect.HasValue ? new
                            {
                                x = clipRect.Value.X,
                                y = clipRect.Value.Y,
                                width = clipRect.Value.Width,
                                height = clipRect.Value.Height
                            } : null,
                            screenTargets = capture.ScreenTargets
                        };
                    }
                }

                Thread.Sleep(100);
            }

            return new
            {
                success = false,
                message = "Timed out waiting for RimWorld to finish writing the screenshot file.",
                expectedPath = capture.ExpectedPath,
                suppressMessage
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
