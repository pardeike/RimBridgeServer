using System;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class OperationRunnerTests
{
    [Fact]
    public void UsesDispatcherWhenMainThreadMarshallingIsEnabled()
    {
        var dispatcher = new FakeDispatcher();
        var runner = new OperationRunner(dispatcher);

        var envelope = runner.Run(() => new { message = "pong" }, new OperationExecutionOptions
        {
            CapabilityId = "rimbridge/ping",
            MarshalToMainThread = true
        });

        Assert.True(dispatcher.InvokeCalled);
        Assert.True(envelope.Success);
        Assert.Equal(OperationStatus.Completed, envelope.Status);
    }

    [Fact]
    public void SkipsDispatcherWhenMainThreadMarshallingIsDisabled()
    {
        var dispatcher = new FakeDispatcher();
        var runner = new OperationRunner(dispatcher);

        var envelope = runner.Run(() => "background", new OperationExecutionOptions
        {
            CapabilityId = "rimworld/take_screenshot",
            MarshalToMainThread = false
        });

        Assert.False(dispatcher.InvokeCalled);
        Assert.True(envelope.Success);
        Assert.Equal("background", envelope.Result);
    }

    [Fact]
    public void ConvertsExceptionsIntoFailedOperationEnvelopes()
    {
        var dispatcher = new FakeDispatcher();
        var runner = new OperationRunner(dispatcher);

        var envelope = runner.Run(() => throw new InvalidOperationException("boom"), new OperationExecutionOptions
        {
            CapabilityId = "rimworld/get_game_info",
            MarshalToMainThread = false,
            FailureCode = "operation.failed"
        });

        Assert.False(envelope.Success);
        Assert.Equal(OperationStatus.Failed, envelope.Status);
        Assert.Equal("operation.failed", envelope.Error.Code);
        Assert.Equal("boom", envelope.Error.Message);
    }

    [Fact]
    public void ReusesSuppliedOperationIdentity()
    {
        var dispatcher = new FakeDispatcher();
        var runner = new OperationRunner(dispatcher);
        var startedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);

        var envelope = runner.Run(() => "pong", new OperationExecutionOptions
        {
            OperationId = "op_external",
            CapabilityId = "rimbridge/ping",
            StartedAtUtc = startedAtUtc,
            MarshalToMainThread = false
        });

        Assert.Equal("op_external", envelope.OperationId);
        Assert.Equal(startedAtUtc, envelope.StartedAtUtc);
        Assert.True(envelope.DurationMs >= 0);
    }

    private sealed class FakeDispatcher : IGameThreadDispatcher
    {
        public bool InvokeCalled { get; private set; }

        public bool IsMainThread => false;

        public T Invoke<T>(Func<T> func, int timeoutMs)
        {
            InvokeCalled = true;
            return func();
        }

        public void Invoke(Action action, int timeoutMs)
        {
            InvokeCalled = true;
            action();
        }
    }
}
