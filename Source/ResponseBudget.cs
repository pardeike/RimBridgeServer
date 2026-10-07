using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using RimBridgeServer.Core;

namespace RimBridgeServer;

/// <summary>
/// Bounds list-shaped results for direct agent calls. MCP clients cut oversized tool output
/// (Codex keeps head and tail of roughly 48K characters), so large lists are paged instead.
/// Nested calls from scripts or companion tools are not agent-facing and stay unbounded.
/// </summary>
internal static class ResponseBudget
{
    public const int DefaultMaxChars = 30000;

    // Matches the agent-facing output, which drops null fields.
    private static readonly JsonSerializerSettings MeasureSettings = new() { NullValueHandling = NullValueHandling.Ignore };

    public static int CurrentMaxChars
    {
        get
        {
            var context = OperationContext.Current;
            return string.IsNullOrEmpty(context?.ParentOperationId) ? DefaultMaxChars : int.MaxValue;
        }
    }

    public static List<object> TakePage<T>(IReadOnlyList<T> items, int offset, Func<T, object> project, out object page, int? maxChars = null)
    {
        var budget = maxChars ?? CurrentMaxChars;
        var start = Math.Max(0, Math.Min(offset, items.Count));
        var result = new List<object>();
        var used = 0;
        var index = start;
        for (; index < items.Count; index++)
        {
            var projected = project(items[index]);
            if (budget != int.MaxValue)
            {
                var size = JsonConvert.SerializeObject(projected, MeasureSettings).Length + 1;
                if (result.Count > 0 && used + size > budget)
                    break;
                used += size;
            }

            result.Add(projected);
        }

        page = DescribePage(start, result.Count, items.Count, index < items.Count ? index : null);
        return result;
    }

    public static object DescribePage(int offset, int returned, int total, int? nextOffset)
    {
        return new
        {
            offset,
            returned,
            total,
            nextOffset,
            hint = nextOffset.HasValue
                ? $"Partial result to stay within the response size budget; call again with offset={nextOffset.Value} for the next page."
                : null
        };
    }
}
