using System;
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

    [Fact]
    public void ConvertsHandlerTimeoutsIntoTimedOutOperations()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ThrowingProvider("timeout", () => throw new TimeoutException("too slow")));

        var envelope = registry.Invoke("rimbridge/timeout");

        Assert.False(envelope.Success);
        Assert.Equal(OperationStatus.TimedOut, envelope.Status);
        Assert.Equal("capability.timed_out", envelope.Error.Code);
    }

    [Fact]
    public void ConvertsHandlerCancellationsIntoCancelledOperations()
    {
        var registry = new CapabilityRegistry();
        registry.RegisterProvider(new ThrowingProvider("cancelled", () => throw new OperationCanceledException("stopped")));

        var envelope = registry.Invoke("rimbridge/cancelled");

        Assert.False(envelope.Success);
        Assert.Equal(OperationStatus.Cancelled, envelope.Status);
        Assert.Equal("capability.cancelled", envelope.Error.Code);
    }

    [Fact]
    public void RecordsParentAndScriptCorrelationMetadata()
    {
        var journal = new OperationJournal();
        var registry = new CapabilityRegistry(journal);
        registry.RegisterProvider(new FakeProvider());

        using (OperationContext.PushOperation("op_parent", "rimbridge.core/run_script"))
        using (OperationContext.PushMetadata(scriptStatementId: "step-1", scriptStepId: "call-1", scriptCall: "rimbridge/ping"))
        {
            var envelope = registry.Invoke("rimbridge/ping");
            var tracked = journal.GetOperation(envelope.OperationId, includeResult: false);

            Assert.Equal("op_parent", tracked.Metadata["parentOperationId"]);
            Assert.Equal("rimbridge.core/run_script", tracked.Metadata["parentCapabilityId"]);
            Assert.Equal("step-1", tracked.Metadata["scriptStatementId"]);
            Assert.Equal("call-1", tracked.Metadata["scriptStepId"]);
            Assert.Equal("rimbridge/ping", tracked.Metadata["scriptCall"]);
        }
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

    private sealed class ThrowingProvider(string alias, System.Action action) : IRimBridgeCapabilityProvider
    {
        public string ProviderId => "throwing.provider";

        public IEnumerable<RimBridgeCapabilityRegistration> GetCapabilities()
        {
            yield return new RimBridgeCapabilityRegistration(
                new CapabilityDescriptor
                {
                    Id = "throwing.provider/" + alias,
                    ProviderId = ProviderId,
                    Category = "diagnostics",
                    Aliases = ["rimbridge/" + alias]
                },
                (_, _) =>
                {
                    action();
                    return Task.FromResult(OperationEnvelope.Completed("op_throwing", "throwing.provider/" + alias, System.DateTimeOffset.UtcNow, new { }));
                });
        }
    }
}
