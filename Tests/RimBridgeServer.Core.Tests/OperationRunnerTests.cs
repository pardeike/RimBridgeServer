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
    public void ConvertsTimeoutExceptionsIntoTimedOutOperationEnvelopes()
    {
        var dispatcher = new TimeoutDispatcher();
        var runner = new OperationRunner(dispatcher);

        var envelope = runner.Run(() => "never", new OperationExecutionOptions
        {
            CapabilityId = "rimworld/save_game",
            MarshalToMainThread = true,
            TimeoutCode = "capability.timed_out"
        });

        Assert.False(envelope.Success);
        Assert.Equal(OperationStatus.TimedOut, envelope.Status);
        Assert.Equal("capability.timed_out", envelope.Error.Code);
    }

    [Fact]
    public void ConvertsOperationCanceledExceptionsIntoCancelledOperationEnvelopes()
    {
        var dispatcher = new CancelledDispatcher();
        var runner = new OperationRunner(dispatcher);

        var envelope = runner.Run(() => "never", new OperationExecutionOptions
        {
            CapabilityId = "rimworld/save_game",
            MarshalToMainThread = true,
            CancellationCode = "capability.cancelled"
        });

        Assert.False(envelope.Success);
        Assert.Equal(OperationStatus.Cancelled, envelope.Status);
        Assert.Equal("capability.cancelled", envelope.Error.Code);
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

    private sealed class TimeoutDispatcher : IGameThreadDispatcher
    {
        public bool IsMainThread => false;

        public T Invoke<T>(Func<T> func, int timeoutMs)
        {
            throw new TimeoutException("dispatcher timeout");
        }

        public void Invoke(Action action, int timeoutMs)
        {
            throw new TimeoutException("dispatcher timeout");
        }
    }

    private sealed class CancelledDispatcher : IGameThreadDispatcher
    {
        public bool IsMainThread => false;

        public T Invoke<T>(Func<T> func, int timeoutMs)
        {
            throw new OperationCanceledException("dispatcher cancelled");
        }

        public void Invoke(Action action, int timeoutMs)
        {
            throw new OperationCanceledException("dispatcher cancelled");
        }
    }
}
