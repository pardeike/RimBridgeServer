using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class ScreenshotClipMathTests
{
    [Fact]
    public void CreatesPixelRectWithoutScaling()
    {
        var success = ScreenshotClipMath.TryCreatePixelRect(
            logicalX: 10f,
            logicalY: 20f,
            logicalWidth: 30f,
            logicalHeight: 40f,
            logicalScreenWidth: 200f,
            logicalScreenHeight: 100f,
            imageWidth: 200,
            imageHeight: 100,
            logicalPadding: 0,
            out var rect);

        Assert.True(success);
        Assert.Equal(10, rect.X);
        Assert.Equal(20, rect.Y);
        Assert.Equal(30, rect.Width);
        Assert.Equal(40, rect.Height);
        Assert.Equal(40, rect.BottomLeftY(100));
    }

    [Fact]
    public void ScalesLogicalRectIntoRetinaImagePixels()
    {
        var success = ScreenshotClipMath.TryCreatePixelRect(
            logicalX: 10f,
            logicalY: 12f,
            logicalWidth: 25f,
            logicalHeight: 10f,
            logicalScreenWidth: 100f,
            logicalScreenHeight: 50f,
            imageWidth: 200,
            imageHeight: 100,
            logicalPadding: 0,
            out var rect);

        Assert.True(success);
        Assert.Equal(20, rect.X);
        Assert.Equal(24, rect.Y);
        Assert.Equal(50, rect.Width);
        Assert.Equal(20, rect.Height);
    }

    [Fact]
    public void AppliesPaddingAndClampsToImageBounds()
    {
        var success = ScreenshotClipMath.TryCreatePixelRect(
            logicalX: 2f,
            logicalY: 3f,
            logicalWidth: 5f,
            logicalHeight: 4f,
            logicalScreenWidth: 20f,
            logicalScreenHeight: 20f,
            imageWidth: 20,
            imageHeight: 20,
            logicalPadding: 10,
            out var rect);

        Assert.True(success);
        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(17, rect.Width);
        Assert.Equal(17, rect.Height);
    }

    [Fact]
    public void RejectsEmptyRects()
    {
        var success = ScreenshotClipMath.TryCreatePixelRect(
            logicalX: 10f,
            logicalY: 10f,
            logicalWidth: 0f,
            logicalHeight: 5f,
            logicalScreenWidth: 100f,
            logicalScreenHeight: 100f,
            imageWidth: 100,
            imageHeight: 100,
            logicalPadding: 0,
            out _);

        Assert.False(success);
    }
}
