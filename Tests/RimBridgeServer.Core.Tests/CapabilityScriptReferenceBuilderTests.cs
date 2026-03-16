using System.Collections.Generic;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class CapabilityScriptReferenceBuilderTests
{
    [Fact]
    public void CreateDocumentIncludesRunScriptToolMetadata()
    {
        var document = CapabilityScriptReferenceBuilder.CreateDocument();

        Assert.True(ReadBool(document, "success"));
        Assert.Equal("json-script-v1", ReadString(document, "version"));

        var tool = ReadObject(document, "tool");
        Assert.Equal("rimbridge/run_script", ReadString(tool, "name"));
        Assert.Equal("rimbridge/get_script_reference", ReadString(tool, "companionTool"));

        var arguments = ReadArray(tool, "arguments");
        Assert.Equal(2, arguments.Count);
        Assert.Equal("scriptJson", ReadString(ReadObject(arguments[0]), "name"));
    }

    [Fact]
    public void CreateDocumentReflectsCurrentScriptDefaults()
    {
        var document = CapabilityScriptReferenceBuilder.CreateDocument();
        var definition = ReadObject(document, "definition");
        var fields = ReadArray(definition, "fields");
        var defaults = new CapabilityScriptDefinition();

        Assert.Equal(defaults.MaxDurationMs, ReadInt32(FindField(fields, "maxDurationMs"), "defaultValue"));
        Assert.Equal(defaults.MaxExecutedStatements, ReadInt32(FindField(fields, "maxExecutedStatements"), "defaultValue"));
        Assert.Equal(defaults.MaxControlDepth, ReadInt32(FindField(fields, "maxControlDepth"), "defaultValue"));

        var limits = ReadObject(document, "limits");
        var global = ReadArray(limits, "global");
        Assert.Equal("script.timeout", ReadString(ReadObject(global[0]), "failureCode"));
        Assert.Equal("script.statement_limit_exceeded", ReadString(ReadObject(global[1]), "failureCode"));
        Assert.Equal("script.max_depth_exceeded", ReadString(ReadObject(global[2]), "failureCode"));
    }

    [Fact]
    public void CreateDocumentIncludesStatementsConditionsAndExamples()
    {
        var document = CapabilityScriptReferenceBuilder.CreateDocument();

        var statementTypes = ReadArray(document, "statementTypes");
        Assert.Contains(statementTypes, item => ReadString(ReadObject(item), "type") == "call");
        Assert.Contains(statementTypes, item => ReadString(ReadObject(item), "type") == "while");
        Assert.Contains(statementTypes, item => ReadString(ReadObject(item), "type") == "assert");

        var conditionModel = ReadObject(document, "conditionModel");
        var operators = ReadArray(conditionModel, "operators");
        Assert.Contains(operators, item => ReadString(ReadObject(item), "name") == "allItems");
        Assert.Contains(operators, item => ReadString(ReadObject(item), "name") == "greaterThanOrEqual");

        var examples = ReadArray(document, "examples");
        Assert.Contains(examples, item => ReadString(ReadObject(item), "name") == "value_passing");
        Assert.Contains(examples, item => ReadString(ReadObject(item), "name") == "test_style");

        var testStyle = ReadObject(examples.Find(item => ReadString(ReadObject(item), "name") == "test_style"));
        var script = ReadObject(testStyle, "script");
        Assert.Equal("test-style", ReadString(script, "name"));
    }

    [Fact]
    public void CreateDocumentIncludesCurrentExpressionForms()
    {
        var document = CapabilityScriptReferenceBuilder.CreateDocument();
        var expressionForms = ReadArray(document, "expressionForms");

        Assert.Contains(expressionForms, item => ReadString(ReadObject(item), "name") == "$var");
        Assert.Contains(expressionForms, item => ReadString(ReadObject(item), "name") == "$and");
        Assert.Contains(expressionForms, item => ReadString(ReadObject(item), "name") == "$greaterThanOrEqual");
        Assert.Contains(expressionForms, item => ReadString(ReadObject(item), "name") == "$not");
    }

    [Fact]
    public void CreateDocumentIncludesAttemptsInReferenceRootFields()
    {
        var document = CapabilityScriptReferenceBuilder.CreateDocument();
        var references = ReadObject(document, "references");
        var rootFields = ReadArray(references, "stepReferenceRootFields");

        Assert.Contains(rootFields, item => Assert.IsType<string>(item) == "attempts");
    }

    private static Dictionary<string, object> FindField(List<object> fields, string name)
    {
        foreach (var field in fields)
        {
            var fieldObject = ReadObject(field);
            if (ReadString(fieldObject, "name") == name)
                return fieldObject;
        }

        throw new KeyNotFoundException($"Field '{name}' was not found.");
    }

    private static Dictionary<string, object> ReadObject(Dictionary<string, object> source, string key)
    {
        return ReadObject(source[key]);
    }

    private static Dictionary<string, object> ReadObject(object value)
    {
        return Assert.IsType<Dictionary<string, object>>(value);
    }

    private static List<object> ReadArray(Dictionary<string, object> source, string key)
    {
        return Assert.IsType<List<object>>(source[key]);
    }

    private static string ReadString(Dictionary<string, object> source, string key)
    {
        return Assert.IsType<string>(source[key]);
    }

    private static bool ReadBool(Dictionary<string, object> source, string key)
    {
        return Assert.IsType<bool>(source[key]);
    }

    private static int ReadInt32(Dictionary<string, object> source, string key)
    {
        return Assert.IsType<int>(source[key]);
    }
}
