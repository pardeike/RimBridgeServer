using System.Collections.Generic;
using System.Linq;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class SemanticValueGraphTests
{
    [Fact]
    public void DescribesNestedObjectsCollectionsAndPrivateFields()
    {
        var settings = new SampleSettings
        {
            Enabled = true,
            Name = "bridge",
            Nested = new SampleNested
            {
                Threshold = 2.5f
            },
            Labels = ["alpha", "beta"],
            Weights = new Dictionary<string, int>
            {
                ["wood"] = 3
            }
        };
        settings.SetRetryCount(4);

        var graph = SemanticValueGraph.Describe(settings);

        Assert.Equal("object", graph.ValueKind);
        Assert.Equal(typeof(SampleSettings).FullName, graph.TypeName);
        Assert.Equal("boolean", GetChild(graph, nameof(SampleSettings.Enabled)).ValueKind);
        Assert.Equal(true, GetChild(graph, nameof(SampleSettings.Enabled)).Value);
        Assert.Equal("bridge", GetChild(graph, nameof(SampleSettings.Name)).Value);
        Assert.Equal(4, GetChild(graph, "retryCount").Value);

        var nested = GetChild(graph, nameof(SampleSettings.Nested));
        Assert.Equal("object", nested.ValueKind);
        Assert.Equal(2.5f, GetChild(nested, nameof(SampleNested.Threshold)).Value);

        var labels = GetChild(graph, nameof(SampleSettings.Labels));
        Assert.Equal("array", labels.ValueKind);
        Assert.Equal(nameof(SampleSettings.Labels) + "[0]", labels.Children[0].Path);
        Assert.Equal("alpha", labels.Children[0].Value);

        var weights = GetChild(graph, nameof(SampleSettings.Weights));
        Assert.Equal("dictionary", weights.ValueKind);
        Assert.Single(weights.Children);
        Assert.Equal(nameof(SampleSettings.Weights) + "[wood]", weights.Children[0].Path);
        Assert.Equal(3, weights.Children[0].Value);
    }

    [Fact]
    public void MarksRepeatedReferencesInsteadOfRecursingForever()
    {
        var root = new LoopNode();
        root.Next = root;

        var graph = SemanticValueGraph.Describe(root);
        var next = GetChild(graph, nameof(LoopNode.Next));

        Assert.Equal("reference", next.ValueKind);
        Assert.Equal(string.Empty, next.Value);
    }

    [Fact]
    public void TruncatesWhenMaxDepthIsReached()
    {
        var settings = new SampleSettings
        {
            Nested = new SampleNested
            {
                Child = new SampleNested()
            }
        };

        var graph = SemanticValueGraph.Describe(settings, new SemanticValueGraphOptions
        {
            MaxDepth = 1
        });

        var nested = GetChild(graph, nameof(SampleSettings.Nested));
        Assert.True(nested.Truncated);
        Assert.Empty(nested.Children);
    }

    private static SemanticValueNode GetChild(SemanticValueNode node, string name)
    {
        return Assert.Single(node.Children, child => child.Name == name);
    }

    private sealed class SampleSettings
    {
        public bool Enabled;

        public List<string> Labels = [];

        public string Name = string.Empty;

        public SampleNested Nested = new();

        public Dictionary<string, int> Weights = [];

        private int retryCount;

        public void SetRetryCount(int value)
        {
            retryCount = value;
        }
    }

    private sealed class SampleNested
    {
        public SampleNested Child;

        public float Threshold;
    }

    private sealed class LoopNode
    {
        public LoopNode Next;
    }
}
