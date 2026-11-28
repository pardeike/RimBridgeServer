using System.Collections.Generic;
using System.Linq;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class PlanShapesTests
{
    [Fact]
    public void ClassifiesEmptyAndFilledRectangles()
    {
        Assert.Equal("empty", PlanShapes.Classify([]));
        Assert.Equal("filled_rectangle", PlanShapes.Classify([(0, 0)]));
        Assert.Equal("filled_rectangle", PlanShapes.Classify(Rectangle(0, 0, 3, 2)));
    }

    [Fact]
    public void ClassifiesFullBorderAsOutline()
    {
        var outline = Rectangle(0, 0, 4, 3).Where(cell => cell.X == 0 || cell.X == 3 || cell.Z == 0 || cell.Z == 2).ToList();

        Assert.Equal("rectangle_outline", PlanShapes.Classify(outline));
    }

    [Fact]
    public void ClassifiesShapesThatOnlyTouchTheBorderAsIrregular()
    {
        Assert.Equal("irregular", PlanShapes.Classify([(0, 0), (5, 5)]));
        Assert.Equal("irregular", PlanShapes.Classify([(0, 0), (1, 0), (0, 1)]));
        var openOutline = Rectangle(0, 0, 4, 4).Where(cell => cell.X == 0 || cell.X == 3 || cell.Z == 0 || cell.Z == 3).Where(cell => cell != (3, 3)).ToList();
        Assert.Equal("irregular", PlanShapes.Classify(openOutline));
    }

    private static List<(int X, int Z)> Rectangle(int x, int z, int width, int height)
    {
        return Enumerable.Range(x, width).SelectMany(cellX => Enumerable.Range(z, height).Select(cellZ => (cellX, cellZ))).ToList();
    }
}
