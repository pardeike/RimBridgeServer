using System.Collections.Generic;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class CapabilityLuaReferenceBuilderTests
{
    [Fact]
    public void CreateDocumentIncludesRunLuaToolMetadata()
    {
        var document = CapabilityLuaReferenceBuilder.CreateDocument();

        Assert.True(ReadBool(document, "success"));
        Assert.Equal("lua-script-v1", ReadString(document, "version"));

        var tool = ReadObject(document, "tool");
        Assert.Equal("rimbridge/run_lua", ReadString(tool, "name"));
        Assert.Equal("rimbridge/get_lua_reference", ReadString(tool, "companionTool"));
        Assert.Equal("rimbridge/compile_lua", ReadString(tool, "compileTool"));

        var arguments = ReadArray(tool, "arguments");
        Assert.Equal("luaSource", ReadString(ReadObject(arguments[0]), "name"));
    }

    [Fact]
    public void CreateDocumentIncludesHostApiAndUnsupportedSurface()
    {
        var document = CapabilityLuaReferenceBuilder.CreateDocument();

        var hostApi = ReadArray(document, "hostApi");
        Assert.Contains(hostApi, item => ReadString(ReadObject(item), "name") == "rb.call");
        Assert.Contains(hostApi, item => ReadString(ReadObject(item), "name") == "rb.poll");
        Assert.Contains(hostApi, item => ReadString(ReadObject(item), "name") == "rb.assert");
        Assert.Contains(hostApi, item => ReadString(ReadObject(item), "name") == "ipairs");

        var unsupported = ReadArray(document, "unsupported");
        Assert.Contains(unsupported, item => Assert.IsType<string>(item) == "break");
        Assert.Contains(unsupported, item => Assert.IsType<string>(item) == "dynamic indexing");
        Assert.Contains(unsupported, item => Assert.IsType<string>(item) == "arbitrary global assignment");
    }

    [Fact]
    public void CreateDocumentIncludesCompileErrorsAndExamples()
    {
        var document = CapabilityLuaReferenceBuilder.CreateDocument();

        var compileErrors = ReadObject(document, "compileErrors");
        var failureCodes = ReadArray(compileErrors, "failureCodes");
        Assert.Contains(failureCodes, item => ReadString(ReadObject(item), "code") == "lua.syntax_error");
        Assert.Contains(failureCodes, item => ReadString(ReadObject(item), "code") == "lua.unsupported_expression");

        var examples = ReadArray(document, "examples");
        Assert.Contains(examples, item => ReadString(ReadObject(item), "name") == "minimal");
        Assert.Contains(examples, item => ReadString(ReadObject(item), "name") == "poll_and_assert");
        Assert.Contains(examples, item => ReadString(ReadObject(item), "name") == "wait_for_entry_scene");
        Assert.Contains(examples, item => ReadString(ReadObject(item), "name") == "bounded_search_and_validate");
        Assert.Contains(examples, item => ReadString(ReadObject(item), "name") == "foreach_selection");
    }

    [Fact]
    public void CreateDocumentPointsAtSharedRuntimeReference()
    {
        var document = CapabilityLuaReferenceBuilder.CreateDocument();

        var runtimeModel = ReadObject(document, "runtimeModel");
        Assert.Equal("rimbridge/run_script", ReadString(runtimeModel, "loweringTargetTool"));
        Assert.Equal("rimbridge/get_script_reference", ReadString(runtimeModel, "runtimeReferenceTool"));
    }

    [Fact]
    public void CreateDocumentIncludesPollingGuidanceAndPatterns()
    {
        var document = CapabilityLuaReferenceBuilder.CreateDocument();

        var pollingGuidance = ReadObject(document, "pollingGuidance");
        var bestPractices = ReadArray(pollingGuidance, "bestPractices");
        Assert.Contains(bestPractices, item => Assert.IsType<string>(item).Contains("Prefer rb.poll against a read-only capability"));

        var patterns = ReadArray(pollingGuidance, "patterns");
        Assert.Contains(patterns, item => ReadString(ReadObject(item), "name") == "wait_for_entry_scene");
        Assert.Contains(patterns, item => ReadString(ReadObject(item), "name") == "wait_for_colonists_in_area");
        Assert.Contains(patterns, item => ReadString(ReadObject(item), "name") == "bounded_search_and_validate");
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
}
