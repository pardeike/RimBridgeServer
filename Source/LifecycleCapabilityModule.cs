using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using RimBridgeServer.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimBridgeServer;

internal sealed class LifecycleCapabilityModule
{
    private static readonly MainThreadDispatcher Dispatcher = new();
    private static readonly ConditionWaiter Waiter = new();
    private static readonly FieldInfo UltraSpeedBoostField = typeof(TickManager).GetField("UltraSpeedBoost", BindingFlags.Static | BindingFlags.NonPublic);

    private sealed class LetterWaitState
    {
        public bool Available { get; set; } = true;

        public bool Paused { get; set; }

        public long TickCount { get; set; }

        public object SessionToken { get; set; }

        public object Snapshot { get; set; }

        public string Message { get; set; } = string.Empty;

        public List<string> LetterIds { get; set; } = [];

        public List<object> MatchingLetters { get; set; } = [];
    }

    private sealed class SaveCompatibilityInspection
    {
        public SaveModMetadataReadResult Metadata { get; set; }

        public SaveModCompatibilityResult Compatibility { get; set; }

        public int ActiveModCount { get; set; }

        public object State { get; set; }
    }

    private sealed class SaveCompatibilityRuntimeSnapshot
    {
        public List<string> ActivePackageIds { get; set; } = [];

        public object State { get; set; }
    }

    public object PauseGame(bool pause = true)
    {
        if (Current.Game == null)
        {
            return new { success = false, message = "No game is currently loaded" };
        }

        var currentlyPaused = Find.TickManager.Paused;
        if (pause && !currentlyPaused)
            Find.TickManager.TogglePaused();
        else if (!pause && currentlyPaused)
            Find.TickManager.TogglePaused();

        return new
        {
            success = true,
            paused = Find.TickManager.Paused,
            message = pause ? "Game paused" : "Game unpaused"
        };
    }

