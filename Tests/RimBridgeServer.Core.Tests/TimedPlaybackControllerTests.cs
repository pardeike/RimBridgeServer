using System;
using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class TimedPlaybackControllerTests
{
    [Fact]
    public void RejectsNonPositiveDurationWithoutStartingPlayback()
    {
        var started = false;
        var paused = false;
        var controller = new TimedPlaybackController();

        var result = controller.PlayFor(
            durationMs: 0,
            pollIntervalMs: 25,
            getElapsedMs: () => 0,
            readState: () => new TimedPlaybackState
            {
                Available = true,
                Paused = true,
                TickCount = 12
            },
            startPlayback: () => started = true,
            pausePlayback: () => paused = true);

        Assert.False(result.Success);
        Assert.Equal("durationMs must be greater than 0.", result.Message);
        Assert.False(started);
        Assert.False(paused);
    }

    [Fact]
    public void PlaysUntilDurationElapsesThenPauses()
    {
        var paused = true;
        long tickCount = 100;
        long elapsedMs = 0;
        var sessionToken = new object();
        var controller = new TimedPlaybackController();

        TimedPlaybackState ReadState()
        {
            if (!paused)
            {
                tickCount += 5;
                elapsedMs += 25;
            }

            return new TimedPlaybackState
            {
                Available = true,
                Paused = paused,
                TickCount = tickCount,
                SessionToken = sessionToken,
                Snapshot = new { tickCount, elapsedMs }
            };
        }

        var result = controller.PlayFor(
            durationMs: 100,
            pollIntervalMs: 0,
            getElapsedMs: () => elapsedMs,
            readState: ReadState,
            startPlayback: () => paused = false,
            pausePlayback: () => paused = true);

        Assert.True(result.Success);
        Assert.True(result.InitiallyPaused);
        Assert.True(result.PausedAtEnd);
        Assert.Equal(105, result.StartTick);
        Assert.Equal(120, result.EndTick);
        Assert.Equal(15, result.AdvancedTicks);
        Assert.Equal(100, result.ElapsedMs);
        Assert.Equal("Game played for 100ms and is now paused.", result.Message);
    }

    [Fact]
    public void EndsImmediatelyWhenPlaybackIsPausedExternally()
    {
        var paused = true;
        long tickCount = 60;
        long elapsedMs = 0;
        var probeCount = 0;
        var sessionToken = new object();
        var controller = new TimedPlaybackController();

        TimedPlaybackState ReadState()
        {
            probeCount++;
            if (probeCount >= 4)
                paused = true;

            if (!paused)
            {
                tickCount += 4;
                elapsedMs += 20;
            }

            return new TimedPlaybackState
            {
                Available = true,
                Paused = paused,
                TickCount = tickCount,
                SessionToken = sessionToken
            };
        }

        var result = controller.PlayFor(
            durationMs: 200,
            pollIntervalMs: 0,
            getElapsedMs: () => elapsedMs,
            readState: ReadState,
            startPlayback: () => paused = false,
            pausePlayback: () => paused = true);

        Assert.False(result.Success);
        Assert.Equal(40, result.ElapsedMs);
        Assert.Equal("Game was paused externally after 40ms, before the requested 200ms elapsed.", result.Message);
        Assert.True(result.PausedAtEnd);
    }

    [Fact]
    public void EndsImmediatelyWhenCurrentGameSessionChanges()
    {
        var paused = true;
        long tickCount = 40;
        long elapsedMs = 0;
        var probeCount = 0;
        var initialSession = new object();
        var replacementSession = new object();
        var sessionToken = initialSession;
        var controller = new TimedPlaybackController();

        TimedPlaybackState ReadState()
        {
            probeCount++;
            if (probeCount >= 4)
                sessionToken = replacementSession;

            if (!paused && ReferenceEquals(sessionToken, initialSession))
            {
                tickCount += 3;
                elapsedMs += 20;
            }

            return new TimedPlaybackState
            {
                Available = true,
                Paused = paused,
                TickCount = tickCount,
                SessionToken = sessionToken
            };
        }

        var result = controller.PlayFor(
            durationMs: 200,
            pollIntervalMs: 0,
            getElapsedMs: () => elapsedMs,
            readState: ReadState,
            startPlayback: () => paused = false,
            pausePlayback: () => paused = true);

        Assert.False(result.Success);
        Assert.Equal(40, result.ElapsedMs);
        Assert.Equal("Game session changed after 40ms, before the requested 200ms elapsed.", result.Message);
        Assert.True(result.EndTick >= result.StartTick);
    }

    [Fact]
    public void ReturnsFailureWhenPlaybackReturnsToMainMenu()
    {
        var paused = true;
        long tickCount = 40;
        long elapsedMs = 0;
        var probeCount = 0;
        var sessionToken = new object();
        var controller = new TimedPlaybackController();

        TimedPlaybackState ReadState()
        {
            probeCount++;
            if (!paused)
            {
                tickCount += 3;
                elapsedMs += 20;
            }

            if (probeCount >= 4)
            {
                return new TimedPlaybackState
                {
                    Available = false,
                    Paused = paused,
                    TickCount = tickCount,
                    Message = "RimWorld returned to the main menu."
                };
            }

            return new TimedPlaybackState
            {
                Available = true,
                Paused = paused,
                TickCount = tickCount,
                SessionToken = sessionToken
            };
        }

        var result = controller.PlayFor(
            durationMs: 200,
            pollIntervalMs: 0,
            getElapsedMs: () => elapsedMs,
            readState: ReadState,
            startPlayback: () => paused = false,
            pausePlayback: () => paused = true);

        Assert.False(result.Success);
        Assert.Equal("RimWorld returned to the main menu.", result.Message);
        Assert.True(result.EndTick >= result.StartTick);
    }

    [Fact]
    public void RetriesAfterHandledMainThreadTimeouts()
    {
        var paused = true;
        long tickCount = 5;
        long elapsedMs = 0;
        var readAttempts = 0;
        var sessionToken = new object();
        var controller = new TimedPlaybackController();

        TimedPlaybackState ReadState()
        {
            readAttempts++;
            if (readAttempts == 3)
                throw new TimeoutException("main thread busy");

            if (!paused)
            {
                tickCount += 4;
                elapsedMs += 20;
            }

            return new TimedPlaybackState
            {
                Available = true,
                Paused = paused,
                TickCount = tickCount,
                SessionToken = sessionToken
            };
        }

        var result = controller.PlayFor(
            durationMs: 80,
            pollIntervalMs: 0,
            getElapsedMs: () => elapsedMs,
            readState: ReadState,
            startPlayback: () => paused = false,
            pausePlayback: () => paused = true);

        Assert.True(result.Success);
        Assert.Equal(1, result.ProbeFailureCount);
        Assert.Equal("main thread busy", result.LastProbeError);
        Assert.True(result.AdvancedTicks > 0);
    }
}
