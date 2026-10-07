using System;
using System.Collections.Generic;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class ObjectPathUpdaterTests
{
    [Fact]
    public void UpdatesPublicPrivateNestedAndIndexedFields()
    {
        var settings = new SampleSettings
        {
            Nested = new SampleNested
            {
                Threshold = 2
            },
            Labels = ["alpha"]
        };
        settings.SetRetryCount(3);

        var results = ObjectPathUpdater.Apply(settings, new Dictionary<string, object>
        {
            ["Enabled"] = true,
            ["retryCount"] = 5,
            ["Nested.Threshold"] = 4,
            ["Labels[0]"] = "beta"
        });

        Assert.Equal(4, results.Count);
        Assert.True(settings.Enabled);
        Assert.Equal(5, settings.GetRetryCount());
        Assert.Equal(4, settings.Nested.Threshold);
        Assert.Equal("beta", settings.Labels[0]);
    }

    [Fact]
    public void InstantiatesMissingNestedObjectsAndExpandsLists()
    {
        var settings = new SampleSettings();

        ObjectPathUpdater.Apply(settings, new Dictionary<string, object>
        {
            ["Nested.Threshold"] = 7,
            ["Labels[1]"] = "gamma"
        });

        Assert.NotNull(settings.Nested);
        Assert.Equal(7, settings.Nested.Threshold);
        Assert.Equal(2, settings.Labels.Count);
        Assert.Null(settings.Labels[0]);
        Assert.Equal("gamma", settings.Labels[1]);
    }

    [Fact]
    public void ReadsCurrentValuesByPath()
    {
        var settings = new SampleSettings
        {
            Enabled = true,
            Nested = new SampleNested
            {
                Threshold = 9
            }
        };

        Assert.Equal(true, ObjectPathUpdater.GetValue(settings, "Enabled"));
        Assert.Equal(9, ObjectPathUpdater.GetValue(settings, "Nested.Threshold"));
    }

    [Fact]
    public void ResolvesMemberPathsAfterIndexSegments()
    {
        var settings = new SampleSettings
        {
            Entries = [new SampleNested { Threshold = 1 }]
        };

        ObjectPathUpdater.Apply(settings, new Dictionary<string, object>
        {
            ["Entries[0].Threshold"] = 6,
            ["Entries[1].Threshold"] = 8
        });

        Assert.Equal(6, ObjectPathUpdater.GetValue(settings, "Entries[0].Threshold"));
        Assert.Equal(8, settings.Entries[1].Threshold);
    }

    [Theory]
    [InlineData("Entries[0]..Threshold")]
    [InlineData("Entries[0].")]
    [InlineData("Nested..Threshold")]
    [InlineData(".Enabled")]
    [InlineData("Enabled.")]
    public void RejectsEmptyMemberSegments(string path)
    {
        var settings = new SampleSettings
        {
            Nested = new SampleNested(),
            Entries = [new SampleNested()]
        };

        var error = Assert.Throws<InvalidOperationException>(() => ObjectPathUpdater.GetValue(settings, path));
        Assert.Contains("empty member segment", error.Message);
    }

    private sealed class SampleSettings
    {
        public List<SampleNested> Entries = [];

        public bool Enabled;

        public List<string> Labels = [];

        public SampleNested Nested;

        private int retryCount;

        public int GetRetryCount()
        {
            return retryCount;
        }

        public void SetRetryCount(int value)
        {
            retryCount = value;
        }
    }

    private sealed class SampleNested
    {
        public int Threshold;
    }
}
