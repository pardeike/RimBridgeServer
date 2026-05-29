using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimBridgeServer;

internal static class RimWorldTickStepper
{
    private static readonly object Sync = new();
    private static StepRequest _active;
    private static StepSnapshot _lastSnapshot;

    public static StepSnapshot Start(int ticks, int timeoutMs, bool pauseFirst, bool playSound)
    {
        if (ticks <= 0)
            return StepSnapshot.Failed("ticks must be greater than 0.", CurrentTicksGame(), RimWorldState.ToolStateSnapshot());
        if (timeoutMs < 0)
            return StepSnapshot.Failed("timeoutMs cannot be negative.", CurrentTicksGame(), RimWorldState.ToolStateSnapshot());

        var availabilityError = ValidatePlayableGame();
        if (availabilityError != null)
            return StepSnapshot.Failed(availabilityError, CurrentTicksGame(), RimWorldState.ToolStateSnapshot());

        if (pauseFirst)
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        if (Find.TickManager.CurTimeSpeed != TimeSpeed.Paused)
            return StepSnapshot.Failed("RimWorld must be paused before deterministic tick stepping. Pass pauseFirst=true or pause the game first.", Find.TickManager.TicksGame, RimWorldState.ToolStateSnapshot());

        lock (Sync)
        {
            if (_active != null && !_active.IsTerminal)
                return _active.ToSnapshot(RimWorldState.ToolStateSnapshot());

            var request = new StepRequest
            {
                Id = "step_" + Guid.NewGuid().ToString("N"),
                RequestedTicks = ticks,
                TimeoutMs = timeoutMs,
                PlaySound = playSound,
                InitialFrame = Time.frameCount,
                LastFrame = Time.frameCount,
                StartedAtUtc = DateTimeOffset.UtcNow,
                StartTicksGame = Find.TickManager.TicksGame,
                EndTicksGame = Find.TickManager.TicksGame,
                InitialTimeSpeed = Find.TickManager.CurTimeSpeed.ToString(),
                Status = StepStatus.Active,
                Message = $"Queued {ticks} paused game tick(s), one per Unity update frame."
            };
            _active = request;
            _lastSnapshot = request.ToSnapshot(RimWorldState.ToolStateSnapshot());
            return _lastSnapshot;
        }
    }

    public static StepSnapshot GetSnapshot(string stepId = null)
    {
        lock (Sync)
        {
            if (_active != null && (string.IsNullOrWhiteSpace(stepId) || string.Equals(_active.Id, stepId, StringComparison.Ordinal)))
                return _active.ToSnapshot(RimWorldState.ToolStateSnapshot());

            return _lastSnapshot ?? StepSnapshot.Failed("No tick-step request has been started.", CurrentTicksGame(), RimWorldState.ToolStateSnapshot());
        }
    }

    public static StepSnapshot Cancel(string stepId = null, string message = null)
    {
        lock (Sync)
        {
            if (_active == null || _active.IsTerminal)
                return _lastSnapshot ?? StepSnapshot.Failed("No active tick-step request exists.", CurrentTicksGame(), RimWorldState.ToolStateSnapshot());
            if (!string.IsNullOrWhiteSpace(stepId) && !string.Equals(_active.Id, stepId, StringComparison.Ordinal))
                return _active.ToSnapshot(RimWorldState.ToolStateSnapshot());

            _active.Status = StepStatus.Cancelled;
            _active.CompletedAtUtc = DateTimeOffset.UtcNow;
            _active.Message = string.IsNullOrWhiteSpace(message) ? "Tick-step request was cancelled." : message;
            _active.EndTicksGame = CurrentTicksGame();
            _lastSnapshot = _active.ToSnapshot(RimWorldState.ToolStateSnapshot());
            return _lastSnapshot;
        }
    }

    public static void AdvanceFromRootUpdate(int frameCount)
    {
        StepRequest request;
        lock (Sync)
        {
            request = _active;
        }

        if (request == null || request.IsTerminal)
            return;

        StepSnapshot snapshot;
        lock (Sync)
        {
            if (!ReferenceEquals(request, _active) || request.IsTerminal)
                return;

            snapshot = AdvanceLocked(request, frameCount);
            _lastSnapshot = snapshot;
        }
    }

    private static StepSnapshot AdvanceLocked(StepRequest request, int frameCount)
    {
        request.LastFrame = frameCount;

        if (request.TimeoutMs > 0 && (DateTimeOffset.UtcNow - request.StartedAtUtc).TotalMilliseconds > request.TimeoutMs)
            return CompleteLocked(request, StepStatus.TimedOut, $"Timed out after advancing {request.CompletedTicks} of {request.RequestedTicks} requested tick(s).");

        var availabilityError = ValidatePlayableGame();
        if (availabilityError != null)
            return CompleteLocked(request, StepStatus.Failed, availabilityError);

        var tickManager = Find.TickManager;
        if (tickManager.CurTimeSpeed != TimeSpeed.Paused)
            return CompleteLocked(request, StepStatus.Failed, "RimWorld was unpaused during deterministic tick stepping.");

        try
        {
            tickManager.DoSingleTick();
            if (request.PlaySound)
                SoundDefOf.Clock_Stop.PlayOneShotOnCamera();
        }
        catch (Exception ex)
        {
            return CompleteLocked(request, StepStatus.Failed, $"DoSingleTick failed: {ex.Message}");
        }

        request.CompletedTicks++;
        request.EndTicksGame = tickManager.TicksGame;

        if (request.CompletedTicks >= request.RequestedTicks)
            return CompleteLocked(request, StepStatus.Completed, $"Advanced {request.CompletedTicks} game tick(s) across {Math.Max(1, request.LastFrame - request.InitialFrame + 1)} Unity update frame(s).");

        return request.ToSnapshot(RimWorldState.ToolStateSnapshot());
    }

