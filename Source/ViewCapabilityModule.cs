using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class ViewCapabilityModule
{
    private sealed class PendingScreenshotCapture
    {
        public string ExpectedPath { get; set; } = string.Empty;

        public object ScreenTargets { get; set; }
    }

    public object GetCameraState()
    {
        return RimWorldState.DescribeCamera();
    }

    public object GetScreenTargets()
    {
        return RimWorldTargeting.GetScreenTargetsResponse();
    }

    public object JumpCameraToPawn(string pawnName)
    {
        var pawn = RimWorldState.ResolveCurrentMapPawn(pawnName);
        Find.CameraDriver.JumpToCurrentMapLoc(pawn.Position);
        Find.Selector.ClearSelection();
        Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
        return new
        {
            success = true,
            target = pawn.Name?.ToStringShort ?? pawn.LabelShort,
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

    public object FramePawns(string pawnNamesCsv = null)
    {
        List<Pawn> pawns;
        if (string.IsNullOrWhiteSpace(pawnNamesCsv))
        {
            pawns = Find.Selector.SelectedPawns.Where(pawn => pawn.Spawned).ToList();
        }
        else
        {
            pawns = RimWorldState.ParseNames(pawnNamesCsv)
                .Select(RimWorldState.ResolveCurrentMapPawn)
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
            rootSize = Find.CameraDriver.RootSize,
            camera = RimWorldState.DescribeCamera()
        };
    }

    public object TakeScreenshot(string fileName = null, bool includeTargets = true)
    {
        var safeName = RimWorldState.SanitizeName(fileName, "rimbridge");
        var capture = RimBridgeMainThread.Invoke(() =>
        {
            var screenTargets = includeTargets ? RimWorldTargeting.CreateScreenTargetsPayload() : null;
            ScreenshotTaker.TakeNonSteamShot(safeName);
            return new PendingScreenshotCapture
            {
                ExpectedPath = Path.Combine(GenFilePaths.ScreenshotFolderPath, safeName + ".png"),
                ScreenTargets = screenTargets
            };
        }, timeoutMs: 5000);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(capture.ExpectedPath))
            {
                var info = new FileInfo(capture.ExpectedPath);
                if (info.Length > 0)
                {
                    return new
                    {
                        success = true,
                        path = capture.ExpectedPath,
                        fileName = safeName,
                        screenshotFolder = GenFilePaths.ScreenshotFolderPath,
                        sizeBytes = info.Length,
                        capturedAtUtc = DateTime.UtcNow,
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
            expectedPath = capture.ExpectedPath
        };
    }
}
