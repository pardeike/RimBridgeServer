using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimWorld;
using RimBridgeServer.Contracts;
using RimBridgeServer.Core;
using RimBridgeServer.Sdk;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgeSdkHost
{
    private sealed class Scope : IDisposable
    {
        private readonly IRimBridgeContext _previous;
        private bool _disposed;

        public Scope(IRimBridgeContext previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            CurrentContext.Value = _previous;
            _disposed = true;
        }
    }

    private sealed class Host : IRimBridgeHost
    {
        public IRimBridgeContext Current
        {
            get
            {
                var current = CurrentContext.Value;
                if (current != null)
                    return current;

                EnsureReady();
                return CreateContext(OperationContext.Capture());
            }
        }
    }

    private static readonly AsyncLocal<IRimBridgeContext> CurrentContext = new();
    private static CapabilityRegistry _registry;
    private static bool _registrationComplete;

    public static void Initialize(CapabilityRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _registrationComplete = false;
        RimBridge.SetHost(new Host());
    }

    public static void MarkRegistrationComplete()
    {
        _registrationComplete = true;
    }

    public static IRimBridgeContext Capture()
    {
        return CurrentContext.Value;
    }

    public static IDisposable Push(IRimBridgeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    public static IRimBridgeContext CreateContext(OperationContextSnapshot snapshot)
    {
        EnsureReady();
        snapshot ??= new OperationContextSnapshot();
        return new RimBridgeContext(
            snapshot.OperationId ?? string.Empty,
            snapshot.CapabilityId ?? string.Empty,
            new RimBridgeToolClient(_registry, () => _registrationComplete),
            new RimBridgeGameClock(),
            new RimBridgeMainThreadClient());
    }

    private static void EnsureReady()
    {
        if (_registry == null)
            throw new RimBridgeNotReadyException();
    }
}

internal sealed class RimBridgeContext : IRimBridgeContext
{
    public RimBridgeContext(
        string operationId,
        string capabilityId,
        IRimBridgeToolClient tools,
        IRimBridgeGameClock game,
        IRimBridgeMainThread mainThread)
    {
        OperationId = operationId ?? string.Empty;
        CapabilityId = capabilityId ?? string.Empty;
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
        Game = game ?? throw new ArgumentNullException(nameof(game));
        MainThread = mainThread ?? throw new ArgumentNullException(nameof(mainThread));
    }

    public string OperationId { get; }

    public string CapabilityId { get; }

    public IRimBridgeToolClient Tools { get; }

    public IRimBridgeGameClock Game { get; }

    public IRimBridgeMainThread MainThread { get; }
}

internal sealed class RimBridgeToolClient : IRimBridgeToolClient
{
    private readonly CapabilityRegistry _registry;
    private readonly Func<bool> _isRegistrationComplete;

    public RimBridgeToolClient(CapabilityRegistry registry, Func<bool> isRegistrationComplete)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _isRegistrationComplete = isRegistrationComplete ?? throw new ArgumentNullException(nameof(isRegistrationComplete));
    }

    public IReadOnlyList<RimBridgeToolDescriptor> List(RimBridgeToolQuery query = null)
    {
        EnsureRegistrationComplete();
        IEnumerable<CapabilityDescriptor> descriptors = _registry.GetCapabilities();
        if (query != null)
        {
            if (string.IsNullOrWhiteSpace(query.Category) == false)
                descriptors = descriptors.Where(descriptor => string.Equals(descriptor.Category, query.Category, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(query.ProviderId) == false)
                descriptors = descriptors.Where(descriptor => string.Equals(descriptor.ProviderId, query.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(query.Source) == false)
                descriptors = descriptors.Where(descriptor => string.Equals(descriptor.Source.ToString(), query.Source, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(query.Text) == false)
            {
                var text = query.Text.Trim();
                descriptors = descriptors.Where(descriptor =>
                    Contains(descriptor.Id, text)
                    || Contains(descriptor.Title, text)
                    || Contains(descriptor.Summary, text)
                    || descriptor.Aliases.Any(alias => Contains(alias, text)));
            }
        }

        return descriptors
            .Select(ToSdkDescriptor)
            .ToList();
    }

    public RimBridgeToolDescriptor Get(string idOrAlias)
    {
        EnsureRegistrationComplete();
        return ToSdkDescriptor(_registry.ResolveDescriptor(idOrAlias));
    }

    public bool Exists(string idOrAlias)
    {
        if (string.IsNullOrWhiteSpace(idOrAlias))
            return false;

        EnsureRegistrationComplete();
        try
        {
            _registry.ResolveDescriptor(idOrAlias);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<RimBridgeToolCallResult<object>> CallAsync(string idOrAlias, object args = null, RimBridgeToolCallOptions options = null, CancellationToken cancellationToken = default)
    {
        return CallAsync<object>(idOrAlias, args, options, cancellationToken);
    }

    public async Task<RimBridgeToolCallResult<T>> CallAsync<T>(string idOrAlias, object args = null, RimBridgeToolCallOptions options = null, CancellationToken cancellationToken = default)
    {
        EnsureRegistrationComplete();
        var arguments = NormalizeArguments(args);
        if (options?.TimeoutMs > 0)
            arguments["_rimBridgeTimeoutMs"] = options.TimeoutMs;

        var envelope = await Task.Run(() => _registry.InvokeAsync(idOrAlias, arguments, cancellationToken), CancellationToken.None)
            .ConfigureAwait(false);

        return new RimBridgeToolCallResult<T>
        {
            Success = envelope.Success,
            OperationId = envelope.OperationId,
            CapabilityId = envelope.CapabilityId,
            Status = envelope.Status.ToString().ToLowerInvariant(),
            Result = ConvertResult<T>(envelope.Result),
            Error = ToSdkError(envelope.Error)
        };
    }

    public async Task<RimBridgeOperationInfo> QueueAsync(string idOrAlias, object args = null, RimBridgeToolCallOptions options = null, CancellationToken cancellationToken = default)
    {
        EnsureRegistrationComplete();
        var arguments = NormalizeArguments(args);
        if (options?.TimeoutMs > 0)
            arguments["_rimBridgeTimeoutMs"] = options.TimeoutMs;

        var envelope = await _registry.QueueAsync(idOrAlias, arguments, cancellationToken).ConfigureAwait(false);
        return ToSdkOperation(envelope);
    }

    private void EnsureRegistrationComplete()
    {
        if (_isRegistrationComplete() == false)
            throw new RimBridgeNotReadyException();
    }

    private static bool Contains(string value, string text)
    {
        return value?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Dictionary<string, object> NormalizeArguments(object args)
    {
        return ReflectedCapabilityBinding.NormalizeInvocationArguments(args);
    }

    private static T ConvertResult<T>(object value)
    {
        if (value == null)
            return default;
        if (value is T typed)
            return typed;

        return JToken.FromObject(value).ToObject<T>();
    }

    private static RimBridgeToolDescriptor ToSdkDescriptor(CapabilityDescriptor descriptor)
    {
        return new RimBridgeToolDescriptor
        {
            Id = descriptor.Id,
            ProviderId = descriptor.ProviderId,
            Category = descriptor.Category,
            Title = descriptor.Title,
            Summary = descriptor.Summary,
            Source = descriptor.Source.ToString(),
            ExecutionKind = descriptor.ExecutionKind.ToString(),
            SupportedModes = ExpandSupportedModes(descriptor.SupportedModes),
            EmitsEvents = descriptor.EmitsEvents,
            ResultType = descriptor.ResultType,
            Aliases = descriptor.Aliases.ToList(),
            Parameters = descriptor.Parameters
                .Select(parameter => new RimBridgeToolParameter
                {
                    Name = parameter.Name,
                    ParameterType = parameter.ParameterType,
                    Description = parameter.Description,
                    Required = parameter.Required,
                    DefaultValue = parameter.DefaultValue
                })
                .ToList()
        };
    }

    private static IReadOnlyList<string> ExpandSupportedModes(CapabilityExecutionMode supportedModes)
    {
        return Enum.GetValues(typeof(CapabilityExecutionMode))
            .Cast<CapabilityExecutionMode>()
            .Where(mode => mode != CapabilityExecutionMode.None && (supportedModes & mode) == mode)
            .Select(mode => mode.ToString())
            .ToList();
    }

    private static RimBridgeOperationInfo ToSdkOperation(OperationEnvelope envelope)
    {
        return new RimBridgeOperationInfo
        {
            OperationId = envelope.OperationId,
            CapabilityId = envelope.CapabilityId,
            Status = envelope.Status.ToString().ToLowerInvariant(),
            Success = envelope.Success,
            StartedAtUtc = envelope.StartedAtUtc,
            CompletedAtUtc = envelope.CompletedAtUtc,
            DurationMs = envelope.DurationMs,
            HasResult = envelope.HasResult,
            Error = ToSdkError(envelope.Error)
        };
    }

    private static RimBridgeOperationError ToSdkError(OperationError error)
    {
        if (error == null)
            return null;

        return new RimBridgeOperationError
        {
            Code = error.Code,
            Message = error.Message,
            ExceptionType = error.ExceptionType,
            Details = error.Details
        };
    }
}

internal sealed class RimBridgeMainThreadClient : IRimBridgeMainThread
{
    public bool IsMainThread => RimBridgeMainThread.IsMainThread;

    public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        return RimBridgeMainThread.InvokeAsync(func, cancellationToken);
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        return RimBridgeMainThread.InvokeAsync(action, cancellationToken);
    }
}

internal sealed class RimBridgeGameClock : IRimBridgeGameClock
{
    private static readonly SemaphoreSlim GameControlLease = new(1, 1);

    public Task NextFrameAsync(CancellationToken cancellationToken = default)
    {
        return RimBridgeFrameClock.NextFrameAsync(cancellationToken);
    }

    public async Task FramesAsync(int count, CancellationToken cancellationToken = default)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        for (var i = 0; i < count; i++)
            await NextFrameAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task<RimBridgeTickResult> StepTicksAsync(int ticks, RimBridgeTickOptions options = null, CancellationToken cancellationToken = default)
    {
        options ??= new RimBridgeTickOptions();
        await AcquireLeaseAsync(options.TimeoutMs, options.FailIfBusy, cancellationToken).ConfigureAwait(true);
        try
        {
            var start = RimWorldTickStepper.Start(ticks, options.TimeoutMs, options.PauseFirst, options.PlaySound);
            if (start.Status != "active")
                return ToSdkTickResult(start);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await NextFrameAsync(cancellationToken).ConfigureAwait(true);
                var current = RimWorldTickStepper.GetSnapshot(start.StepId);
                if (current.Status != "active")
                    return ToSdkTickResult(current);
            }
        }
        finally
        {
            GameControlLease.Release();
        }
    }

    public async Task<RimBridgeTickResult> RunForTicksAsync(int ticks, RimBridgeRunTicksOptions options = null, CancellationToken cancellationToken = default)
    {
        if (ticks <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticks));

        options ??= new RimBridgeRunTicksOptions();
        await AcquireLeaseAsync(options.TimeoutMs, options.FailIfBusy, cancellationToken).ConfigureAwait(true);
        try
        {
            var startFrame = Time.frameCount;
            var startTicks = CurrentTicksGame();
            var targetTicks = startTicks + ticks;
            var stopwatch = Stopwatch.StartNew();
            if (options.ForceNormalSpeed && Current.Game != null && Find.TickManager != null && Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;

            while (CurrentTicksGame() < targetTicks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (options.TimeoutMs > 0 && stopwatch.ElapsedMilliseconds > options.TimeoutMs)
                {
                    if (options.PauseWhenDone && Current.Game != null && Find.TickManager != null)
                        Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

                    return new RimBridgeTickResult
                    {
                        Success = false,
                        Status = "timed_out",
                        Message = $"Timed out after advancing {CurrentTicksGame() - startTicks} of {ticks} requested live tick(s).",
                        RequestedTicks = ticks,
                        CompletedTicks = Math.Max(0, CurrentTicksGame() - startTicks),
                        StartTicksGame = startTicks,
                        EndTicksGame = CurrentTicksGame(),
                        AdvancedTicks = Math.Max(0, CurrentTicksGame() - startTicks),
                        AdvancedFrames = Math.Max(0, Time.frameCount - startFrame)
                    };
                }

                await NextFrameAsync(cancellationToken).ConfigureAwait(true);
            }

            if (options.PauseWhenDone && Current.Game != null && Find.TickManager != null)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;

            return new RimBridgeTickResult
            {
                Success = true,
                Status = "completed",
                Message = $"Advanced {CurrentTicksGame() - startTicks} live game tick(s).",
                RequestedTicks = ticks,
                CompletedTicks = Math.Max(0, CurrentTicksGame() - startTicks),
                StartTicksGame = startTicks,
                EndTicksGame = CurrentTicksGame(),
                AdvancedTicks = Math.Max(0, CurrentTicksGame() - startTicks),
                AdvancedFrames = Math.Max(0, Time.frameCount - startFrame)
            };
        }
        finally
        {
            GameControlLease.Release();
        }
    }

    public async Task<RimBridgeWaitResult> RunUntilAsync(Func<bool> predicate, RimBridgeWaitOptions options = null, CancellationToken cancellationToken = default)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        options ??= new RimBridgeWaitOptions();
        await AcquireLeaseAsync(options.TimeoutMs, options.FailIfBusy, cancellationToken).ConfigureAwait(true);
        try
        {
            var startFrame = Time.frameCount;
            var startTicks = CurrentTicksGame();
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (predicate())
                {
                    return new RimBridgeWaitResult
                    {
                        Success = true,
                        Status = "completed",
                        Message = "Condition was satisfied.",
                        ElapsedFrames = Math.Max(0, Time.frameCount - startFrame),
                        StartTicksGame = startTicks,
                        EndTicksGame = CurrentTicksGame(),
                        AdvancedTicks = Math.Max(0, CurrentTicksGame() - startTicks)
                    };
                }

                if (options.TimeoutMs > 0 && stopwatch.ElapsedMilliseconds > options.TimeoutMs)
                {
                    return new RimBridgeWaitResult
                    {
                        Success = false,
                        Status = "timed_out",
                        Message = "Timed out waiting for condition.",
                        ElapsedFrames = Math.Max(0, Time.frameCount - startFrame),
                        StartTicksGame = startTicks,
                        EndTicksGame = CurrentTicksGame(),
                        AdvancedTicks = Math.Max(0, CurrentTicksGame() - startTicks)
                    };
                }

                await NextFrameAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            GameControlLease.Release();
        }
    }

    private static async Task AcquireLeaseAsync(int timeoutMs, bool failIfBusy, CancellationToken cancellationToken)
    {
        if (failIfBusy)
        {
            if (await GameControlLease.WaitAsync(0, cancellationToken).ConfigureAwait(true) == false)
                throw new InvalidOperationException("Another RimBridge game-control flow is already active.");
            return;
        }

        if (timeoutMs > 0)
        {
            if (await GameControlLease.WaitAsync(timeoutMs, cancellationToken).ConfigureAwait(true) == false)
                throw new TimeoutException($"Timed out waiting {timeoutMs}ms for the RimBridge game-control lease.");
            return;
        }

        await GameControlLease.WaitAsync(cancellationToken).ConfigureAwait(true);
    }

    private static int CurrentTicksGame()
    {
        return Current.Game?.tickManager?.TicksGame ?? 0;
    }

    private static RimBridgeTickResult ToSdkTickResult(RimWorldTickStepper.StepSnapshot snapshot)
    {
        return new RimBridgeTickResult
        {
            Success = snapshot.Success,
            Status = snapshot.Status,
            Message = snapshot.Message,
            RequestedTicks = snapshot.RequestedTicks,
            CompletedTicks = snapshot.CompletedTicks,
            StartTicksGame = snapshot.StartTicksGame,
            EndTicksGame = snapshot.EndTicksGame,
            AdvancedTicks = snapshot.AdvancedTicks,
            AdvancedFrames = snapshot.AdvancedFrames
        };
    }
}
