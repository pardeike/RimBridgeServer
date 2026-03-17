using System;

namespace RimBridgeServer;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class ReadmeToolAttribute : Attribute
{
    public ReadmeToolAttribute(string group, string summary)
    {
        Group = group ?? throw new ArgumentNullException(nameof(group));
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    public string Group { get; }

    public string Summary { get; }
}
