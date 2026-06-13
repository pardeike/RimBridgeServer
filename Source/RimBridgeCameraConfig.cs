using System;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgeCameraConfig
{
    private const float MinimumCameraConfigZoom = 0f;
    private const float MaximumCameraConfigZoom = 100f;

    private static readonly FloatRange FullZoomRange = new(MinimumCameraConfigZoom, MaximumCameraConfigZoom);
    private static bool _loggedApplied;
    private static bool _loggedFailure;

    public static void KeepFullZoomRange()
    {
        try
        {
            var config = Find.CameraDriver?.config;
            if (config == null || IsFullZoomRange(config.sizeRange))
                return;

            config.sizeRange = FullZoomRange;
            if (_loggedApplied == false)
            {
                _loggedApplied = true;
                Log.Message("[RimBridge] Camera zoom range set to RimWorld's full editor range (0..100).");
            }
        }
        catch (Exception ex)
        {
            if (_loggedFailure)
                return;

            _loggedFailure = true;
            Log.Warning($"[RimBridge] Could not apply full camera zoom range: {ex}");
        }
    }

    private static bool IsFullZoomRange(FloatRange range)
    {
        return Mathf.Approximately(range.min, MinimumCameraConfigZoom)
            && Mathf.Approximately(range.max, MaximumCameraConfigZoom);
    }
}
