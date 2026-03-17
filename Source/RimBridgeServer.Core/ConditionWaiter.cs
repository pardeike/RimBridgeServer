using System;
using System.Diagnostics;
using System.Threading;

namespace RimBridgeServer.Core;

public sealed class WaitOptions
{
    public int TimeoutMs { get; set; } = 30000;

    public int PollIntervalMs { get; set; } = 100;

    public string TimeoutMessage { get; set; } = "Timed out waiting for the condition.";

    public Func<Exception, WaitProbeResult> HandleProbeException { get; set; }
}

public sealed class WaitProbeResult
{
    public bool IsSatisfied { get; set; }

    public string Message { get; set; } = string.Empty;

    public object Snapshot { get; set; }
}

public sealed class WaitOutcome
{
    public bool Satisfied { get; set; }

    public int Attempts { get; set; }

    public long ElapsedMs { get; set; }

    public int ProbeFailureCount { get; set; }

    public string Message { get; set; } = string.Empty;

    public object Snapshot { get; set; }
}

public sealed class ConditionWaiter
{
    public WaitOutcome WaitUntil(Func<WaitProbeResult> probe, WaitOptions options, CancellationToken cancellationToken = default)
    {
        if (probe == null)
            throw new ArgumentNullException(nameof(probe));
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (options.TimeoutMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options.TimeoutMs));
        if (options.PollIntervalMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options.PollIntervalMs));

        var stopwatch = Stopwatch.StartNew();
        WaitProbeResult lastProbe = null;
        var attempts = 0;
        var probeFailureCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;

            try
            {
                lastProbe = probe() ?? new WaitProbeResult();
            }
            catch (Exception ex)
            {
                if (options.HandleProbeException == null)
                    throw;

                lastProbe = options.HandleProbeException(ex);
                if (lastProbe == null)
                    throw;

                probeFailureCount++;
            }

            if (lastProbe.IsSatisfied)
            {
                return new WaitOutcome
                {
                    Satisfied = true,
                    Attempts = attempts,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    ProbeFailureCount = probeFailureCount,
                    Message = string.IsNullOrWhiteSpace(lastProbe.Message) ? "Condition satisfied." : lastProbe.Message,
                    Snapshot = lastProbe.Snapshot
                };
            }

            if (stopwatch.ElapsedMilliseconds >= options.TimeoutMs)
            {
                return new WaitOutcome
                {
                    Satisfied = false,
                    Attempts = attempts,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    ProbeFailureCount = probeFailureCount,
                    Message = string.IsNullOrWhiteSpace(options.TimeoutMessage) ? lastProbe.Message : options.TimeoutMessage,
                    Snapshot = lastProbe.Snapshot
                };
            }

            if (options.PollIntervalMs > 0)
                SleepUntilNextProbe(options.PollIntervalMs, cancellationToken);
        }
    }

    private static void SleepUntilNextProbe(int pollIntervalMs, CancellationToken cancellationToken)
    {
        if (pollIntervalMs <= 0)
            return;

        if (!cancellationToken.CanBeCanceled)
        {
            Thread.Sleep(pollIntervalMs);
            return;
        }

        if (cancellationToken.WaitHandle.WaitOne(pollIntervalMs))
            cancellationToken.ThrowIfCancellationRequested();
    }
}
