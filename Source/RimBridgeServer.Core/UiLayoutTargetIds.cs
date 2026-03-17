using System;

namespace RimBridgeServer.Core;

public enum UiLayoutTargetKind
{
    Unknown = 0,
    Surface = 1,
    Element = 2
}

public sealed class UiLayoutTargetReference
{
    public string TargetId { get; set; } = string.Empty;

    public UiLayoutTargetKind Kind { get; set; }

    public int CaptureId { get; set; }

    public int SurfaceIndex { get; set; }

    public int ElementIndex { get; set; }
}

public static class UiLayoutTargetIds
{
    public static string CreateSurfaceTargetId(int captureId, int surfaceIndex)
    {
        if (captureId <= 0)
            throw new ArgumentOutOfRangeException(nameof(captureId));
        if (surfaceIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceIndex));

        return "ui-surface:" + captureId + ":" + surfaceIndex;
    }

    public static string CreateElementTargetId(int captureId, int surfaceIndex, int elementIndex)
    {
        if (captureId <= 0)
            throw new ArgumentOutOfRangeException(nameof(captureId));
        if (surfaceIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceIndex));
        if (elementIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(elementIndex));

        return "ui-element:" + captureId + ":" + surfaceIndex + ":" + elementIndex;
    }

    public static bool TryParse(string targetId, out UiLayoutTargetReference target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        var segments = targetId.Split(':');
        if (segments.Length == 3
            && string.Equals(segments[0], "ui-surface", StringComparison.Ordinal)
            && int.TryParse(segments[1], out var captureId)
            && int.TryParse(segments[2], out var surfaceIndex)
            && captureId > 0
            && surfaceIndex > 0)
        {
            target = new UiLayoutTargetReference
            {
                TargetId = targetId,
                Kind = UiLayoutTargetKind.Surface,
                CaptureId = captureId,
                SurfaceIndex = surfaceIndex
            };
            return true;
        }

        if (segments.Length == 4
            && string.Equals(segments[0], "ui-element", StringComparison.Ordinal)
            && int.TryParse(segments[1], out captureId)
            && int.TryParse(segments[2], out surfaceIndex)
            && int.TryParse(segments[3], out var elementIndex)
            && captureId > 0
            && surfaceIndex > 0
            && elementIndex > 0)
        {
            target = new UiLayoutTargetReference
            {
                TargetId = targetId,
                Kind = UiLayoutTargetKind.Element,
                CaptureId = captureId,
                SurfaceIndex = surfaceIndex,
                ElementIndex = elementIndex
            };
            return true;
        }

        return false;
    }
}
