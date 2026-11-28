using System.Collections.Generic;
using System.Linq;

namespace RimBridgeServer.Core;

public static class PlanShapes
{
    // Classifies the distinct cells of a plan. A rectangle outline is the full border of a bounding box that is at
    // least 3 by 3, with no inner cell. Two separate cells or an L shape touch the box border too, but are irregular.
    public static string Classify(IReadOnlyCollection<(int X, int Z)> cells)
    {
        if (cells == null || cells.Count == 0)
            return "empty";

        var minX = cells.Min(cell => cell.X);
        var maxX = cells.Max(cell => cell.X);
        var minZ = cells.Min(cell => cell.Z);
        var maxZ = cells.Max(cell => cell.Z);
        var width = maxX - minX + 1;
        var height = maxZ - minZ + 1;
        if (cells.Count == width * height)
            return "filled_rectangle";

        var allOnBorder = cells.All(cell => cell.X == minX || cell.X == maxX || cell.Z == minZ || cell.Z == maxZ);
        var borderCellCount = 2 * (width + height) - 4;
        return width >= 3 && height >= 3 && allOnBorder && cells.Count == borderCellCount
            ? "rectangle_outline"
            : "irregular";
    }
}
