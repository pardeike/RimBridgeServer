using System.Text.Json.Nodes;
using RimBridgeServer.LiveSmoke;
using Xunit;

namespace RimBridgeServer.LiveSmoke.Tests;

public class JsonNodeHelpersTests
{
    [Fact]
    public void ReadsNestedScalarValues()
    {
        var node = JsonNode.Parse("""
            {
              "success": true,
              "count": 3,
              "state": {
                "programState": "Playing",
                "hasCurrentGame": "true"
              },
              "latestOperationEventSequence": 12
            }
            """);

        Assert.True(JsonNodeHelpers.ReadBoolean(node, "success"));
        Assert.Equal(3, JsonNodeHelpers.ReadInt32(node, "count"));
        Assert.Equal("Playing", JsonNodeHelpers.ReadString(node, "state", "programState"));
        Assert.True(JsonNodeHelpers.ReadBoolean(node, "state", "hasCurrentGame"));
        Assert.Equal(12L, JsonNodeHelpers.ReadInt64(node, "latestOperationEventSequence"));
        Assert.Equal(3d, JsonNodeHelpers.ReadDouble(node, "count"));
    }

    [Fact]
    public void ReadsArraysAsDetachedCopies()
    {
        var node = JsonNode.Parse("""
            {
              "events": [
                { "EventType": "operation.started" },
                { "EventType": "operation.completed" }
              ]
            }
            """);

        var values = JsonNodeHelpers.ReadArray(node, "events");

        Assert.Equal(2, values.Count);
        Assert.Equal("operation.started", JsonNodeHelpers.ReadString(values[0], "EventType"));
        Assert.NotSame(JsonNodeHelpers.GetPath(node, "events"), values[0]?.Parent);
    }

    [Fact]
    public void NormalizesStructuredPayloadFromJsonText()
    {
        var normalized = JsonNodeHelpers.NormalizeStructuredPayload(null, """
            {
              "success": true,
              "children": [
                { "path": "Actions\\T: Log Job Details" }
              ]
            }
            """);

        Assert.True(JsonNodeHelpers.ReadBoolean(normalized, "success"));
        var children = JsonNodeHelpers.ReadArray(normalized, "children");
        Assert.Single(children);
        Assert.Equal(@"Actions\T: Log Job Details", JsonNodeHelpers.ReadString(children[0], "path"));
    }

    [Fact]
    public void NormalizesStructuredPayloadFromWrapperContentArray()
    {
        var wrapper = JsonNode.Parse("""
            {
              "tool": "games.call_tool",
              "content": [
                {
                  "type": "text",
                  "text": "{\"success\":true,\"matches\":[{\"path\":\"Actions\\\\T: Toggle Job Logging\"}]}"
                }
              ]
            }
            """);

        var normalized = JsonNodeHelpers.NormalizeStructuredPayload(wrapper, string.Empty);

        Assert.True(JsonNodeHelpers.ReadBoolean(normalized, "success"));
        var matches = JsonNodeHelpers.ReadArray(normalized, "matches");
        Assert.Single(matches);
        Assert.Equal(@"Actions\T: Toggle Job Logging", JsonNodeHelpers.ReadString(matches[0], "path"));
    }
}
