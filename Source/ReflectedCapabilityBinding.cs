using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimBridgeServer;

internal static class ReflectedCapabilityBinding
{
    public static Dictionary<string, object> NormalizeInvocationArguments(object value)
    {
        return value switch
        {
            null => new Dictionary<string, object>(StringComparer.Ordinal),
            string text when string.IsNullOrWhiteSpace(text) => new Dictionary<string, object>(StringComparer.Ordinal),
            string text => NormalizeStructuredDictionary(JsonConvert.DeserializeObject<JObject>(text)
                ?? throw new InvalidOperationException("Tool parameters must deserialize to a JSON object.")),
            _ => NormalizeStructuredDictionary(value)
        };
    }

    public static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = new List<char>(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch) && i > 0)
                chars.Add('-');

            chars.Add(char.ToLowerInvariant(ch));
        }

        return new string(chars.ToArray());
    }

    private static Dictionary<string, object> NormalizeStructuredDictionary(object value)
    {
        return value switch
        {
            null => new Dictionary<string, object>(StringComparer.Ordinal),
            JObject jobject => NormalizeJObject(jobject),
            IDictionary<string, object> dictionary => NormalizeDictionary(dictionary),
            IDictionary legacyDictionary => NormalizeLegacyDictionary(legacyDictionary),
            _ => NormalizeJObject(JObject.FromObject(value))
        };
    }

    private static Dictionary<string, object> NormalizeDictionary(IDictionary<string, object> dictionary)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in dictionary)
            normalized[pair.Key] = NormalizeStructuredValue(pair.Value);

        return normalized;
    }

    private static Dictionary<string, object> NormalizeLegacyDictionary(IDictionary dictionary)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
                throw new InvalidOperationException("Structured object arguments must use string keys.");

            normalized[key] = NormalizeStructuredValue(entry.Value);
        }

        return normalized;
    }

    private static Dictionary<string, object> NormalizeJObject(JObject jobject)
    {
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in jobject.Properties())
            normalized[property.Name] = NormalizeStructuredValue(property.Value);

        return normalized;
    }

    private static List<object> NormalizeJArray(JArray jarray)
    {
        var normalized = new List<object>(jarray.Count);
        foreach (var item in jarray)
            normalized.Add(NormalizeStructuredValue(item));

        return normalized;
    }

    private static List<object> NormalizeEnumerable(IEnumerable<object> items)
    {
        var normalized = new List<object>();
        foreach (var item in items)
            normalized.Add(NormalizeStructuredValue(item));

        return normalized;
    }

    private static object NormalizeStructuredValue(object value)
    {
        return value switch
        {
            null => null,
            JObject jobject => NormalizeJObject(jobject),
            JArray jarray => NormalizeJArray(jarray),
            JValue jvalue => jvalue.Value,
            IDictionary<string, object> dictionary => NormalizeDictionary(dictionary),
            IDictionary legacyDictionary => NormalizeLegacyDictionary(legacyDictionary),
            IEnumerable<object> items when value is not string => NormalizeEnumerable(items),
            IEnumerable enumerable when value is not string => NormalizeEnumerable(enumerable.Cast<object>()),
            _ => value
        };
    }
}
