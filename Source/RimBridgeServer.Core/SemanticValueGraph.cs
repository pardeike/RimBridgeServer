using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RimBridgeServer.Core;

public sealed class SemanticValueGraphOptions
{
    public int MaxDepth { get; set; } = 4;

    public int MaxCollectionEntries { get; set; } = 32;
}

public sealed class SemanticValueNode
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string ValueKind { get; set; } = string.Empty;

    public string TypeName { get; set; }

    public object Value { get; set; }

    public int? Count { get; set; }

    public bool Truncated { get; set; }

    public List<SemanticValueNode> Children { get; set; } = [];
}

public static class SemanticValueGraph
{
    public static SemanticValueNode Describe(object value, SemanticValueGraphOptions options = null)
    {
        options ??= new SemanticValueGraphOptions();
        var visited = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        return DescribeNode(value, string.Empty, string.Empty, 0, options, visited);
    }

    private static SemanticValueNode DescribeNode(
        object value,
        string name,
        string path,
        int depth,
        SemanticValueGraphOptions options,
        Dictionary<object, string> visited)
    {
        if (options.MaxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxDepth));
        if (options.MaxCollectionEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxCollectionEntries));

        if (value == null)
        {
            return new SemanticValueNode
            {
                Name = name,
                Path = path,
                ValueKind = "null"
            };
        }

        var runtimeType = value.GetType();
        var effectiveType = Nullable.GetUnderlyingType(runtimeType) ?? runtimeType;
        if (TryDescribeScalar(value, effectiveType, out var scalarKind, out var scalarValue))
        {
            return new SemanticValueNode
            {
                Name = name,
                Path = path,
                ValueKind = scalarKind,
                TypeName = effectiveType.FullName ?? effectiveType.Name,
                Value = scalarValue
            };
        }

        if (!runtimeType.IsValueType)
        {
            if (visited.TryGetValue(value, out var existingPath))
            {
                return new SemanticValueNode
                {
                    Name = name,
                    Path = path,
                    ValueKind = "reference",
                    TypeName = effectiveType.FullName ?? effectiveType.Name,
                    Value = existingPath
                };
            }

            visited[value] = path;
        }

        if (depth >= options.MaxDepth)
        {
            return new SemanticValueNode
            {
                Name = name,
                Path = path,
                ValueKind = ResolveContainerKind(runtimeType),
                TypeName = effectiveType.FullName ?? effectiveType.Name,
                Count = TryGetCount(value),
                Truncated = true
            };
        }

        if (value is IDictionary dictionary)
            return DescribeDictionary(dictionary, name, path, depth, options, visited, effectiveType);

        if (value is IEnumerable enumerable && value is not string)
            return DescribeEnumerable(enumerable, name, path, depth, options, visited, effectiveType);

        return DescribeObject(value, name, path, depth, options, visited, effectiveType);
    }

    private static SemanticValueNode DescribeObject(
        object value,
        string name,
        string path,
        int depth,
        SemanticValueGraphOptions options,
        Dictionary<object, string> visited,
        Type effectiveType)
    {
        var children = new List<SemanticValueNode>();
        foreach (var field in EnumerateSerializableFields(effectiveType))
        {
            var childValue = field.GetValue(value);
            children.Add(DescribeNode(
                childValue,
                field.Name,
                BuildMemberPath(path, field.Name),
                depth + 1,
                options,
                visited));
        }

        return new SemanticValueNode
        {
            Name = name,
            Path = path,
            ValueKind = "object",
            TypeName = effectiveType.FullName ?? effectiveType.Name,
            Count = children.Count,
            Children = children
        };
    }

    private static SemanticValueNode DescribeEnumerable(
        IEnumerable enumerable,
        string name,
        string path,
        int depth,
        SemanticValueGraphOptions options,
        Dictionary<object, string> visited,
        Type effectiveType)
    {
        var children = new List<SemanticValueNode>();
        var index = 0;
        var truncated = false;
        foreach (var item in enumerable)
        {
            if (children.Count >= options.MaxCollectionEntries)
            {
                truncated = true;
                break;
            }

            children.Add(DescribeNode(
                item,
                "[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                BuildIndexPath(path, index),
                depth + 1,
                options,
                visited));
            index++;
        }

        return new SemanticValueNode
        {
            Name = name,
            Path = path,
            ValueKind = "array",
            TypeName = effectiveType.FullName ?? effectiveType.Name,
            Count = TryGetCount(enumerable) ?? children.Count,
            Truncated = truncated,
            Children = children
        };
    }

    private static SemanticValueNode DescribeDictionary(
        IDictionary dictionary,
        string name,
        string path,
        int depth,
        SemanticValueGraphOptions options,
        Dictionary<object, string> visited,
        Type effectiveType)
    {
        var children = new List<SemanticValueNode>();
        var truncated = false;
        var index = 0;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (children.Count >= options.MaxCollectionEntries)
            {
                truncated = true;
                break;
            }

            var keyLabel = FormatDictionaryKey(entry.Key);
            children.Add(DescribeNode(
                entry.Value,
                keyLabel,
                BuildDictionaryPath(path, keyLabel),
                depth + 1,
                options,
                visited));
            index++;
        }

        return new SemanticValueNode
        {
            Name = name,
            Path = path,
            ValueKind = "dictionary",
            TypeName = effectiveType.FullName ?? effectiveType.Name,
            Count = dictionary.Count,
            Truncated = truncated,
            Children = children
        };
    }

    private static IReadOnlyList<FieldInfo> EnumerateSerializableFields(Type type)
    {
        var fields = new List<FieldInfo>();
        var current = type;
        while (current != null && current != typeof(object))
        {
            fields.AddRange(current
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(static field =>
                    field.IsStatic == false
                    && field.IsInitOnly == false
                    && field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) == false
                    && typeof(Delegate).IsAssignableFrom(field.FieldType) == false));
            current = current.BaseType;
        }

        return fields
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool TryDescribeScalar(object value, Type runtimeType, out string valueKind, out object scalarValue)
    {
        valueKind = string.Empty;
        scalarValue = null;

        if (runtimeType.IsEnum)
        {
            valueKind = "enum";
            scalarValue = value.ToString();
            return true;
        }

        switch (Type.GetTypeCode(runtimeType))
        {
            case TypeCode.Boolean:
                valueKind = "boolean";
                scalarValue = value;
                return true;
            case TypeCode.Char:
            case TypeCode.String:
                valueKind = "string";
                scalarValue = value.ToString();
                return true;
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                valueKind = "number";
                scalarValue = value;
                return true;
        }

        if (runtimeType == typeof(Guid) || runtimeType == typeof(DateTime) || runtimeType == typeof(DateTimeOffset) || runtimeType == typeof(TimeSpan))
        {
            valueKind = "string";
            scalarValue = Convert.ToString(value, CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static string ResolveContainerKind(Type runtimeType)
    {
        if (typeof(IDictionary).IsAssignableFrom(runtimeType))
            return "dictionary";
        if (typeof(IEnumerable).IsAssignableFrom(runtimeType) && runtimeType != typeof(string))
            return "array";

        return "object";
    }

    private static int? TryGetCount(object value)
    {
        if (value is ICollection collection)
            return collection.Count;

        var countProperty = value.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        if (countProperty?.PropertyType == typeof(int) && countProperty.GetIndexParameters().Length == 0)
            return (int)countProperty.GetValue(value);

        return null;
    }

    private static string BuildMemberPath(string parentPath, string memberName)
    {
        return string.IsNullOrWhiteSpace(parentPath)
            ? memberName
            : parentPath + "." + memberName;
    }

    private static string BuildIndexPath(string parentPath, int index)
    {
        return parentPath + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
    }

    private static string BuildDictionaryPath(string parentPath, string keyLabel)
    {
        return parentPath + "[" + keyLabel + "]";
    }

    private static string FormatDictionaryKey(object key)
    {
        if (key == null)
            return "null";

        return Convert.ToString(key, CultureInfo.InvariantCulture) ?? key.ToString() ?? "key";
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
