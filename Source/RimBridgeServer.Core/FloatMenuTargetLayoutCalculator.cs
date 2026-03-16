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

        var columnCount = Math.Max(request.ColumnCount, 1);
        var columnWidth = Math.Max(request.ColumnWidth, 0f);
        var maxViewHeight = request.MaxViewHeight > 0f ? request.MaxViewHeight : float.PositiveInfinity;
        var startX = request.WindowX + request.Margin;
        var startY = request.WindowY + request.Margin + Math.Max(request.TitleHeight, 0f);
        var currentColumn = 0;
        var currentY = startY;
        var results = new List<FloatMenuTargetRect>(optionHeights.Count);

        for (var i = 0; i < optionHeights.Count; i++)
        {
            var optionHeight = Math.Max(optionHeights[i], 0f);
            if (currentColumn < columnCount - 1
                && currentY > startY
                && currentY + optionHeight > startY + maxViewHeight)
            {
                currentColumn++;
                currentY = startY;
            }

            results.Add(new FloatMenuTargetRect
            {
                Index = i + 1,
                ColumnIndex = currentColumn,
                X = startX + (currentColumn * columnWidth),
                Y = currentY,
                Width = columnWidth,
                Height = optionHeight
            });

            currentY += optionHeight + request.OptionSpacing;
        }

        return results;
    }
}
