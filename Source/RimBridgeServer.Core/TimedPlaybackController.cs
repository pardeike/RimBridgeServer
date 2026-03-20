using System;

namespace RimBridgeServer.Core;

internal sealed class TimedPlaybackState
{
    public bool Available { get; set; } = true;

    public bool Paused { get; set; }

    public long TickCount { get; set; }

    public object SessionToken { get; set; }

    public object Snapshot { get; set; }

    public string Message { get; set; } = string.Empty;
}

internal sealed class TimedPlaybackResult
{
    public bool Success { get; set; }

    public int Attempts { get; set; }

    public long ElapsedMs { get; set; }

    public int ProbeFailureCount { get; set; }

    public string LastProbeError { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public long StartTick { get; set; }

    public long EndTick { get; set; }

    public long AdvancedTicks { get; set; }

    public bool InitiallyPaused { get; set; }

    public bool PausedAtEnd { get; set; }

    public object Snapshot { get; set; }
}

internal enum TimedPlaybackCompletionKind
{
    None = 0,
    DurationElapsed,
    ExternalPause,
    SessionChanged,
    Unavailable
}

internal sealed class TimedPlaybackProbeSnapshot
{
    public TimedPlaybackCompletionKind CompletionKind { get; set; }

    public long ElapsedMs { get; set; }

    public TimedPlaybackState State { get; set; }
}

internal sealed class TimedPlaybackController
{
    private readonly ConditionWaiter _waiter;

    public TimedPlaybackController(ConditionWaiter waiter = null)
    {
        _waiter = waiter ?? new ConditionWaiter();
    }

    public TimedPlaybackResult PlayFor(
        int durationMs,
        int pollIntervalMs,
        Func<long> getElapsedMs,
        Func<TimedPlaybackState> readState,
        Action startPlayback,
        Action pausePlayback)
    {
        if (getElapsedMs == null)
            throw new ArgumentNullException(nameof(getElapsedMs));
        if (readState == null)
            throw new ArgumentNullException(nameof(readState));
        if (startPlayback == null)
            throw new ArgumentNullException(nameof(startPlayback));
        if (pausePlayback == null)
            throw new ArgumentNullException(nameof(pausePlayback));

        if (durationMs <= 0)
        {
            return new TimedPlaybackResult
            {
                Success = false,
                ElapsedMs = 0,
                Message = "durationMs must be greater than 0."
            };
        }

        if (pollIntervalMs < 0)
        {
            return new TimedPlaybackResult
            {
                Success = false,
                ElapsedMs = 0,
                Message = "pollIntervalMs cannot be negative."
            };
        }

        var initialState = NormalizeState(readState());
        if (!initialState.Available)
        {
            return new TimedPlaybackResult
            {
                Success = false,
                ElapsedMs = 0,
                InitiallyPaused = initialState.Paused,
                PausedAtEnd = initialState.Paused,
                StartTick = initialState.TickCount,
                EndTick = initialState.TickCount,
                AdvancedTicks = 0,
                Message = string.IsNullOrWhiteSpace(initialState.Message)
                    ? "Playback state is unavailable."
                    : initialState.Message,
                Snapshot = initialState.Snapshot
            };
        }

        var started = false;
        var startState = initialState;
        var finalState = initialState;
        WaitOutcome waitOutcome = null;
        Exception pauseException = null;

        try
        {
            startPlayback();
            started = true;
            startState = NormalizeState(readState());

            waitOutcome = _waiter.WaitUntil(() =>
            {
                var current = NormalizeState(readState());
                var elapsedMs = Math.Max(0L, getElapsedMs());
                var completionKind = ResolveCompletionKind(startState.SessionToken, current, elapsedMs, durationMs);
                var shouldStop = completionKind != TimedPlaybackCompletionKind.None;
                var message = BuildProbeMessage(current, elapsedMs, durationMs, completionKind);

                return new WaitProbeResult
                {
                    IsSatisfied = shouldStop,
                    Message = message,
                    Snapshot = new TimedPlaybackProbeSnapshot
                    {
                        CompletionKind = completionKind,
                        ElapsedMs = elapsedMs,
                        State = current
                    }
                };
            }, new WaitOptions
            {
                TimeoutMs = ComputeTimeoutMs(durationMs, pollIntervalMs),
                PollIntervalMs = pollIntervalMs,
                TimeoutMessage = $"Timed out while waiting to play for {durationMs}ms.",
                HandleProbeException = ex => ex is TimeoutException
                    ? new WaitProbeResult
                    {
                        IsSatisfied = false,
                        Message = "Retrying after main-thread timeout."
                    }
                    : null
            });
        }
        finally
        {
            if (started)
            {
                try
                {
                    pausePlayback();
                }
                catch (Exception ex)
                {
                    pauseException = ex;
                }
            }

            finalState = SafeReadState(readState, finalState);
        }

        var elapsed = Math.Max(0L, getElapsedMs());
        var probeSnapshot = waitOutcome?.Snapshot as TimedPlaybackProbeSnapshot;
        var completionKind = probeSnapshot?.CompletionKind ?? TimedPlaybackCompletionKind.None;
        var success = completionKind == TimedPlaybackCompletionKind.DurationElapsed && pauseException == null;
        var message = BuildMessage(durationMs, waitOutcome, finalState, pauseException, success, probeSnapshot);

        return new TimedPlaybackResult
        {
            Success = success,
            Attempts = waitOutcome?.Attempts ?? 0,
            ElapsedMs = elapsed,
            ProbeFailureCount = waitOutcome?.ProbeFailureCount ?? 0,
            LastProbeError = waitOutcome?.LastProbeError ?? string.Empty,
            Message = message,
            InitiallyPaused = initialState.Paused,
            PausedAtEnd = finalState.Paused,
            StartTick = startState.TickCount,
            EndTick = finalState.TickCount,
            AdvancedTicks = Math.Max(0L, finalState.TickCount - startState.TickCount),
            Snapshot = finalState.Snapshot ?? probeSnapshot?.State?.Snapshot ?? startState.Snapshot
        };
    }

