using System.Linq;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class FloatMenuTargetLayoutCalculatorTests
{
    [Fact]
    public void ComputesSingleColumnRectsFromWindowOriginAndMargin()
    {
        var rects = FloatMenuTargetLayoutCalculator.Compute(new FloatMenuTargetLayoutRequest
        {
            WindowX = 100f,
            WindowY = 200f,
            Margin = 18f,
            ColumnWidth = 300f,
            MaxViewHeight = 500f,
            OptionHeights = [30f, 45f, 20f]
        });

        Assert.Equal(3, rects.Count);
        Assert.All(rects, rect => Assert.Equal(0, rect.ColumnIndex));
        Assert.Equal(118f, rects[0].X);
        Assert.Equal(218f, rects[0].Y);
        Assert.Equal(300f, rects[0].Width);
        Assert.Equal(30f, rects[0].Height);
        Assert.Equal(247f, rects[1].Y);
        Assert.Equal(291f, rects[2].Y);
    }

    [Fact]
    public void WrapsIntoNextColumnWhenViewHeightWouldOverflow()
    {
        var rects = FloatMenuTargetLayoutCalculator.Compute(new FloatMenuTargetLayoutRequest
        {
            WindowX = 10f,
            WindowY = 20f,
            Margin = 5f,
            TitleHeight = 12f,
            ColumnWidth = 150f,
            ColumnCount = 2,
            MaxViewHeight = 80f,
            OptionHeights = [30f, 35f, 40f]
        });

        Assert.Equal(3, rects.Count);
        Assert.Equal(0, rects[0].ColumnIndex);
        Assert.Equal(15f, rects[0].X);
        Assert.Equal(37f, rects[0].Y);
        Assert.Equal(0, rects[1].ColumnIndex);
        Assert.Equal(66f, rects[1].Y);
        Assert.Equal(1, rects[2].ColumnIndex);
        Assert.Equal(165f, rects[2].X);
        Assert.Equal(37f, rects[2].Y);
    }

    [Fact]
    public void ReturnsEmptyWhenNoOptionsAreSupplied()
    {
        var rects = FloatMenuTargetLayoutCalculator.Compute(new FloatMenuTargetLayoutRequest
        {
            ColumnWidth = 200f,
            MaxViewHeight = 100f,
            OptionHeights = []
        });

        Assert.Empty(rects);
    }
}