    private static StepSnapshot CompleteLocked(StepRequest request, StepStatus status, string message)
    {
        request.Status = status;
        request.CompletedAtUtc = DateTimeOffset.UtcNow;
        request.Message = message;
        request.EndTicksGame = CurrentTicksGame();
        if (Current.Game != null && Find.TickManager != null)
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        var snapshot = request.ToSnapshot(RimWorldState.ToolStateSnapshot());
        if (ReferenceEquals(_active, request))
            _active = null;
        return snapshot;
    }

    private static string ValidatePlayableGame()
    {
        if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.TickManager == null)
            return "No playable game is currently loaded.";
        if (LongEventHandler.AnyEventNowOrWaiting)
            return "RimWorld is busy with a long event.";

        return null;
    }

    private static int CurrentTicksGame()
    {
        return Current.Game?.tickManager?.TicksGame ?? 0;
    }

    private enum StepStatus
    {
        Active,
        Completed,
        Failed,
        TimedOut,
        Cancelled
    }

    private sealed class StepRequest
    {
        public string Id { get; set; }

        public int RequestedTicks { get; set; }

        public int CompletedTicks { get; set; }

        public int TimeoutMs { get; set; }

        public bool PlaySound { get; set; }

        public int InitialFrame { get; set; }

        public int LastFrame { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public int StartTicksGame { get; set; }

        public int EndTicksGame { get; set; }

        public string InitialTimeSpeed { get; set; }

        public StepStatus Status { get; set; }

        public string Message { get; set; }

        public bool IsTerminal => Status is StepStatus.Completed or StepStatus.Failed or StepStatus.TimedOut or StepStatus.Cancelled;

        public StepSnapshot ToSnapshot(object state)
        {
            return new StepSnapshot
            {
                Success = Status == StepStatus.Completed,
                StepId = Id,
                Status = Status.ToString().ToLowerInvariant(),
                RequestedTicks = RequestedTicks,
                CompletedTicks = CompletedTicks,
                RemainingTicks = Math.Max(0, RequestedTicks - CompletedTicks),
                StartTicksGame = StartTicksGame,
                EndTicksGame = EndTicksGame,
                AdvancedTicks = Math.Max(0, EndTicksGame - StartTicksGame),
                InitialFrame = InitialFrame,
                LastFrame = LastFrame,
                AdvancedFrames = Math.Max(0, LastFrame - InitialFrame + 1),
                InitialTimeSpeed = InitialTimeSpeed,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                Message = Message,
                State = state
            };
        }
    }

    internal sealed class StepSnapshot
    {
        public bool Success { get; set; }

        public string StepId { get; set; }

        public string Status { get; set; }

        public int RequestedTicks { get; set; }

        public int CompletedTicks { get; set; }

        public int RemainingTicks { get; set; }

        public int StartTicksGame { get; set; }

        public int EndTicksGame { get; set; }

        public int AdvancedTicks { get; set; }

        public int InitialFrame { get; set; }

        public int LastFrame { get; set; }

        public int AdvancedFrames { get; set; }

        public string InitialTimeSpeed { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public string Message { get; set; }

        public object State { get; set; }

        public static StepSnapshot Failed(string message, int ticksGame, object state)
        {
            return new StepSnapshot
            {
                Success = false,
                Status = "failed",
                RequestedTicks = 0,
                CompletedTicks = 0,
                RemainingTicks = 0,
                StartTicksGame = ticksGame,
                EndTicksGame = ticksGame,
                AdvancedTicks = 0,
                Message = message,
                State = state
            };
        }

        public Dictionary<string, object> ToToolResponse()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = Success,
                ["stepId"] = StepId,
                ["status"] = Status,
                ["requestedTicks"] = RequestedTicks,
                ["completedTicks"] = CompletedTicks,
                ["remainingTicks"] = RemainingTicks,
                ["startTicksGame"] = StartTicksGame,
                ["endTicksGame"] = EndTicksGame,
                ["advancedTicks"] = AdvancedTicks,
                ["initialFrame"] = InitialFrame,
                ["lastFrame"] = LastFrame,
                ["advancedFrames"] = AdvancedFrames,
                ["initialTimeSpeed"] = InitialTimeSpeed,
                ["startedAtUtc"] = StartedAtUtc == default ? null : StartedAtUtc,
                ["completedAtUtc"] = CompletedAtUtc,
                ["message"] = Message,
                ["state"] = State
            };
        }
    }
}
