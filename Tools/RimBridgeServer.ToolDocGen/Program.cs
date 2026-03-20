using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var repoRoot = ResolveRepoRoot(args.Length >= 1 ? args[0] : null, args.Length >= 2 ? args[1] : null);
var sourcePath = args.Length >= 1
    ? Path.GetFullPath(args[0], Environment.CurrentDirectory)
    : Path.Combine(repoRoot, "Source", "RimBridgeTools.cs");
var toolReferencePath = args.Length >= 2
    ? Path.GetFullPath(args[1], Environment.CurrentDirectory)
    : Path.Combine(repoRoot, "docs", "tool-reference.md");
var readmePath = Path.Combine(repoRoot, "README.md");

if (!File.Exists(sourcePath))
    throw new FileNotFoundException($"Tool source file not found: {sourcePath}", sourcePath);
if (!File.Exists(readmePath))
    throw new FileNotFoundException($"README file not found: {readmePath}", readmePath);

var sourceText = await File.ReadAllTextAsync(sourcePath).ConfigureAwait(false);
var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);
var compilationUnit = (CompilationUnitSyntax)await syntaxTree.GetRootAsync().ConfigureAwait(false);
var toolDefinitions = compilationUnit
    .DescendantNodes()
    .OfType<MethodDeclarationSyntax>()
    .Select(ParseToolDefinition)
    .Where(definition => definition is not null)
    .Cast<ToolDefinition>()
    .ToList();

if (toolDefinitions.Count == 0)
    throw new InvalidOperationException($"No [Tool(...)] methods were found in {sourcePath}.");

var markdown = RenderToolReference(toolDefinitions, sourcePath, toolReferencePath);
Directory.CreateDirectory(Path.GetDirectoryName(toolReferencePath) ?? repoRoot);
await File.WriteAllTextAsync(toolReferencePath, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);

var readmeMarkdown = await File.ReadAllTextAsync(readmePath).ConfigureAwait(false);
var updatedReadmeMarkdown = ReplaceGeneratedBlock(
    readmeMarkdown,
    blockId: "tool-surface",
    replacementContent: ReadmeToolSurface.Render(toolDefinitions));
await File.WriteAllTextAsync(readmePath, updatedReadmeMarkdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);

Console.WriteLine($"Generated {toolDefinitions.Count} tool reference entries at {toolReferencePath} and updated README tool surface.");

return;

static string ResolveRepoRoot(string? sourceArgument, string? outputArgument)
{
    if (!string.IsNullOrWhiteSpace(sourceArgument))
    {
        var candidate = Path.GetDirectoryName(Path.GetFullPath(sourceArgument, Environment.CurrentDirectory));
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var root = FindRepoRoot(candidate);
            if (!string.IsNullOrWhiteSpace(root))
                return root;
        }
    }

    if (!string.IsNullOrWhiteSpace(outputArgument))
    {
        var candidate = Path.GetDirectoryName(Path.GetFullPath(outputArgument, Environment.CurrentDirectory));
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var root = FindRepoRoot(candidate);
            if (!string.IsNullOrWhiteSpace(root))
                return root;
        }
    }

    var workingDirectoryRoot = FindRepoRoot(Environment.CurrentDirectory);
    if (!string.IsNullOrWhiteSpace(workingDirectoryRoot))
        return workingDirectoryRoot;

    throw new InvalidOperationException("Could not locate the repository root from the current working directory or the provided paths.");
}

static string? FindRepoRoot(string? startDirectory)
{
    if (string.IsNullOrWhiteSpace(startDirectory))
        return null;

    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        var sourceToolsPath = Path.Combine(directory.FullName, "Source", "RimBridgeTools.cs");
        if (File.Exists(sourceToolsPath))
            return directory.FullName;

        directory = directory.Parent;
    }

    return null;
}

