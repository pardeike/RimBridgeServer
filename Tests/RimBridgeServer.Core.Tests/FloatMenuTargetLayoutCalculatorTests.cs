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
        Assert.Equal(164f, rects[2].X);
        Assert.Equal(37f, rects[2].Y);
    }

    [Fact]
    public void WrapsLikeTheGameWhenTheOverlapLetsAnOptionFit()
    {
        // FloatMenu.DoWindowContents wraps when y + height - 1 > maxViewHeight. Three options of 30 then fit in 88.
        var rects = FloatMenuTargetLayoutCalculator.Compute(new FloatMenuTargetLayoutRequest
        {
            ColumnWidth = 100f,
            ColumnCount = 2,
            MaxViewHeight = 88f,
            OptionHeights = [30f, 30f, 30f, 30f]
        });

        Assert.Equal([0, 0, 0, 1], rects.Select(rect => rect.ColumnIndex).ToArray());
        Assert.Equal(58f, rects[2].Y);
        Assert.Equal(99f, rects[3].X);
    }

    [Fact]
    public void ShiftsOptionsByTheScrollPosition()
    {
        var rects = FloatMenuTargetLayoutCalculator.Compute(new FloatMenuTargetLayoutRequest
        {
            WindowX = 10f,
            WindowY = 10f,
            ColumnWidth = 100f,
            MaxViewHeight = 1000f,
            ScrollY = 40f,
            OptionHeights = [30f, 30f]
        });

        Assert.Equal(-30f, rects[0].Y);
        Assert.Equal(-1f, rects[1].Y);
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
