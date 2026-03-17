using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace RimBridgeServer.Core;

public sealed class ObjectPathUpdateResult
{
    public string Path { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public bool Changed { get; set; }

    public object PreviousValue { get; set; }

    public object CurrentValue { get; set; }
}

public static class ObjectPathUpdater
{
    public static IReadOnlyList<ObjectPathUpdateResult> Apply(object root, IReadOnlyDictionary<string, object> updates)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));
        if (updates == null)
            throw new ArgumentNullException(nameof(updates));

        var results = new List<ObjectPathUpdateResult>(updates.Count);
        foreach (var pair in updates.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            results.Add(ApplySingle(root, pair.Key, pair.Value));

        return results;
    }

    public static object GetValue(object root, string path)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        var segments = ParsePath(path);
        if (segments.Count == 0)
            return root;

        object current = root;
        foreach (var segment in segments)
        {
            if (!segment.IsIndex)
            {
                var field = ResolveField(current.GetType(), segment.MemberName);
                current = field.GetValue(current);
            }
            else
            {
                current = GetIndexedValue(current, segment.Index);
            }

            if (current == null)
                return null;
        }

        return current;
    }

    private static ObjectPathUpdateResult ApplySingle(object root, string path, object value)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An object path is required.", nameof(path));

        var segments = ParsePath(path);
        if (segments.Count == 0)
            throw new InvalidOperationException("The root object cannot be replaced directly.");

        object previousValue;
        try
        {
            previousValue = GetValue(root, path);
        }
        catch (InvalidOperationException)
        {
            previousValue = null;
        }

        var target = ResolveTarget(root, segments);
        var convertedValue = ConvertValue(value, target.ValueType);
        target.Assign(convertedValue);
        var currentValue = target.Read();

        return new ObjectPathUpdateResult
        {
            Path = path.Trim(),
            TypeName = target.ValueType.FullName ?? target.ValueType.Name,
            Changed = AreDifferent(previousValue, currentValue),
            PreviousValue = previousValue,
            CurrentValue = currentValue
        };
    }

    private static TargetAccessor ResolveTarget(object root, IReadOnlyList<PathSegment> segments)
    {
        object current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var segment = segments[i];
            var nextSegment = segments[i + 1];
            if (!segment.IsIndex)
            {
                var field = ResolveField(current.GetType(), segment.MemberName);
                var child = field.GetValue(current);
                if (child == null)
                {
                    child = CreateContainer(field.FieldType);
                    field.SetValue(current, child);
                }

                current = child;
                continue;
            }

            current = ResolveIndexedContainer(current, segment.Index, nextSegment);
        }

        var last = segments[segments.Count - 1];
        if (!last.IsIndex)
        {
            var field = ResolveField(current.GetType(), last.MemberName);
            return new TargetAccessor(
                field.FieldType,
                () => field.GetValue(current),
                assigned => field.SetValue(current, assigned));
        }

        return ResolveIndexedTarget(current, last.Index);
    }

    private static object ResolveIndexedContainer(object current, int index, PathSegment nextSegment)
    {
        if (current is not IList list)
            throw new InvalidOperationException($"Path segment '[{index}]' requires an IList-compatible container but found '{current?.GetType().FullName ?? "null"}'.");

        EnsureListSize(list, index);
        var item = list[index];
        if (item != null)
            return item;

        var elementType = GetElementType(list.GetType());
        item = CreateContainer(elementType);
        AssignIndexedValue(list, index, item);
        return item;
    }

    private static TargetAccessor ResolveIndexedTarget(object current, int index)
    {
        if (current is not IList list)
            throw new InvalidOperationException($"Path segment '[{index}]' requires an IList-compatible container but found '{current?.GetType().FullName ?? "null"}'.");

        EnsureListSize(list, index);
        var elementType = GetElementType(list.GetType());
        return new TargetAccessor(
            elementType,
            () => list[index],
            assigned => AssignIndexedValue(list, index, assigned));
    }

    private static void EnsureListSize(IList list, int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (list.IsFixedSize && index >= list.Count)
            throw new InvalidOperationException($"Index {index} is outside the fixed-size collection bounds ({list.Count}).");

        var elementType = GetElementType(list.GetType());
        while (list.Count <= index)
            list.Add(CreateDefault(elementType));
    }

    private static void AssignIndexedValue(IList list, int index, object value)
    {
        list[index] = value;
    }

    private static object GetIndexedValue(object current, int index)
    {
        if (current is not IList list)
            throw new InvalidOperationException($"Path segment '[{index}]' requires an IList-compatible container but found '{current?.GetType().FullName ?? "null"}'.");
        if (index < 0 || index >= list.Count)
            throw new InvalidOperationException($"Index {index} is outside the collection bounds ({list.Count}).");

        return list[index];
    }

    private static FieldInfo ResolveField(Type type, string memberName)
    {
        var current = type;
        while (current != null && current != typeof(object))
        {
            var exact = current.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (exact != null)
                return exact;

            current = current.BaseType;
        }

        current = type;
        while (current != null && current != typeof(object))
        {
            var ignoreCase = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(field => string.Equals(field.Name, memberName, StringComparison.OrdinalIgnoreCase));
            if (ignoreCase != null)
                return ignoreCase;

            current = current.BaseType;
        }

        throw new InvalidOperationException($"Could not resolve field '{memberName}' on '{type.FullName ?? type.Name}'.");
    }

    private static List<PathSegment> ParsePath(string path)
    {
        var trimmed = path?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return [];

        var segments = new List<PathSegment>();
        var buffer = string.Empty;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            switch (ch)
            {
                case '.':
                    if (buffer.Length == 0)
                        throw new InvalidOperationException($"Invalid empty member segment in path '{trimmed}'.");

                    segments.Add(PathSegment.ForMember(buffer));
                    buffer = string.Empty;
                    break;
                case '[':
                    if (buffer.Length > 0)
                    {
                        segments.Add(PathSegment.ForMember(buffer));
                        buffer = string.Empty;
                    }

                    var closingBracket = trimmed.IndexOf(']', i + 1);
                    if (closingBracket < 0)
                        throw new InvalidOperationException($"Unclosed index segment in path '{trimmed}'.");

                    var indexText = trimmed.Substring(i + 1, closingBracket - i - 1);
                    if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                        throw new InvalidOperationException($"Index segment '{indexText}' in path '{trimmed}' is not a valid integer.");

                    segments.Add(PathSegment.ForIndex(index));
                    i = closingBracket;
                    break;
                default:
                    buffer += ch;
                    break;
            }
        }

        if (buffer.Length > 0)
            segments.Add(PathSegment.ForMember(buffer));

        return segments;
    }

    private static object ConvertValue(object value, Type targetType)
    {
        if (targetType == typeof(object))
            return value;

        if (value == null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                return Activator.CreateInstance(targetType);

            return null;
        }

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveType.IsInstanceOfType(value))
            return value;

        if (effectiveType.IsEnum)
        {
            if (value is string enumText)
                return Enum.Parse(effectiveType, enumText, ignoreCase: true);

            var numericValue = Convert.ChangeType(value, Enum.GetUnderlyingType(effectiveType), CultureInfo.InvariantCulture);
            return Enum.ToObject(effectiveType, numericValue);
        }

        if (effectiveType == typeof(string))
            return Convert.ToString(value, CultureInfo.InvariantCulture);

        return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
    }

    private static object CreateContainer(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        if (effectiveType.IsValueType)
            return Activator.CreateInstance(effectiveType);

        if (effectiveType.IsArray)
            throw new InvalidOperationException($"Cannot auto-create array container '{effectiveType.FullName ?? effectiveType.Name}'.");

        var ctor = effectiveType.GetConstructor(Type.EmptyTypes);
        if (ctor == null)
            throw new InvalidOperationException($"Type '{effectiveType.FullName ?? effectiveType.Name}' does not have a parameterless constructor.");

        return ctor.Invoke(Array.Empty<object>());
    }

    private static object CreateDefault(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType.IsValueType ? Activator.CreateInstance(effectiveType) : null;
    }

    private static Type GetElementType(Type listType)
    {
        if (listType.IsArray)
            return listType.GetElementType() ?? typeof(object);

        if (listType.IsGenericType)
            return listType.GetGenericArguments()[0];

        var genericInterface = listType
            .GetInterfaces()
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>));
        if (genericInterface != null)
            return genericInterface.GetGenericArguments()[0];

        return typeof(object);
    }

    private static bool AreDifferent(object previousValue, object currentValue)
    {
        return Equals(previousValue, currentValue) == false;
    }

    private readonly struct PathSegment
    {
        private PathSegment(string memberName, int index, bool isIndex)
        {
            MemberName = memberName;
            Index = index;
            IsIndex = isIndex;
        }

        public string MemberName { get; }

        public int Index { get; }

        public bool IsIndex { get; }

        public static PathSegment ForMember(string memberName)
        {
            return new PathSegment(memberName, -1, isIndex: false);
        }

        public static PathSegment ForIndex(int index)
        {
            return new PathSegment(string.Empty, index, isIndex: true);
        }
    }

    private sealed class TargetAccessor
    {
        public TargetAccessor(Type valueType, Func<object> read, Action<object> assign)
        {
            ValueType = valueType;
            _read = read;
            _assign = assign;
        }

        private readonly Func<object> _read;
        private readonly Action<object> _assign;

        public Type ValueType { get; }

        public object Read()
        {
            return _read();
        }

        public void Assign(object value)
        {
            _assign(value);
        }
    }
}