static ToolDefinition? ParseToolDefinition(MethodDeclarationSyntax method)
{
    var toolAttribute = FindAttribute(method.AttributeLists, "Tool");
    if (toolAttribute is null)
        return null;
    var readmeToolAttribute = FindAttribute(method.AttributeLists, "ReadmeTool")
        ?? throw new InvalidOperationException($"Method {method.Identifier.ValueText} is missing the required ReadmeTool metadata.");

    var toolName = GetPositionalStringArgument(toolAttribute, 0)
        ?? throw new InvalidOperationException($"Method {method.Identifier.ValueText} is missing the Tool name argument.");
    var description = GetNamedStringArgument(toolAttribute, "Description")
        ?? throw new InvalidOperationException($"Method {method.Identifier.ValueText} is missing Tool.Description.");
    var resultDescription = GetNamedStringArgument(toolAttribute, "ResultDescription");
    var readmeGroup = GetPositionalStringArgument(readmeToolAttribute, 0);
    if (string.IsNullOrWhiteSpace(readmeGroup))
        throw new InvalidOperationException($"Method {method.Identifier.ValueText} is missing the ReadmeTool group.");

    var readmeSummary = GetPositionalStringArgument(readmeToolAttribute, 1);
    if (string.IsNullOrWhiteSpace(readmeSummary))
        throw new InvalidOperationException($"Method {method.Identifier.ValueText} is missing the ReadmeTool summary.");

    var parameters = method.ParameterList.Parameters
        .Select(ParseParameterDefinition)
        .ToList();

    return new ToolDefinition(toolName, description, resultDescription, readmeGroup, readmeSummary, parameters);
}

static ParameterDefinition ParseParameterDefinition(ParameterSyntax parameter)
{
    var toolParameterAttribute = FindAttribute(parameter.AttributeLists, "ToolParameter");
    var description = toolParameterAttribute is null
        ? null
        : GetNamedStringArgument(toolParameterAttribute, "Description");

    var typeName = parameter.Type?.ToString() ?? "object";
    var defaultValue = parameter.Default?.Value.ToString();
    var required = parameter.Default is null;

    return new ParameterDefinition(
        parameter.Identifier.ValueText,
        typeName,
        description,
        required,
        defaultValue);
}

static AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
{
    foreach (var attributeList in attributeLists)
    {
        foreach (var attribute in attributeList.Attributes)
        {
            var candidate = attribute.Name.ToString();
            if (candidate.Equals(attributeName, StringComparison.Ordinal)
                || candidate.Equals($"{attributeName}Attribute", StringComparison.Ordinal)
                || candidate.EndsWith($".{attributeName}", StringComparison.Ordinal)
                || candidate.EndsWith($".{attributeName}Attribute", StringComparison.Ordinal))
            {
                return attribute;
            }
        }
    }

    return null;
}

static string? GetPositionalStringArgument(AttributeSyntax attribute, int index)
{
    var argument = attribute.ArgumentList?.Arguments.ElementAtOrDefault(index);
    return ExtractStringValue(argument?.Expression);
}

static string? GetNamedStringArgument(AttributeSyntax attribute, string name)
{
    var argument = attribute.ArgumentList?.Arguments.FirstOrDefault(candidate =>
        candidate.NameEquals?.Name.Identifier.ValueText.Equals(name, StringComparison.Ordinal) == true);
    return ExtractStringValue(argument?.Expression);
}

static string? ExtractStringValue(ExpressionSyntax? expression)
{
    return expression switch
    {
        LiteralExpressionSyntax literal when literal.Kind() == SyntaxKind.StringLiteralExpression => literal.Token.ValueText,
        _ => null
    };
}