    public object SetTimeSpeed(string speed = "Normal", bool? ultraSpeedBoost = null)
    {
        if (Current.Game == null || Find.TickManager == null)
        {
            return new
            {
                success = false,
                message = "No game is currently loaded.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (Enum.TryParse<TimeSpeed>(speed, ignoreCase: true, out var parsed) == false)
        {
            return new
            {
                success = false,
                message = $"Unknown time speed '{speed}'. Supported values: {string.Join(", ", Enum.GetNames(typeof(TimeSpeed)))}.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        var previousUltraSpeedBoost = TryReadUltraSpeedBoost();
        bool? currentUltraSpeedBoost = null;
        if (ultraSpeedBoost != null)
        {
            TrySetUltraSpeedBoost(ultraSpeedBoost.Value);
            currentUltraSpeedBoost = TryReadUltraSpeedBoost();
        }

        Find.TickManager.CurTimeSpeed = parsed;
        return new
        {
            success = true,
            timeSpeed = Find.TickManager.CurTimeSpeed.ToString(),
            ultraSpeedBoostAvailable = UltraSpeedBoostField != null,
            requestedUltraSpeedBoost = ultraSpeedBoost,
            previousUltraSpeedBoost,
            currentUltraSpeedBoost = currentUltraSpeedBoost ?? TryReadUltraSpeedBoost(),
            paused = Find.TickManager.Paused,
            message = $"Time speed set to {Find.TickManager.CurTimeSpeed}.",
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public object PlayFor(int durationMs, string speed = "Normal", int pollIntervalMs = 25, bool forceRequestedSpeed = false)
    {
        if (durationMs <= 0)
        {
            return new
            {
                success = false,
                message = "durationMs must be greater than 0.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (pollIntervalMs < 0)
        {
            return new
            {
                success = false,
                message = "pollIntervalMs cannot be negative.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (Enum.TryParse<TimeSpeed>(speed, ignoreCase: true, out var parsedSpeed) == false)
        {
            return new
            {
                success = false,
                message = $"Unknown time speed '{speed}'. Supported values: Normal, Fast, Superfast, Ultrafast.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (parsedSpeed == TimeSpeed.Paused)
        {
            return new
            {
                success = false,
                message = "play_for requires an active play speed. Use Normal, Fast, Superfast, or Ultrafast.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        bool? previousNeverForceNormalSpeed = null;
        bool? restoredNeverForceNormalSpeed = null;
        bool? previousUltraSpeedBoost = null;
        bool? restoredUltraSpeedBoost = null;

        try
        {
            Stopwatch stopwatch = null;
            var controller = new TimedPlaybackController();
            var result = controller.PlayFor(
                durationMs,
                pollIntervalMs,
                getElapsedMs: () => stopwatch?.ElapsedMilliseconds ?? 0L,
                readState: () => RimBridgeMainThread.Invoke(CapturePlaybackState, timeoutMs: 5000),
                startPlayback: () => RimBridgeMainThread.Invoke(() =>
                {
                    if (forceRequestedSpeed && previousNeverForceNormalSpeed == null)
                    {
                        previousNeverForceNormalSpeed = DebugViewSettings.neverForceNormalSpeed;
                        DebugViewSettings.neverForceNormalSpeed = true;
                        previousUltraSpeedBoost = TrySetUltraSpeedBoost(true);
                    }
                    stopwatch = Stopwatch.StartNew();
                    EnsurePlaybackRunning(parsedSpeed);
                }, timeoutMs: 5000),
                pausePlayback: () => RimBridgeMainThread.Invoke(() =>
                {
                    try
                    {
                        PauseIfNeeded();
                    }
                    finally
                    {
                        restoredNeverForceNormalSpeed = RestoreNeverForceNormalSpeed(previousNeverForceNormalSpeed);
                        restoredUltraSpeedBoost = RestoreUltraSpeedBoost(previousUltraSpeedBoost);
                    }
                }, timeoutMs: 5000));

            restoredNeverForceNormalSpeed ??= RimBridgeMainThread.Invoke(
                () => RestoreNeverForceNormalSpeed(previousNeverForceNormalSpeed),
                timeoutMs: 5000);
            restoredUltraSpeedBoost ??= RimBridgeMainThread.Invoke(
                () => RestoreUltraSpeedBoost(previousUltraSpeedBoost),
                timeoutMs: 5000);
            var finalNeverForceNormalSpeed = RimBridgeMainThread.Invoke(() => DebugViewSettings.neverForceNormalSpeed, timeoutMs: 5000);
            var finalUltraSpeedBoost = RimBridgeMainThread.Invoke(TryReadUltraSpeedBoost, timeoutMs: 5000);
            var finalTimeSpeed = RimBridgeMainThread.Invoke(() => Find.TickManager?.CurTimeSpeed.ToString(), timeoutMs: 5000);

            return new
            {
                success = result.Success,
                requestedDurationMs = durationMs,
                elapsedMs = result.ElapsedMs,
                pollIntervalMs,
                timeSpeed = parsedSpeed.ToString(),
                forceRequestedSpeed,
                previousNeverForceNormalSpeed,
                restoredNeverForceNormalSpeed,
                finalNeverForceNormalSpeed,
                ultraSpeedBoostAvailable = UltraSpeedBoostField != null,
                previousUltraSpeedBoost,
                restoredUltraSpeedBoost,
                finalUltraSpeedBoost,
                finalTimeSpeed,
                paused = result.PausedAtEnd,
                initiallyPaused = result.InitiallyPaused,
                startTick = result.StartTick,
                endTick = result.EndTick,
                advancedTicks = result.AdvancedTicks,
                attempts = result.Attempts,
                probeFailureCount = result.ProbeFailureCount,
                lastProbeError = string.IsNullOrWhiteSpace(result.LastProbeError) ? null : result.LastProbeError,
                message = result.Message,
                state = result.Snapshot
            };
        }
        catch (Exception ex)
        {
            if (forceRequestedSpeed && previousNeverForceNormalSpeed != null)
            {
                try
                {
                    restoredNeverForceNormalSpeed = RimBridgeMainThread.Invoke(
                        () => RestoreNeverForceNormalSpeed(previousNeverForceNormalSpeed),
                        timeoutMs: 5000);
                    restoredUltraSpeedBoost = RimBridgeMainThread.Invoke(
                        () => RestoreUltraSpeedBoost(previousUltraSpeedBoost),
                        timeoutMs: 5000);
                }
                catch
                {
                    restoredNeverForceNormalSpeed = null;
                    restoredUltraSpeedBoost = null;
                }
            }

            return new
            {
                success = false,
                message = $"Failed to play for {durationMs}ms: {ex.Message}",
                forceRequestedSpeed,
                previousNeverForceNormalSpeed,
                restoredNeverForceNormalSpeed,
                ultraSpeedBoostAvailable = UltraSpeedBoostField != null,
                previousUltraSpeedBoost,
                restoredUltraSpeedBoost,
                state = RimWorldState.ToolStateSnapshot()
            };
        }
    }

    public object PlayUntilLetter(int timeoutMs = 1800000, string speed = "Normal", int pollIntervalMs = 250, bool forceRequestedSpeed = false, bool includeExistingLetters = false)
    {
        if (timeoutMs < 0)
        {
            return new
            {
                success = false,
                message = "timeoutMs cannot be negative.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (pollIntervalMs < 0)
        {
            return new
            {
                success = false,
                message = "pollIntervalMs cannot be negative.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (Enum.TryParse<TimeSpeed>(speed, ignoreCase: true, out var parsedSpeed) == false)
        {
            return new
            {
                success = false,
                message = $"Unknown time speed '{speed}'. Supported values: Normal, Fast, Superfast, Ultrafast.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (parsedSpeed == TimeSpeed.Paused)
        {
            return new
            {
                success = false,
                message = "play_until_letter requires an active play speed. Use Normal, Fast, Superfast, or Ultrafast.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        bool? previousNeverForceNormalSpeed = null;
        bool? restoredNeverForceNormalSpeed = null;
        bool? previousUltraSpeedBoost = null;
        bool? restoredUltraSpeedBoost = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var initialState = RimBridgeMainThread.Invoke(
                () => CaptureLetterWaitState(new HashSet<string>(StringComparer.Ordinal)),
                timeoutMs: 5000);
            if (initialState.Available == false)
            {
                return new
                {
                    success = false,
                    message = string.IsNullOrWhiteSpace(initialState.Message) ? "Playback state is unavailable." : initialState.Message,
                    state = initialState.Snapshot
                };
            }

            var baselineLetterIds = includeExistingLetters
                ? new HashSet<string>(StringComparer.Ordinal)
                : initialState.LetterIds.ToHashSet(StringComparer.Ordinal);

            if (includeExistingLetters && initialState.MatchingLetters.Count > 0)
            {
                RimBridgeMainThread.Invoke(PauseIfNeeded, timeoutMs: 5000);
                var pausedState = RimBridgeMainThread.Invoke(
                    () => CaptureLetterWaitState(baselineLetterIds),
                    timeoutMs: 5000);
                return CreatePlayUntilLetterResponse(
                    success: true,
                    completionReason: "letter",
                    message: "Found existing letter(s) and paused the game.",
                    timeoutMs,
                    elapsedMs: stopwatch.ElapsedMilliseconds,
                    pollIntervalMs,
                    parsedSpeed,
                    forceRequestedSpeed,
                    includeExistingLetters,
                    previousNeverForceNormalSpeed,
                    restoredNeverForceNormalSpeed,
                    previousUltraSpeedBoost,
                    restoredUltraSpeedBoost,
                    finalNeverForceNormalSpeed: null,
                    finalUltraSpeedBoost: null,
                    initialState,
                    finalState: pausedState,
                    attempts: 1,
                    probeFailureCount: 0,
                    lastProbeError: string.Empty,
                    baselineLetterIds);
            }

            var startState = initialState;
            LetterWaitState finalState = initialState;
            WaitOutcome outcome = null;

            try
            {
                RimBridgeMainThread.Invoke(() =>
                {
                    if (forceRequestedSpeed && previousNeverForceNormalSpeed == null)
                    {
                        previousNeverForceNormalSpeed = DebugViewSettings.neverForceNormalSpeed;
                        DebugViewSettings.neverForceNormalSpeed = true;
                        previousUltraSpeedBoost = TrySetUltraSpeedBoost(true);
                    }
                    EnsurePlaybackRunning(parsedSpeed);
                }, timeoutMs: 5000);

                startState = RimBridgeMainThread.Invoke(
                    () => CaptureLetterWaitState(baselineLetterIds),
                    timeoutMs: 5000);

                outcome = Waiter.WaitUntil(() =>
                {
                    var current = RimBridgeMainThread.Invoke(
                        () => CaptureLetterWaitState(baselineLetterIds),
                        timeoutMs: 5000);
                    var hasNewLetter = current.MatchingLetters.Count > 0;
                    var sessionChanged = !SameSession(startState.SessionToken, current.SessionToken);
                    var externalPause = current.Available && current.Paused && !hasNewLetter;
                    var shouldStop = hasNewLetter || current.Available == false || sessionChanged || externalPause;
                    var message = BuildLetterWaitProbeMessage(current, hasNewLetter, sessionChanged, externalPause);

                    return new WaitProbeResult
                    {
                        IsSatisfied = shouldStop,
                        Message = message,
                        Snapshot = current
                    };
                }, new WaitOptions
                {
                    TimeoutMs = timeoutMs,
                    PollIntervalMs = NormalizePollIntervalMs(pollIntervalMs),
                    TimeoutMessage = $"Timed out waiting {timeoutMs}ms for a new letter.",
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
                try
                {
                    RimBridgeMainThread.Invoke(PauseIfNeeded, timeoutMs: 5000);
                }
                finally
                {
                    if (forceRequestedSpeed)
                    {
                        restoredNeverForceNormalSpeed = RimBridgeMainThread.Invoke(
                            () => RestoreNeverForceNormalSpeed(previousNeverForceNormalSpeed),
                            timeoutMs: 5000);
                        restoredUltraSpeedBoost = RimBridgeMainThread.Invoke(
                            () => RestoreUltraSpeedBoost(previousUltraSpeedBoost),
                            timeoutMs: 5000);
                    }
                }

                finalState = RimBridgeMainThread.Invoke(
                    () => CaptureLetterWaitState(baselineLetterIds),
                    timeoutMs: 5000);
            }

            var finalNeverForceNormalSpeed = RimBridgeMainThread.Invoke(() => DebugViewSettings.neverForceNormalSpeed, timeoutMs: 5000);
            var finalUltraSpeedBoost = RimBridgeMainThread.Invoke(TryReadUltraSpeedBoost, timeoutMs: 5000);
            var outcomeState = outcome?.Snapshot as LetterWaitState;
            var responseState = finalState ?? outcomeState ?? startState;
            var hasLetter = responseState.MatchingLetters.Count > 0;
            var completionReason = ResolveLetterWaitCompletionReason(outcome, responseState, hasLetter, startState.SessionToken);
            var success = hasLetter && (forceRequestedSpeed == false || restoredNeverForceNormalSpeed != null);

            return CreatePlayUntilLetterResponse(
                success,
                completionReason,
                BuildLetterWaitMessage(completionReason, hasLetter, outcome, responseState),
                timeoutMs,
                stopwatch.ElapsedMilliseconds,
                pollIntervalMs,
                parsedSpeed,
                forceRequestedSpeed,
                includeExistingLetters,
                previousNeverForceNormalSpeed,
                restoredNeverForceNormalSpeed,
                previousUltraSpeedBoost,
                restoredUltraSpeedBoost,
                finalNeverForceNormalSpeed,
                finalUltraSpeedBoost,
                initialState,
                responseState,
                outcome?.Attempts ?? 0,
                outcome?.ProbeFailureCount ?? 0,
                outcome?.LastProbeError ?? string.Empty,
                baselineLetterIds);
        }
        catch (Exception ex)
        {
            if (forceRequestedSpeed && previousNeverForceNormalSpeed != null)
            {
                try
                {
                    restoredNeverForceNormalSpeed = RimBridgeMainThread.Invoke(
                        () => RestoreNeverForceNormalSpeed(previousNeverForceNormalSpeed),
                        timeoutMs: 5000);
                    restoredUltraSpeedBoost = RimBridgeMainThread.Invoke(
                        () => RestoreUltraSpeedBoost(previousUltraSpeedBoost),
                        timeoutMs: 5000);
                }
                catch
                {
                    restoredNeverForceNormalSpeed = null;
                    restoredUltraSpeedBoost = null;
                }
            }

            try
            {
                RimBridgeMainThread.Invoke(PauseIfNeeded, timeoutMs: 5000);
            }
            catch
            {
                // Keep the original failure as the tool result.
            }

            return new
            {
                success = false,
                message = $"Failed to play until letter: {ex.Message}",
                forceRequestedSpeed,
                previousNeverForceNormalSpeed,
                restoredNeverForceNormalSpeed,
                ultraSpeedBoostAvailable = UltraSpeedBoostField != null,
                previousUltraSpeedBoost,
                restoredUltraSpeedBoost,
                state = RimWorldState.ToolStateSnapshot()
            };
        }
    }

    public object StepGameTicks(int ticks = 1, int timeoutMs = 10000, int pollIntervalMs = 10, bool pauseFirst = true, bool playSound = false)
    {
        if (ticks <= 0)
        {
            return new
            {
                success = false,
                message = "ticks must be greater than 0.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (timeoutMs < 0)
        {
            return new
            {
                success = false,
                message = "timeoutMs cannot be negative.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        if (pollIntervalMs < 0)
        {
            return new
            {
                success = false,
                message = "pollIntervalMs cannot be negative.",
                state = RimWorldState.ToolStateSnapshot()
            };
        }

        var start = RimBridgeMainThread.Invoke(() => RimWorldTickStepper.Start(ticks, timeoutMs, pauseFirst, playSound), timeoutMs: 5000);
        if (start.Status != "active")
            return start.ToToolResponse();

        var waiter = new ConditionWaiter();
        var outcome = waiter.WaitUntil(() =>
        {
            var current = RimBridgeMainThread.Invoke(() => RimWorldTickStepper.GetSnapshot(start.StepId), timeoutMs: 5000);
            var terminal = current.Status != "active";
            return new WaitProbeResult
            {
                IsSatisfied = terminal,
                Message = current.Message,
                Snapshot = current
            };
        }, new WaitOptions
        {
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs,
            TimeoutMessage = $"Timed out waiting to advance {ticks} game tick(s).",
            HandleProbeException = ex => ex is TimeoutException
                ? new WaitProbeResult
                {
                    IsSatisfied = false,
                    Message = "Retrying after main-thread timeout."
                }
                : null
        });

        if (outcome.Satisfied && outcome.Snapshot is RimWorldTickStepper.StepSnapshot completed)
            return completed.ToToolResponse();

        return RimBridgeMainThread.Invoke(() => RimWorldTickStepper.Cancel(start.StepId, outcome.Message), timeoutMs: 5000).ToToolResponse();
    }

    public object StartDebugGame()
    {
        return StartDebugGameCore();
    }

    public object StartDebugGameReady(int timeoutMs = 120000, int pollIntervalMs = 50, string readiness = AutomationReadiness.DefaultTargetName, bool pauseIfNeeded = false, string targetReadiness = null, bool waitForVisualReady = false)
    {
        readiness = RimWorldWaits.ResolveReadinessInput(readiness, targetReadiness, waitForVisualReady);
        var stopwatch = Stopwatch.StartNew();
        var entry = RimWorldWaits.WaitForEntrySceneReadyResult(timeoutMs, pollIntervalMs);
        if (entry.TryGetValue("success", out var entrySuccessValue) == false || entrySuccessValue is not bool entrySuccess || entrySuccess == false)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = false,
                ["message"] = entry.TryGetValue("message", out var entryMessage) ? entryMessage : "RimWorld entry scene was not ready.",
                ["state"] = entry.TryGetValue("state", out var entryState) ? entryState : null,
                ["entry"] = entry
            };
        }

        var start = Dispatcher.Invoke(() => StartDebugGameCore(allowWhileLongEventPending: true), timeoutMs: 5000);
        if (start.TryGetValue("success", out var startSuccessValue) == false || startSuccessValue is not bool startSuccess || startSuccess == false)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = false,
                ["message"] = start.TryGetValue("message", out var message) ? message : "Failed to queue RimWorld quick test start.",
                ["state"] = start.TryGetValue("state", out var state) ? state : null,
                ["start"] = start
            };
        }

        var remainingTimeoutMs = timeoutMs <= 0
            ? 0
            : Math.Max(0, timeoutMs - (int)stopwatch.ElapsedMilliseconds);
        var wait = RimWorldWaits.WaitForGameLoadedResult(remainingTimeoutMs, pollIntervalMs, readiness, pauseIfNeeded);
        var success = wait.TryGetValue("success", out var waitSuccessValue) && waitSuccessValue is bool satisfied && satisfied;
        wait.TryGetValue("message", out var waitMessage);
        wait.TryGetValue("state", out var finalState);

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["success"] = success,
            ["message"] = waitMessage,
            ["state"] = finalState,
            ["entry"] = entry,
            ["start"] = start,
            ["wait"] = wait
        };
    }

    private static Dictionary<string, object> StartDebugGameCore(bool allowWhileLongEventPending = false)
    {
        if (LongEventHandler.AnyEventNowOrWaiting && allowWhileLongEventPending == false)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = false,
                ["message"] = "RimWorld is busy with another long event. Wait for it to finish before starting a debug game.",
                ["state"] = RimWorldState.ToolStateSnapshot()
            };
        }

        if (!GenScene.InEntryScene || Current.ProgramState != ProgramState.Entry)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = false,
                ["message"] = "Debug game start is only supported from the main menu entry scene.",
                ["state"] = RimWorldState.ToolStateSnapshot()
            };
        }

        if (Current.Game != null)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = false,
                ["message"] = "A game is already loaded. Return to the main menu before starting a new debug game.",
                ["state"] = RimWorldState.ToolStateSnapshot()
            };
        }

        var pauseOnLoad = Prefs.PauseOnLoad;
        LongEventHandler.QueueLongEvent(delegate
        {
            Root_Play.SetupForQuickTestPlay();
            RimBridgeDebugGamePause.Register(Current.Game, pauseOnLoad);
            PageUtility.InitGameStart();
        }, "GeneratingMap", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["success"] = true,
            ["status"] = "queued",
            ["message"] = "Queued RimWorld quick test start.",
            ["scenario"] = SafeDefName(() => ScenarioDefOf.Crashlanded.defName, "Crashlanded"),
            ["storyteller"] = SafeDefName(() => StorytellerDefOf.Cassandra.defName, "Cassandra"),
            ["difficulty"] = SafeDefName(() => DifficultyDefOf.Rough.defName, "Rough"),
            ["mapSize"] = 250,
            ["pauseOnLoad"] = pauseOnLoad,
            ["state"] = RimWorldState.ToolStateSnapshot()
        };
    }

    private static string SafeDefName(Func<string> readDefName, string fallback)
    {
        try
        {
            var value = readDefName?.Invoke();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    public object GoToMainMenu()
    {
        var state = RimWorldState.ToolStateSnapshot();
        if (GenScene.InEntryScene && Current.ProgramState == ProgramState.Entry && Current.Game == null)
        {
            return new
            {
                success = true,
                status = "noop",
                message = "RimWorld is already at the main menu entry scene.",
                state
            };
        }

        if (LongEventHandler.AnyEventNowOrWaiting)
        {
            return new
            {
                success = false,
                message = "RimWorld is busy with another long event. Wait for it to finish before returning to the main menu.",
                state
            };
        }

        GenScene.GoToMainMenu();
        return new
        {
            success = true,
            status = "queued",
            message = "Queued return to the RimWorld main menu.",
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public object ListSaves(bool compatibleOnly = false, int offset = 0)
    {
        var runtime = Dispatcher.Invoke(CaptureSaveCompatibilityRuntimeSnapshot, timeoutMs: 5000);
        var inspectedSaves = GenFilePaths.AllSavedGameFiles
            .Select(file => new
            {
                File = file,
                Compatibility = InspectSaveCompatibility(file.FullName, runtime)
            })
            .ToList();
        var saves = inspectedSaves
            .Where(save => compatibleOnly == false || save.Compatibility.Compatibility.IsCompatible)
            .OrderByDescending(save => save.File.LastWriteTimeUtc)
            .ToList();
        var savePage = ResponseBudget.TakePage(saves, offset, save => (object)new
            {
                name = Path.GetFileNameWithoutExtension(save.File.Name),
                lastWriteTimeUtc = save.File.LastWriteTimeUtc,
                sizeBytes = save.File.Length,
                compatibility = DescribeSaveCompatibilityCompact(save.Compatibility)
            }, out var page);
        var compatibleCount = inspectedSaves.Count(save => save.Compatibility.Compatibility.IsCompatible);
        var missingModsCount = inspectedSaves.Count(save => save.Compatibility.Compatibility.Status == SaveModCompatibilityStatus.MissingMods);
        var metadataUnavailableCount = inspectedSaves.Count(save => save.Compatibility.Compatibility.Status == SaveModCompatibilityStatus.MetadataUnavailable);

        return new
        {
            success = true,
            saveFolder = GenFilePaths.SavedGamesFolderPath,
            compatibleOnly,
            totalCount = inspectedSaves.Count,
            count = saves.Count,
            compatibleCount,
            incompatibleCount = inspectedSaves.Count - compatibleCount,
            missingModsCount,
            metadataUnavailableCount,
            page,
            saves = savePage
        };
    }

    public object SpawnThing(string defName, int x, int z, int stackCount = 1)
    {
        var map = RimWorldState.CurrentMapOrThrow();
        var cell = new IntVec3(x, 0, z);
        if (!cell.InBounds(map))
            return new { success = false, message = $"Cell ({x}, {z}) is out of bounds for the current map." };

        // GetNamedSilentFail avoids RimWorld's error log entry (which opens the debug log window and raises a GABS
        // attention item) and lets the bridge answer with close matches instead of a NullReferenceException later.
        var thingDef = string.IsNullOrWhiteSpace(defName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(defName.Trim());
        if (thingDef == null)
        {
            return new
            {
                success = false,
                message = $"No ThingDef named '{defName}'.",
                suggestions = SuggestThingDefNames(defName)
            };
        }

        var thing = ThingMaker.MakeThing(thingDef);
        if (thing.def.stackLimit > 1)
            thing.stackCount = Mathf.Clamp(stackCount, 1, thing.def.stackLimit);

        var spawned = GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
        return new
        {
            success = true,
            thingId = RimWorldState.GetThingId(spawned),
            defName = spawned.def.defName,
            label = spawned.LabelCap,
            stackCount = spawned.stackCount,
            cell = new { x = cell.x, z = cell.z }
        };
    }

    private static List<string> SuggestThingDefNames(string query)
    {
        var needle = (query ?? string.Empty).Trim();
        if (needle.Length == 0)
            return [];

        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(def => def.defName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                || needle.IndexOf(def.defName, StringComparison.OrdinalIgnoreCase) >= 0
                || (def.label != null && def.label.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(def => Math.Abs(def.defName.Length - needle.Length))
            .ThenBy(def => def.defName, StringComparer.Ordinal)
            .Select(def => def.defName)
            .Take(8)
            .ToList();
    }

    public object SaveGame(string saveName)
    {
        if (Current.Game == null)
            return new { success = false, message = "No game is currently loaded." };
        if (GameDataSaveLoader.SavingIsTemporarilyDisabled)
            return new { success = false, message = "RimWorld is temporarily blocking saves because a UI/window state or cutscene prevents saving." };

        var safeName = RimWorldState.SanitizeName(saveName, "rimbridge_save");
        GameDataSaveLoader.SaveGame(safeName);
        var path = GenFilePaths.FilePathForSavedGame(safeName);
        var info = new FileInfo(path);

        return new
        {
            success = true,
            saveName = safeName,
            path,
            exists = info.Exists,
            sizeBytes = info.Exists ? info.Length : 0
        };
    }

    public object LoadGame(string saveName, bool ignoreModCompatibility = false)
    {
        var safeName = RimWorldState.SanitizeName(saveName, "rimbridge_save");
        var path = GenFilePaths.FilePathForSavedGame(safeName);
        if (!File.Exists(path))
        {
            return new { success = false, message = $"Save '{safeName}' does not exist.", path };
        }

        var compatibility = InspectSaveCompatibility(path, captureRuntimeOnCurrentThread: true);
        var compatibilityFailure = ignoreModCompatibility
            ? null
            : CreateSaveCompatibilityFailure(safeName, path, compatibility);
        if (compatibilityFailure != null)
            return compatibilityFailure;

        if (Find.WindowStack?.FloatMenu != null)
            Find.WindowStack.TryRemove(Find.WindowStack.FloatMenu, doCloseSound: false);
        RimBridgeContextMenus.Clear();

        GameDataSaveLoader.LoadGame(safeName);

        return new
        {
            success = true,
            status = "queued",
            saveName = safeName,
            path,
            compatibilityCheckIgnored = ignoreModCompatibility,
            compatibility = DescribeSaveCompatibility(compatibility),
            state = RimWorldState.ToolStateSnapshot()
        };
    }

    public object LoadGameReady(string saveName, int timeoutMs = 120000, int pollIntervalMs = 50, string readiness = AutomationReadiness.DefaultTargetName, bool pauseIfNeeded = false, string targetReadiness = null, bool waitForVisualReady = false, bool ignoreModCompatibility = false)
    {
        readiness = RimWorldWaits.ResolveReadinessInput(readiness, targetReadiness, waitForVisualReady);
        var operationStopwatch = Stopwatch.StartNew();
        var safeName = RimWorldState.SanitizeName(saveName, "rimbridge_save");
        var path = GenFilePaths.FilePathForSavedGame(safeName);
        if (!File.Exists(path))
        {
            return new { success = false, message = $"Save '{safeName}' does not exist.", path };
        }

        var mainThreadReady = WaitForAutomationMainThread(timeoutMs, pollIntervalMs);
        if (mainThreadReady.Satisfied == false)
        {
            return CreateLoadGameReadyQueueFailure(safeName, path, mainThreadReady);
        }

        var compatibility = InspectSaveCompatibility(path, captureRuntimeOnCurrentThread: false);
        var compatibilityFailure = ignoreModCompatibility
            ? null
            : CreateSaveCompatibilityFailure(safeName, path, compatibility);
        if (compatibilityFailure != null)
            return compatibilityFailure;

        object queuedState;
        try
        {
            queuedState = Dispatcher.Invoke(() =>
            {
                if (Find.WindowStack?.FloatMenu != null)
                    Find.WindowStack.TryRemove(Find.WindowStack.FloatMenu, doCloseSound: false);
                RimBridgeContextMenus.Clear();

                GameDataSaveLoader.LoadGame(safeName);
                return RimWorldState.ToolStateSnapshot();
            }, timeoutMs: ResolveLoadQueueDispatchTimeoutMs(timeoutMs, operationStopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            return CreateLoadGameReadyQueueFailure(safeName, path, mainThreadReady, ex);
        }

        var wait = RimWorldWaits.WaitForGameLoadedResult(ResolveRemainingTimeoutMs(timeoutMs, operationStopwatch.ElapsedMilliseconds), pollIntervalMs, readiness, pauseIfNeeded);
        var success = wait.TryGetValue("success", out var successValue) && successValue is bool satisfied && satisfied;
        wait.TryGetValue("message", out var message);
        wait.TryGetValue("state", out var finalState);

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["success"] = success,
            ["saveName"] = safeName,
            ["path"] = path,
            ["message"] = message,
            ["compatibilityCheckIgnored"] = ignoreModCompatibility,
            ["compatibility"] = DescribeSaveCompatibility(compatibility),
            ["state"] = finalState ?? queuedState,
            ["load"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = true,
                ["status"] = "queued",
                ["saveName"] = safeName,
                ["path"] = path,
                ["compatibilityCheckIgnored"] = ignoreModCompatibility,
                ["compatibility"] = DescribeSaveCompatibility(compatibility),
                ["state"] = queuedState
            },
            ["wait"] = wait
        };
    }

    private static SaveCompatibilityInspection InspectSaveCompatibility(string path, bool captureRuntimeOnCurrentThread)
    {
        var runtime = captureRuntimeOnCurrentThread
            ? CaptureSaveCompatibilityRuntimeSnapshot()
            : Dispatcher.Invoke(CaptureSaveCompatibilityRuntimeSnapshot, timeoutMs: 5000);
        return InspectSaveCompatibility(path, runtime);
    }

    private static SaveCompatibilityInspection InspectSaveCompatibility(string path, SaveCompatibilityRuntimeSnapshot runtime)
    {
        var metadata = SaveModCompatibility.ReadMetadata(path);
        return new SaveCompatibilityInspection
        {
            Metadata = metadata,
            Compatibility = SaveModCompatibility.Evaluate(metadata, runtime.ActivePackageIds),
            ActiveModCount = runtime.ActivePackageIds.Count,
            State = runtime.State
        };
    }

    private static SaveCompatibilityRuntimeSnapshot CaptureSaveCompatibilityRuntimeSnapshot()
    {
        return new SaveCompatibilityRuntimeSnapshot
        {
            ActivePackageIds = LoadedModManager.RunningMods
                .Where(mod => mod != null)
                .Select(mod => mod.PackageId)
                .Where(packageId => string.IsNullOrWhiteSpace(packageId) == false)
                .ToList(),
            State = RimWorldState.ToolStateSnapshot()
        };
    }

    private static Dictionary<string, object> CreateSaveCompatibilityFailure(
        string saveName,
        string path,
        SaveCompatibilityInspection inspection)
    {
        if (inspection.Compatibility.IsCompatible)
            return null;

        var missingMods = inspection.Compatibility.MissingMods;
        var hasMissingMods = inspection.Compatibility.Status == SaveModCompatibilityStatus.MissingMods;
        var message = hasMissingMods
            ? $"Save '{saveName}' cannot be loaded because {missingMods.Count} mod(s) recorded by the save are not currently active: {string.Join(", ", missingMods.Select(DescribeMissingModInline))}."
            : $"Save '{saveName}' cannot be loaded because its mod compatibility metadata could not be read: {inspection.Compatibility.MetadataError}";

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["success"] = false,
            ["code"] = hasMissingMods ? "save.missing_mods" : "save.mod_metadata_unavailable",
            ["message"] = message,
            ["saveName"] = saveName,
            ["path"] = path,
            ["compatibilityCheckIgnored"] = false,
            ["compatibility"] = DescribeSaveCompatibility(inspection),
            ["state"] = inspection.State
        };
    }

    private static string DescribeMissingModInline(SaveModReference mod)
    {
        return string.Equals(mod.Name, mod.PackageId, StringComparison.OrdinalIgnoreCase)
            ? mod.PackageId
            : $"{mod.Name} ({mod.PackageId})";
    }

    // Listing form: status only for compatible saves; missing mod names (bounded) when something is missing.
    private static object DescribeSaveCompatibilityCompact(SaveCompatibilityInspection inspection)
    {
        var compatibility = inspection.Compatibility;
        var missing = compatibility.MissingMods;
        return new
        {
            status = DescribeSaveCompatibilityStatus(compatibility.Status),
            missingModCount = missing.Count > 0 ? missing.Count : (int?)null,
            missingMods = missing.Count > 0 ? missing.Select(mod => mod.Name ?? mod.PackageId).Take(10).ToList() : null,
            metadataError = string.IsNullOrWhiteSpace(compatibility.MetadataError) ? null : compatibility.MetadataError
        };
    }

    private static Dictionary<string, object> DescribeSaveCompatibility(SaveCompatibilityInspection inspection)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["status"] = DescribeSaveCompatibilityStatus(inspection.Compatibility.Status),
            ["compatible"] = inspection.Compatibility.IsCompatible,
            ["metadataStatus"] = DescribeSaveMetadataStatus(inspection.Compatibility.MetadataStatus),
            ["metadataReadable"] = inspection.Compatibility.MetadataReadable,
            ["metadataError"] = string.IsNullOrWhiteSpace(inspection.Compatibility.MetadataError)
                ? null
                : inspection.Compatibility.MetadataError,
            ["recordedModCount"] = inspection.Metadata.Mods.Count,
            ["activeModCount"] = inspection.ActiveModCount,
            ["missingModCount"] = inspection.Compatibility.MissingMods.Count,
            ["missingMods"] = inspection.Compatibility.MissingMods.Select(mod => new
            {
                name = mod.Name,
                packageId = mod.PackageId,
                canonicalPackageId = mod.CanonicalPackageId
            }).ToList()
        };
    }

    private static string DescribeSaveCompatibilityStatus(SaveModCompatibilityStatus status)
    {
        return status switch
        {
            SaveModCompatibilityStatus.Compatible => "compatible",
            SaveModCompatibilityStatus.MissingMods => "missing_mods",
            _ => "metadata_unavailable"
        };
    }

    private static string DescribeSaveMetadataStatus(SaveModMetadataStatus status)
    {
        return status switch
        {
            SaveModMetadataStatus.Readable => "readable",
            SaveModMetadataStatus.MissingModMetadata => "missing_mod_metadata",
            _ => "unreadable"
        };
    }

    private static LetterWaitState CaptureLetterWaitState(ISet<string> baselineLetterIds)
    {
        var hasPlayableGame = Current.ProgramState == ProgramState.Playing
            && Current.Game != null
            && Find.TickManager != null;
        var hasLongEvent = LongEventHandler.AnyEventNowOrWaiting;
        var atMainMenu = GenScene.InEntryScene || Current.ProgramState == ProgramState.Entry;
        var allLetters = hasPlayableGame
            ? RimWorldNotifications.GetLettersInDisplayOrder()
            : [];
        var baseline = baselineLetterIds ?? new HashSet<string>(StringComparer.Ordinal);
        var matchingLetters = allLetters
            .Where(letter => baseline.Contains(letter.GetUniqueLoadID()) == false)
            .Select(RimWorldNotifications.DescribeLetter)
            .ToList();

        return new LetterWaitState
        {
            Available = hasPlayableGame && !hasLongEvent,
            Paused = hasPlayableGame && Find.TickManager.Paused,
            TickCount = hasPlayableGame ? Find.TickManager.TicksGame : 0,
            SessionToken = Current.Game,
            Snapshot = RimWorldState.ToolStateSnapshot(),
            Message = hasPlayableGame
                ? (hasLongEvent ? "RimWorld is busy with a long event." : string.Empty)
                : (atMainMenu ? "RimWorld returned to the main menu." : "No playable game is currently loaded."),
            LetterIds = allLetters.Select(letter => letter.GetUniqueLoadID()).ToList(),
            MatchingLetters = matchingLetters
        };
    }

    private static object CreatePlayUntilLetterResponse(
        bool success,
        string completionReason,
        string message,
        int timeoutMs,
        long elapsedMs,
        int pollIntervalMs,
        TimeSpeed speed,
        bool forceRequestedSpeed,
        bool includeExistingLetters,
        bool? previousNeverForceNormalSpeed,
        bool? restoredNeverForceNormalSpeed,
        bool? previousUltraSpeedBoost,
        bool? restoredUltraSpeedBoost,
        bool? finalNeverForceNormalSpeed,
        bool? finalUltraSpeedBoost,
        LetterWaitState initialState,
        LetterWaitState finalState,
        int attempts,
        int probeFailureCount,
        string lastProbeError,
        ISet<string> baselineLetterIds)
    {
        finalState ??= initialState ?? new LetterWaitState
        {
            Available = false,
            Snapshot = RimWorldState.ToolStateSnapshot(),
            Message = "Letter wait state is unavailable."
        };

        return new
        {
            success,
            completionReason,
            message,
            requestedTimeoutMs = timeoutMs,
            elapsedMs,
            pollIntervalMs,
            timeSpeed = speed.ToString(),
            forceRequestedSpeed,
            previousNeverForceNormalSpeed,
            restoredNeverForceNormalSpeed,
            finalNeverForceNormalSpeed,
            ultraSpeedBoostAvailable = UltraSpeedBoostField != null,
            previousUltraSpeedBoost,
            restoredUltraSpeedBoost,
            finalUltraSpeedBoost,
            includeExistingLetters,
            paused = finalState.Paused,
            initiallyPaused = initialState?.Paused ?? false,
            startTick = initialState?.TickCount ?? 0,
            endTick = finalState.TickCount,
            advancedTicks = Math.Max(0L, finalState.TickCount - (initialState?.TickCount ?? finalState.TickCount)),
            attempts,
            probeFailureCount,
            lastProbeError = string.IsNullOrWhiteSpace(lastProbeError) ? null : lastProbeError,
            baselineLetterCount = baselineLetterIds?.Count ?? 0,
            baselineLetterIds = baselineLetterIds?.ToList() ?? [],
            totalLetterCount = finalState.LetterIds.Count,
            newLetterCount = finalState.MatchingLetters.Count,
            letters = finalState.MatchingLetters,
            state = finalState.Snapshot
        };
    }

    private static string BuildLetterWaitProbeMessage(LetterWaitState current, bool hasNewLetter, bool sessionChanged, bool externalPause)
    {
        if (current.Available == false)
            return string.IsNullOrWhiteSpace(current.Message) ? "Playback state became unavailable." : current.Message;
        if (sessionChanged)
            return "Game session changed before a new letter appeared.";
        if (hasNewLetter)
            return $"Detected {current.MatchingLetters.Count} new letter(s).";
        if (externalPause)
            return "Game was paused externally before a new letter appeared.";

        return $"Waiting for a new letter. Current letter count: {current.LetterIds.Count}.";
    }

    private static string ResolveLetterWaitCompletionReason(WaitOutcome outcome, LetterWaitState finalState, bool hasLetter, object startSessionToken)
    {
        if (hasLetter)
            return "letter";
        if (outcome?.Satisfied != true)
            return "timeout";
        if (finalState.Available == false)
            return "unavailable";
        if (!SameSession(startSessionToken, finalState.SessionToken))
            return "session_changed";
        if (finalState.Paused)
            return "external_pause";

        return "stopped";
    }

    private static string BuildLetterWaitMessage(string completionReason, bool hasLetter, WaitOutcome outcome, LetterWaitState finalState)
    {
        if (hasLetter)
            return $"Detected {finalState.MatchingLetters.Count} new letter(s) and paused the game.";

        return completionReason switch
        {
            "timeout" => outcome?.Message ?? "Timed out waiting for a new letter.",
            "external_pause" => "Game was paused externally before a new letter appeared.",
            "session_changed" => "Game session changed before a new letter appeared.",
            "unavailable" => string.IsNullOrWhiteSpace(finalState.Message)
                ? "Playback state became unavailable before a new letter appeared."
                : finalState.Message,
            _ => outcome?.Message ?? "Stopped waiting for a new letter."
        };
    }

    private static bool SameSession(object left, object right)
    {
        return ReferenceEquals(left, right) || Equals(left, right);
    }

    private static WaitOutcome WaitForAutomationMainThread(int timeoutMs, int pollIntervalMs)
    {
        return Waiter.WaitUntil(
            () =>
            {
                var state = Dispatcher.Invoke(RimWorldState.ToolStateSnapshot, timeoutMs: ResolveProbeDispatchTimeoutMs(timeoutMs));
                return new WaitProbeResult
                {
                    IsSatisfied = true,
                    Message = "RimWorld main thread accepted automation work.",
                    Snapshot = state
                };
            },
            new WaitOptions
            {
                TimeoutMs = NormalizeTimeoutMs(timeoutMs),
                PollIntervalMs = NormalizePollIntervalMs(pollIntervalMs),
                HandleProbeException = ex => new WaitProbeResult
                {
                    IsSatisfied = false,
                    Message = "RimWorld main thread was busy before queueing save load. Retrying.",
                    BlockingReason = ex.Message
                },
                TimeoutMessage = "Timed out waiting for RimWorld main thread before queueing save load."
            });
    }

    private static Dictionary<string, object> CreateLoadGameReadyQueueFailure(
        string safeName,
        string path,
        WaitOutcome mainThreadReady,
        Exception exception = null)
    {
        var message = exception == null
            ? mainThreadReady.Message
            : $"RimWorld main thread did not queue save '{safeName}': {exception.GetBaseException().Message}";

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["success"] = false,
            ["saveName"] = safeName,
            ["path"] = path,
            ["message"] = message,
            ["state"] = mainThreadReady.Snapshot,
            ["queue"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["success"] = false,
                ["message"] = message,
                ["attempts"] = mainThreadReady.Attempts,
                ["elapsedMs"] = mainThreadReady.ElapsedMs,
                ["probeFailureCount"] = mainThreadReady.ProbeFailureCount,
                ["lastProbeError"] = string.IsNullOrWhiteSpace(mainThreadReady.LastProbeError) ? null : mainThreadReady.LastProbeError,
                ["blockingReason"] = string.IsNullOrWhiteSpace(mainThreadReady.BlockingReason) ? null : mainThreadReady.BlockingReason
            }
        };
    }

    private static int ResolveProbeDispatchTimeoutMs(int timeoutMs)
    {
        if (timeoutMs <= 0)
            return 0;

        return Math.Min(timeoutMs, 10000);
    }

    private static int ResolveLoadQueueDispatchTimeoutMs(int timeoutMs, long elapsedMs)
    {
        var remainingMs = ResolveRemainingTimeoutMs(timeoutMs, elapsedMs);
        if (remainingMs <= 0)
            return 0;

        return remainingMs <= 10000
            ? remainingMs
            : Math.Min(remainingMs, 30000);
    }

    private static int ResolveRemainingTimeoutMs(int timeoutMs, long elapsedMs)
    {
        if (timeoutMs <= 0)
            return 0;

        return Math.Max(1, timeoutMs - (int)Math.Min(int.MaxValue, elapsedMs));
    }

    private static int NormalizeTimeoutMs(int timeoutMs)
    {
        return Math.Max(0, timeoutMs);
    }

    private static int NormalizePollIntervalMs(int pollIntervalMs)
    {
        return Math.Max(25, pollIntervalMs);
    }

    private static TimedPlaybackState CapturePlaybackState()
    {
        var hasPlayableGame = Current.ProgramState == ProgramState.Playing
            && Current.Game != null
            && Find.TickManager != null;
        var hasLongEvent = LongEventHandler.AnyEventNowOrWaiting;
        var atMainMenu = GenScene.InEntryScene || Current.ProgramState == ProgramState.Entry;

        return new TimedPlaybackState
        {
            Available = hasPlayableGame && !hasLongEvent,
            Paused = hasPlayableGame && Find.TickManager.Paused,
            TickCount = hasPlayableGame ? Find.TickManager.TicksGame : 0,
            SessionToken = Current.Game,
            Snapshot = RimWorldState.ToolStateSnapshot(),
            Message = hasPlayableGame
                ? (hasLongEvent ? "RimWorld is busy with a long event." : string.Empty)
                : (atMainMenu ? "RimWorld returned to the main menu." : "No playable game is currently loaded.")
        };
    }

    private static void EnsurePlaybackRunning(TimeSpeed speed)
    {
        if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.TickManager == null)
            throw new InvalidOperationException("No playable game is currently loaded.");
        if (LongEventHandler.AnyEventNowOrWaiting)
            throw new InvalidOperationException("RimWorld is busy with a long event.");

        Find.TickManager.CurTimeSpeed = speed;
        if (Find.TickManager.Paused)
            Find.TickManager.TogglePaused();
    }

    private static void PauseIfNeeded()
    {
        if (Current.Game == null || Find.TickManager == null)
            return;

        if (!Find.TickManager.Paused)
            Find.TickManager.TogglePaused();
    }

    private static bool? RestoreNeverForceNormalSpeed(bool? previousNeverForceNormalSpeed)
    {
        if (previousNeverForceNormalSpeed == null)
            return null;

        DebugViewSettings.neverForceNormalSpeed = previousNeverForceNormalSpeed.Value;
        return DebugViewSettings.neverForceNormalSpeed;
    }

    private static bool? TryReadUltraSpeedBoost()
    {
        return UltraSpeedBoostField?.GetValue(null) as bool?;
    }

    private static bool? TrySetUltraSpeedBoost(bool value)
    {
        var previous = TryReadUltraSpeedBoost();
        UltraSpeedBoostField?.SetValue(null, value);
        return previous;
    }

    private static bool? RestoreUltraSpeedBoost(bool? previousUltraSpeedBoost)
    {
        if (previousUltraSpeedBoost == null)
            return null;

        UltraSpeedBoostField?.SetValue(null, previousUltraSpeedBoost.Value);
        return TryReadUltraSpeedBoost();
    }
}
