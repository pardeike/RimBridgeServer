using System.Text.Json.Nodes;

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
}