static string RenderToolReference(IReadOnlyList<ToolDefinition> tools, string sourcePath, string outputPath)
{
    var sourceLink = GetRelativeMarkdownPath(outputPath, sourcePath);
    var generatorScriptPath = GetRelativeMarkdownPath(outputPath, Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "scripts", "generate-tool-reference.sh"));
    var groups = tools
        .GroupBy(tool => tool.Name.Split('/')[0], StringComparer.Ordinal)
        .OrderBy(group => GroupOrder(group.Key))
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .ToList();

    var builder = new StringBuilder();
    builder.AppendLine("# Tool Reference");
    builder.AppendLine();
    builder.AppendLine($"> Generated from [Source/RimBridgeTools.cs]({sourceLink}) by [`scripts/generate-tool-reference.sh`]({generatorScriptPath}). Do not edit by hand.");
    builder.AppendLine();
    builder.AppendLine("This is the full annotation-driven tool reference. The main README stays beginner-friendly; this page is meant to be the authoritative per-tool summary for harnesses, skills, and humans who need the exact surface.");
    builder.AppendLine();
    builder.AppendLine("## Summary");
    builder.AppendLine();
    builder.AppendLine($"- `{tools.Count}` tools total");
    foreach (var group in groups)
        builder.AppendLine($"- `{group.Count()}` `{group.Key}/*` tools");

    foreach (var group in groups)
    {
        builder.AppendLine();
        builder.AppendLine($"## `{group.Key}/*`");
        foreach (var tool in group)
        {
            builder.AppendLine();
            builder.AppendLine($"### `{tool.Name}`");
            builder.AppendLine();
            builder.AppendLine(tool.Description);
            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(tool.ResultDescription))
            {
                builder.AppendLine("Returns:");
                builder.Append("- ");
                builder.AppendLine(tool.ResultDescription);
                builder.AppendLine();
            }

            if (tool.Parameters.Count == 0)
            {
                builder.AppendLine("Parameters: none.");
                continue;
            }

            builder.AppendLine("Parameters:");
            foreach (var parameter in tool.Parameters)
            {
                var metadata = new List<string> { $"`{parameter.TypeName}`", parameter.Required ? "`required`" : "`optional`" };
                if (!parameter.Required && !string.IsNullOrWhiteSpace(parameter.DefaultValue))
                    metadata.Add($"default `{parameter.DefaultValue}`");

                builder.Append("- ");
                builder.Append('`').Append(parameter.Name).Append('`');
                builder.Append(" (");
                builder.Append(string.Join(", ", metadata));
                builder.Append(')');

                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    builder.Append(": ");
                    builder.Append(parameter.Description);
                }

                builder.AppendLine();
            }
        }
    }

    return builder.ToString();
}

static string ReplaceGeneratedBlock(string markdown, string blockId, string replacementContent)
{
    var beginMarker = $"<!-- BEGIN GENERATED:{blockId} -->";
    var endMarker = $"<!-- END GENERATED:{blockId} -->";
    var beginIndex = markdown.IndexOf(beginMarker, StringComparison.Ordinal);
    if (beginIndex < 0)
        throw new InvalidOperationException($"Could not find README marker '{beginMarker}'.");

    var endIndex = markdown.IndexOf(endMarker, beginIndex + beginMarker.Length, StringComparison.Ordinal);
    if (endIndex < 0)
        throw new InvalidOperationException($"Could not find README marker '{endMarker}'.");

    var builder = new StringBuilder();
    builder.Append(markdown.AsSpan(0, beginIndex + beginMarker.Length));
    builder.AppendLine();
    builder.AppendLine();
    builder.AppendLine(replacementContent.TrimEnd());
    builder.AppendLine();
    builder.Append(markdown.AsSpan(endIndex));
    return builder.ToString();
}

static int GroupOrder(string key)
{
    return key switch
    {
        "rimbridge" => 0,
        "rimworld" => 1,
        _ => 2
    };
}

static string GetRelativeMarkdownPath(string fromPath, string toPath)
{
    var fromDirectory = Path.GetDirectoryName(fromPath)
        ?? throw new InvalidOperationException($"Could not resolve directory name for {fromPath}.");
    var relativePath = Path.GetRelativePath(fromDirectory, toPath);
    return relativePath.Replace('\\', '/');
}

internal sealed record ToolDefinition(string Name, string Description, string? ResultDescription, string ReadmeGroup, string ReadmeSummary, IReadOnlyList<ParameterDefinition> Parameters);

internal sealed record ParameterDefinition(string Name, string TypeName, string? Description, bool Required, string? DefaultValue);
