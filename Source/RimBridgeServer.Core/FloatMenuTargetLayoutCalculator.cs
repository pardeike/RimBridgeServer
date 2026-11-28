using System;
using System.Collections.Generic;
using System.Linq;

namespace RimBridgeServer.Core;

public sealed class FloatMenuTargetLayoutRequest
{
    public float WindowX { get; set; }

    public float WindowY { get; set; }

    public float Margin { get; set; }

    public float TitleHeight { get; set; }

    public float ColumnWidth { get; set; }

    public float MaxViewHeight { get; set; }

    public float OptionSpacing { get; set; } = -1f;

    public int ColumnCount { get; set; } = 1;

    // FloatMenu.scrollPosition. A long menu with a scroll bar draws its options shifted by it.
    public float ScrollX { get; set; }

    public float ScrollY { get; set; }

    public IReadOnlyList<float> OptionHeights { get; set; } = [];
}

public sealed class FloatMenuTargetRect
{
    public int Index { get; set; }

    public int ColumnIndex { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }
}

public static class FloatMenuTargetLayoutCalculator
{
    public static IReadOnlyList<FloatMenuTargetRect> Compute(FloatMenuTargetLayoutRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var optionHeights = request.OptionHeights ?? [];
        if (optionHeights.Count == 0)
            return [];

        // Mirrors FloatMenu.DoWindowContents: an option starts a new column when y + height + spacing exceeds the view
        // height, and the next column starts at columnWidth + spacing. The spacing is -1, so options overlap by a pixel.
        var columnWidth = Math.Max(request.ColumnWidth, 0f);
        var maxViewHeight = request.MaxViewHeight > 0f ? request.MaxViewHeight : float.PositiveInfinity;
        var startX = request.WindowX + request.Margin - request.ScrollX;
        var startY = request.WindowY + request.Margin + Math.Max(request.TitleHeight, 0f) - request.ScrollY;
        var currentColumn = 0;
        var columnX = 0f;
        var y = 0f;
        var results = new List<FloatMenuTargetRect>(optionHeights.Count);

        for (var i = 0; i < optionHeights.Count; i++)
        {
            var optionHeight = Math.Max(optionHeights[i], 0f);
            if (y + optionHeight + request.OptionSpacing > maxViewHeight)
            {
                y = 0f;
                columnX += columnWidth + request.OptionSpacing;
                currentColumn++;
            }

            results.Add(new FloatMenuTargetRect
            {
                Index = i + 1,
                ColumnIndex = currentColumn,
                X = startX + columnX,
                Y = startY + y,
                Width = columnWidth,
                Height = optionHeight
            });

            y += optionHeight + request.OptionSpacing;
        }

        return results;
    }
}