    private static TimedPlaybackState NormalizeState(TimedPlaybackState state)
    {
        return state ?? new TimedPlaybackState
        {
            Available = false,
            Message = "Playback state is unavailable."
        };
    }

    private static TimedPlaybackState SafeReadState(Func<TimedPlaybackState> readState, TimedPlaybackState fallback)
    {
        try
        {
            return NormalizeState(readState());
        }
        catch
        {
            return fallback;
        }
    }

    private static int ComputeTimeoutMs(int durationMs, int pollIntervalMs)
    {
        var slackMs = Math.Max(2000L, (pollIntervalMs * 4L) + 500L);
        var timeoutMs = durationMs + slackMs;
        return timeoutMs >= int.MaxValue
            ? int.MaxValue
            : (int)timeoutMs;
    }

    private static string BuildMessage(
        int durationMs,
        WaitOutcome waitOutcome,
        TimedPlaybackState finalState,
        Exception pauseException,
        bool success,
        TimedPlaybackProbeSnapshot probeSnapshot)
    {
        if (pauseException != null)
            return $"Game played for {durationMs}ms but failed to pause cleanly: {pauseException.Message}";

        if (probeSnapshot?.CompletionKind == TimedPlaybackCompletionKind.ExternalPause)
        {
            return $"Game was paused externally after {probeSnapshot.ElapsedMs}ms, before the requested {durationMs}ms elapsed.";
        }

        if (probeSnapshot?.CompletionKind == TimedPlaybackCompletionKind.SessionChanged)
        {
            return $"Game session changed after {probeSnapshot.ElapsedMs}ms, before the requested {durationMs}ms elapsed.";
        }

        if (probeSnapshot?.CompletionKind == TimedPlaybackCompletionKind.Unavailable || !finalState.Available)
        {
            var messageSource = probeSnapshot?.State ?? finalState;
            return string.IsNullOrWhiteSpace(messageSource.Message)
                ? "Playback state became unavailable before the play window finished."
                : messageSource.Message;
        }

        if (!success)
            return waitOutcome?.Message ?? $"Timed out while waiting to play for {durationMs}ms.";

        return $"Game played for {durationMs}ms and is now paused.";
    }

    private static TimedPlaybackCompletionKind ResolveCompletionKind(object startSessionToken, TimedPlaybackState current, long elapsedMs, int durationMs)
    {
        if (!current.Available)
            return TimedPlaybackCompletionKind.Unavailable;
        if (!SameSession(startSessionToken, current.SessionToken))
            return TimedPlaybackCompletionKind.SessionChanged;
        if (current.Paused && elapsedMs < durationMs)
            return TimedPlaybackCompletionKind.ExternalPause;
        if (elapsedMs >= durationMs)
            return TimedPlaybackCompletionKind.DurationElapsed;

        return TimedPlaybackCompletionKind.None;
    }

    private static string BuildProbeMessage(TimedPlaybackState current, long elapsedMs, int durationMs, TimedPlaybackCompletionKind completionKind)
    {
        return completionKind switch
        {
            TimedPlaybackCompletionKind.Unavailable => string.IsNullOrWhiteSpace(current.Message)
                ? "Playback state became unavailable."
                : current.Message,
            TimedPlaybackCompletionKind.ExternalPause => $"Game was paused externally after {elapsedMs}ms.",
            TimedPlaybackCompletionKind.SessionChanged => $"Game session changed after {elapsedMs}ms.",
            TimedPlaybackCompletionKind.DurationElapsed => $"Played for {Math.Min(elapsedMs, durationMs)} of {durationMs}ms.",
            _ => $"Played for {Math.Min(elapsedMs, durationMs)} of {durationMs}ms."
        };
    }

    private static bool SameSession(object left, object right)
    {
        return ReferenceEquals(left, right) || Equals(left, right);
    }
}
