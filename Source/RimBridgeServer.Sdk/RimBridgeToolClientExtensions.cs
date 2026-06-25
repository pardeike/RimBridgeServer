using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace RimBridgeServer.Sdk;

public static class RimBridgeToolClientExtensions
{
    public static bool Succeeded(this IRimBridgeToolCallResult result)
    {
        if (result == null || result.Success == false)
            return false;

        var payloadSuccess = result.PayloadSuccess();
        return payloadSuccess ?? true;
    }

    public static bool? PayloadSuccess(this IRimBridgeToolCallResult result)
    {
        if (result == null)
            return null;

        return result.TryReadResult<bool>(out var success, "success")
            ? success
            : null;
    }

    public static TValue ReadResult<TValue>(this IRimBridgeToolCallResult result, params string[] path)
    {
        if (result.TryReadResult<TValue>(out var value, path))
            return value;

        var pathLabel = path == null || path.Length == 0
            ? "<root>"
            : string.Join(".", path);
        throw new KeyNotFoundException($"Result path '{pathLabel}' was not found or could not be converted to {typeof(TValue).Name}.");
    }

    public static bool TryReadResult<TValue>(this IRimBridgeToolCallResult result, out TValue value, params string[] path)
    {
        value = default;
        if (result == null)
            return false;

        if (TryGetPath(result.RawResult, path, out var raw) == false)
            return false;

        return TryConvertValue(raw, out value);
    }

    private static bool TryGetPath(object source, IReadOnlyList<string> path, out object value)
    {
        value = source;
        if (path == null || path.Count == 0)
            return true;

        foreach (var segment in path)
        {
            if (TryGetMember(value, segment, out value) == false)
                return false;
        }

        return true;
    }

    private static bool TryGetMember(object source, string name, out object value)
    {
        value = null;
        if (source == null || string.IsNullOrWhiteSpace(name))
            return false;

        if (source is IDictionary dictionary)
            return TryGetDictionaryValue(dictionary, name, out value);

        if (TryGetKeyValueEnumerable(source, name, out value))
            return true;

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
        var type = source.GetType();
        var property = type
            .GetProperties(flags)
            .FirstOrDefault(candidate => candidate.GetIndexParameters().Length == 0
                && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (property != null)
        {
            value = property.GetValue(source);
            return true;
        }

        var field = type.GetField(name, flags);
        if (field != null)
        {
            value = field.GetValue(source);
            return true;
        }

        return false;
    }

    private static bool TryGetDictionaryValue(IDictionary dictionary, string name, out object value)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key != null && string.Equals(entry.Key.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetKeyValueEnumerable(object source, string name, out object value)
    {
        value = null;
        if (source is IEnumerable enumerable == false)
            return false;

        foreach (var item in enumerable)
        {
            if (item == null)
                continue;

            var type = item.GetType();
            var keyProperty = type.GetProperty("Key");
            var valueProperty = type.GetProperty("Value");
            if (keyProperty == null || valueProperty == null)
                continue;

            var key = keyProperty.GetValue(item);
            if (key != null && string.Equals(key.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                value = valueProperty.GetValue(item);
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertValue<TValue>(object raw, out TValue value)
    {
        value = default;
        if (raw == null)
            return AllowsNull(typeof(TValue));

        if (raw is TValue typed)
        {
            value = typed;
            return true;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        try
        {
            if (targetType == typeof(string))
            {
                value = (TValue)(object)raw.ToString();
                return true;
            }

            if (targetType.IsEnum)
            {
                var enumValue = raw is string text
                    ? Enum.Parse(targetType, text, ignoreCase: true)
                    : Enum.ToObject(targetType, raw);
                value = (TValue)enumValue;
                return true;
            }

            if (raw is IConvertible)
            {
                value = (TValue)Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool AllowsNull(Type type)
    {
        return type.IsValueType == false || Nullable.GetUnderlyingType(type) != null;
    }
}
