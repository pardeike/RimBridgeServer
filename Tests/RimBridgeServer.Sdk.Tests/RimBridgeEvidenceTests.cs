using System;
using System.Collections.Generic;
using RimBridgeServer.Sdk;
using Xunit;

namespace RimBridgeServer.Sdk.Tests;

public sealed class RimBridgeEvidenceTests
{
    [Fact]
    public void CreateManifestInitializesStableEvidenceShape()
    {
        var manifest = RimBridgeEvidence.CreateManifest("tank-pipe", "run-1");

        Assert.False(manifest.success);
        Assert.Equal("tank-pipe", manifest.suite);
        Assert.Equal("run-1", manifest.runId);
        Assert.NotEqual(default, manifest.startedAtUtc);
        Assert.NotNull(manifest.environment);
        Assert.NotNull(manifest.captures);
        Assert.NotNull(manifest.assertions);
        Assert.NotNull(manifest.errors);
        Assert.NotNull(manifest.logs);
        Assert.False(string.IsNullOrWhiteSpace(manifest.sdkVersion));
        Assert.Equal(manifest.sdkVersion, manifest.environment.sdkVersion);
    }

    [Fact]
    public void AssertionHelpersReportPassAndFail()
    {
        var pass = RimBridgeEvidence.AreEqual("count", 16, 16);
        var fail = RimBridgeEvidence.AreEqual("count", 16, 15);

        Assert.True(pass.success);
        Assert.False(fail.success);
        Assert.Equal(16, fail.expected);
        Assert.Equal(15, fail.actual);
        Assert.False(RimBridgeEvidence.AllPassed(new[] { pass, fail }));
    }

    [Fact]
    public void ToolSucceededUsesToolCallResultSemantics()
    {
        var result = new RimBridgeToolCallResult<object>
        {
            Success = true,
            OperationId = "op_1",
            CapabilityId = "rimbridge/ping",
            Result = new Dictionary<string, object>
            {
                ["success"] = true
            }
        };

        var assertion = RimBridgeEvidence.ToolSucceeded("ping", result);

        Assert.True(assertion.success);
        Assert.NotNull(assertion.details);
    }

    [Fact]
    public void CompleteMarksManifestFromAssertionsAndErrors()
    {
        var manifest = RimBridgeEvidence.CreateManifest("suite");
        manifest.assertions.Add(RimBridgeEvidence.Pass("ready"));

        RimBridgeEvidence.Complete(manifest);

        Assert.True(manifest.success);
        Assert.NotNull(manifest.completedAtUtc);

        manifest = RimBridgeEvidence.CreateManifest("suite");
        manifest.assertions.Add(RimBridgeEvidence.Pass("ready"));
        manifest.errors.Add(new RimBridgeEvidenceError { stage = "load", message = "failed" });

        RimBridgeEvidence.Complete(manifest);

        Assert.False(manifest.success);
    }
}
