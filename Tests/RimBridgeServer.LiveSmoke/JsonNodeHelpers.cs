using System.Text.Json.Nodes;
using System.Text.Json;

namespace RimBridgeServer.LiveSmoke;

internal static class JsonNodeHelpers
{
    public static JsonNode? CloneNode(JsonNode? node)
    {
        return node?.DeepClone();
    }

    public static JsonNode? GetPath(JsonNode? node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                return null;
        }

        return current;
    }

    public static string ReadString(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is null)
            return string.Empty;

        if (valueNode is JsonValue value)
        {
            if (value.TryGetValue(out string? stringValue) && stringValue is not null)
                return stringValue;

            return value.ToJsonString().Trim('"');
        }

        return valueNode.ToJsonString();
    }

    public static bool? ReadBoolean(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is not JsonValue value)
            return null;

        if (value.TryGetValue(out bool boolValue))
            return boolValue;

        if (value.TryGetValue(out string? stringValue) && bool.TryParse(stringValue, out var parsed))
            return parsed;

        return null;
    }

    public static int? ReadInt32(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is not JsonValue value)
            return null;

        if (value.TryGetValue(out int intValue))
            return intValue;

        if (value.TryGetValue(out long longValue))
            return checked((int)longValue);

        if (value.TryGetValue(out string? stringValue) && int.TryParse(stringValue, out var parsed))
            return parsed;

        return null;
    }

    public static long? ReadInt64(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is not JsonValue value)
            return null;

        if (value.TryGetValue(out long longValue))
            return longValue;

        if (value.TryGetValue(out int intValue))
            return intValue;

        if (value.TryGetValue(out string? stringValue) && long.TryParse(stringValue, out var parsed))
            return parsed;

        return null;
    }

    public static double? ReadDouble(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is not JsonValue value)
            return null;

        if (value.TryGetValue(out double doubleValue))
            return doubleValue;

        if (value.TryGetValue(out float floatValue))
            return floatValue;

        if (value.TryGetValue(out decimal decimalValue))
            return (double)decimalValue;

        if (value.TryGetValue(out int intValue))
            return intValue;

        if (value.TryGetValue(out long longValue))
            return longValue;

        if (value.TryGetValue(out string? stringValue) && double.TryParse(stringValue, out var parsed))
            return parsed;

        return null;
    }

    public static List<JsonNode?> ReadArray(JsonNode? node, params string[] path)
    {
        return GetPath(node, path) is JsonArray array
            ? array.Select(CloneNode).ToList()
            : [];
    }

    public static JsonNode? ReadObject(JsonNode? node, params string[] path)
    {
        return GetPath(node, path) is JsonObject obj ? CloneNode(obj) : null;
    }

    public static string ReadTextContent(JsonNode? resultNode)
    {
        if (GetPath(resultNode, "content") is not JsonArray contentArray)
            return string.Empty;

        var lines = contentArray
            .Select(entry => ReadString(entry, "text"))
            .Where(text => string.IsNullOrWhiteSpace(text) == false)
            .ToList();

        return string.Join(Environment.NewLine, lines);
    }

    public static JsonNode? NormalizeStructuredPayload(JsonNode? structuredContent, string? textContent)
    {
        return NormalizeStructuredPayload(structuredContent, textContent, depth: 0);
    }

    private static JsonNode? NormalizeStructuredPayload(JsonNode? structuredContent, string? textContent, int depth)
    {
        if (depth > 4)
            return CloneNode(structuredContent);

        if (TryParseEmbeddedJsonValue(structuredContent, out var parsedValue))
            return parsedValue;

        if (structuredContent is JsonObject wrapper)
        {
            var nestedStructuredContent = GetPath(wrapper, "structuredContent");
            var nestedTextContent = ReadTextContent(wrapper);
            if (nestedStructuredContent != null || string.IsNullOrWhiteSpace(nestedTextContent) == false)
            {
                var normalizedNested = NormalizeStructuredPayload(nestedStructuredContent, nestedTextContent, depth + 1);
                if (normalizedNested != null)
                    return normalizedNested;
            }
        }

        if (structuredContent != null)
            return CloneNode(structuredContent);

        return TryParseJsonText(textContent, out var parsedText) ? parsedText : null;
    }

    public static bool TryParseJsonText(string? text, out JsonNode? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (!(trimmed.StartsWith("{", StringComparison.Ordinal)
              || trimmed.StartsWith("[", StringComparison.Ordinal)
              || trimmed.StartsWith("\"", StringComparison.Ordinal)))
        {
            return false;
        }

        try
        {
            parsed = JsonNode.Parse(trimmed);
            return parsed != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseEmbeddedJsonValue(JsonNode? node, out JsonNode? parsed)
    {
        parsed = null;
        if (node is not JsonValue value)
            return false;

        return value.TryGetValue(out string? stringValue) && TryParseJsonText(stringValue, out parsed);
    }
}
