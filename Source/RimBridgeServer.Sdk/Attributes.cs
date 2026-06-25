using System;

namespace RimBridgeServer.Sdk;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ToolAttribute : Attribute
{
    public ToolAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string ResultDescription { get; set; }

    public string[] Tags { get; set; } = [];

    public bool RequiresAuth { get; set; }
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ToolParameterAttribute : Attribute
{
    public string Description { get; set; }

    public bool Required { get; set; }

    public object DefaultValue { get; set; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ToolResponseAttribute : Attribute
{
    public ToolResponseAttribute(string name, string type, string description)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public string Name { get; }

    public string Type { get; }

    public string Description { get; }

    public bool Always { get; set; }

    public bool Nullable { get; set; }
}
