using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;
using RimBridgeServer.Extensions.Abstractions;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class CapabilityRegistryTests
{
    [Fact]
    public void ResolvesCapabilityByAlias()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new FakeProvider());

        var descriptor = registry.ResolveDescriptor("rimbridge/ping");

        Assert.Equal("rimbridge.core/diagnostics/ping", descriptor.Id);
    }

    [Fact]
    public void InvokesRegisteredCapabilityByAlias()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new FakeProvider());

        var envelope = registry.Invoke("rimbridge/ping");

        Assert.True(envelope.Success);
        Assert.Equal("rimbridge.core/diagnostics/ping", envelope.CapabilityId);
    }

    [Fact]
    public void RejectsDuplicateAliasesAcrossProviders()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new FakeProvider());

        var ex = Assert.Throws<System.InvalidOperationException>(() => registry.RegisterProvider(new DuplicateAliasProvider()));

        Assert.Contains("rimbridge/ping", ex.Message);
    }

    [Fact]
    public void RecordsOperationLifecycleInJournal()
    {
        var journal = new OperationJournal();
        var registry = new CapabilityRegistry(journal);
        registry.RegisterProvider(new FakeProvider());

        var envelope = registry.Invoke("rimbridge/ping");
        var tracked = journal.GetOperation(envelope.OperationId);
        var events = journal.GetRecentEvents();

        Assert.NotNull(tracked);
        Assert.Equal(OperationStatus.Completed, tracked.Status);
        Assert.Empty((Dictionary<string, object>)tracked.Metadata["arguments"]);
        Assert.Equal("rimbridge/ping", tracked.Metadata["requestedId"]);
        Assert.Equal("operation.completed", events[0].EventType);
        Assert.Equal("operation.started", events[1].EventType);
    }

    private sealed class FakeProvider : IRimBridgeCapabilityProvider
    {
        public string ProviderId => "fake.provider";

        public IEnumerable<RimBridgeCapabilityRegistration> GetCapabilities()
        {
            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "rimbridge.core/diagnostics/ping",
                    ProviderId = ProviderId,
                    Category = "diagnostics",
                    Aliases = ["rimbridge/ping"]
                },
                (_, _) => Task.FromResult(OperationEnvelope.Completed("op_1", "rimbridge.core/diagnostics/ping", System.DateTimeOffset.UtcNow, new { message = "pong" })));
        }
    }

    private sealed class DuplicateAliasProvider : IRimBridgeCapabilityProvider
    {
        public string ProviderId => "duplicate.provider";

        public IEnumerable<RimBridgeCapabilityRegistration> GetCapabilities()
        {
            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "rimbridge.core/other/ping",
                    ProviderId = ProviderId,
                    Category = "diagnostics",
                    Aliases = ["rimbridge/ping"]
                },
                (_, _) => Task.FromResult(OperationEnvelope.Completed("op_2", "rimbridge.core/other/ping", System.DateTimeOffset.UtcNow, new { message = "pong" })));
        }
    }
}
