using System;

namespace RimBridgeServer.Core;

public readonly struct ScreenshotPixelRect
{
    public ScreenshotPixelRect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public int BottomLeftY(int imageHeight)
    {
        return imageHeight - Y - Height;
    }
}

public static class ScreenshotClipMath
{
    public static bool TryCreatePixelRect(
        float logicalX,
        float logicalY,
        float logicalWidth,
        float logicalHeight,
        float logicalScreenWidth,
        float logicalScreenHeight,
        int imageWidth,
        int imageHeight,
        int logicalPadding,
        out ScreenshotPixelRect clipRect)
    {
        clipRect = default(ScreenshotPixelRect);
        if (logicalWidth <= 0f
            || logicalHeight <= 0f
            || logicalScreenWidth <= 0f
            || logicalScreenHeight <= 0f
            || imageWidth <= 0
            || imageHeight <= 0)
        {
            return false;
        }

        var scaleX = imageWidth / logicalScreenWidth;
        var scaleY = imageHeight / logicalScreenHeight;
        if (scaleX <= 0f || scaleY <= 0f)
            return false;

        var padding = Math.Max(logicalPadding, 0);
        var padX = padding * scaleX;
        var padY = padding * scaleY;

        var left = (int)Math.Floor((logicalX * scaleX) - padX);
        var top = (int)Math.Floor((logicalY * scaleY) - padY);
        var right = (int)Math.Ceiling(((logicalX + logicalWidth) * scaleX) + padX);
        var bottom = (int)Math.Ceiling(((logicalY + logicalHeight) * scaleY) + padY);

        left = Math.Max(0, left);
        top = Math.Max(0, top);
        right = Math.Min(imageWidth, right);
        bottom = Math.Min(imageHeight, bottom);

        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
            return false;

        clipRect = new ScreenshotPixelRect(left, top, width, height);
        return true;
    }
}
