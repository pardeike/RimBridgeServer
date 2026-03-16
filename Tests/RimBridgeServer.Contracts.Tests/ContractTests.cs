using System;
using System.Text.Json;
using RimBridgeServer.Contracts;
using RimBridgeServer.Extensions.Abstractions;
using Xunit;

namespace RimBridgeServer.Contracts.Tests;

public class ContractTests
{
    [Fact]
    public void CompletedEnvelopeSetsExpectedMetadata()
    {
        var startedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-25);

        var envelope = OperationEnvelope.Completed("op_123", "rimbridge/ping", startedAtUtc, new { message = "pong" });

        Assert.True(envelope.Success);
        Assert.Equal(OperationStatus.Completed, envelope.Status);
        Assert.Equal("op_123", envelope.OperationId);
        Assert.Equal("rimbridge/ping", envelope.CapabilityId);
        Assert.NotNull(envelope.CompletedAtUtc);
        Assert.True(envelope.DurationMs >= 0);
        Assert.NotNull(envelope.Result);
    }

    [Fact]
    public void WithoutResultKeepsMetadataButRemovesPayload()
    {
        var envelope = OperationEnvelope.Completed("op_456", "rimworld/get_game_info", DateTimeOffset.UtcNow, new { status = "game_loaded" });
        envelope.Metadata["source"] = "test";
        envelope.Warnings.Add(new OperationWarning { Code = "warning.test", Message = "warn" });

        var projected = envelope.WithoutResult();

        Assert.Null(projected.Result);
        Assert.Equal(envelope.OperationId, projected.OperationId);
        Assert.Equal(envelope.CapabilityId, projected.CapabilityId);
        Assert.Equal("test", projected.Metadata["source"]);
        Assert.Single(projected.Warnings);
    }

    [Fact]
    public void CapabilityDescriptorSupportsFlagsBasedExecutionModes()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "rimworld/load_game",
            SupportedModes = CapabilityExecutionMode.Wait | CapabilityExecutionMode.Queue
        };

        Assert.True(descriptor.SupportsMode(CapabilityExecutionMode.Wait));
        Assert.True(descriptor.SupportsMode(CapabilityExecutionMode.Queue));
        Assert.False(descriptor.SupportsMode(CapabilityExecutionMode.Immediate));
    }

    [Fact]
    public void CapabilityRegistrationCanRepresentDescriptorOnlyPackages()
    {
        var registration = new RimBridgeCapabilityRegistration(new CapabilityDescriptor
        {
            Id = "rimbridge.core/diagnostics/ping",
            ProviderId = "rimbridge.builtin"
        });

        Assert.False(registration.HasHandler);
        Assert.Equal("rimbridge.builtin", registration.Descriptor.ProviderId);
    }

    [Fact]
    public void ContractsSerializeWithStablePropertyNames()
    {
        var envelope = OperationEnvelope.Completed("op_789", "rimbridge/ping", DateTimeOffset.UtcNow, new { message = "pong" });
        var json = JsonSerializer.Serialize(envelope);

        Assert.Contains("\"OperationId\"", json);
        Assert.Contains("\"CapabilityId\"", json);
        Assert.Contains("\"Success\":true", json);
    }
}
