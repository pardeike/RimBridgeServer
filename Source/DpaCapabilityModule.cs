using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimBridgeServer;

internal sealed class DpaCapabilityModule
{
    private static readonly string[] GeneralTickPreset =
    [
        "Verse.TickManager:DoSingleTick",
        "Verse.TickList:Tick",
        "Verse.Pawn:Tick",
        "Verse.Pawn:TickInterval"
    ];

    private static readonly string[] GeneralUpdatePreset =
    [
        "Verse.Root_Play:Update",
        "Verse.Map:MapUpdate",
        "Verse.Pawn:ProcessPostTickVisuals"
    ];

    private static readonly string[] GeneralPawnAiMethodsPreset =
    [
        "Verse.AI.Pawn_JobTracker:JobTrackerTick",
        "Verse.AI.Pawn_JobTracker:JobTrackerTickInterval",
        "Verse.AI.Pawn_PathFollower:PatherTick",
        "Verse.AI.Pawn_PathFollower:CostToPayThisTick",
        "Verse.Pawn_HealthTracker:HealthTick",
        "Verse.Pawn_HealthTracker:HealthTickInterval",
        "Verse.AI.AttackTargetsCache:GetPotentialTargetsFor",
        "RimWorld.Building_TurretGun:Tick",
        "RimWorld.WorkGiver_Scanner:PotentialWorkCellsGlobal",
        "RimWorld.WorkGiver_Scanner:PotentialWorkThingsGlobal"
    ];

    public object DpaStatus(bool includePresets = true)
    {
        var runtime = DpaRuntime.Resolve();
        return new
        {
            success = true,
            dpa = runtime.Describe(),
            presets = includePresets ? DescribePresets() : null
        };
    }

    public object DpaPatchMethods(
        string category = "Tick",
        string inputMode = "Method",
        string preset = "none",
        string targets = "",
        bool initialize = true,
        bool resetAfterPatch = true,
        int previewLimit = 8)
    {
        var runtime = DpaRuntime.Resolve();
        previewLimit = Math.Max(0, Math.Min(previewLimit, 50));

        if (runtime.Available == false)
            return DpaUnavailable("patch", runtime);

        if (initialize && EnsureDpaProfiling(runtime, out var initMessage, out var initError) == false)
        {
            return new
            {
                success = false,
                action = "patch",
                message = initError ?? initMessage,
                dpa = runtime.Describe()
            };
        }

        var resolvedTargets = ResolveTargetNames(preset, targets);
        if (resolvedTargets.Length == 0)
        {
            return new
            {
                success = false,
                action = "patch",
                message = "No DPA targets were provided or resolved from the requested preset.",
                category,
                inputMode,
                preset,
                targets,
                dpa = runtime.Describe()
            };
        }

        var patch = PatchTargets(runtime, category, inputMode, resolvedTargets, previewLimit);
        if (resetAfterPatch)
            runtime.ResetProfilers();

        return new
        {
            success = patch.Failures.Length == 0,
            action = "patch",
            category = patch.Category,
            inputMode = patch.InputMode,
            preset,
            requestedTargetCount = resolvedTargets.Length,
            patchedCount = patch.Successes.Length,
            failedCount = patch.Failures.Length,
            elapsedMilliseconds = patch.ElapsedMilliseconds,
            successes = patch.Successes,
            failures = patch.Failures,
            resetAfterPatch,
            dpa = runtime.Describe()
        };
    }

    public object DpaSnapshot(string sortBy = "average", int limit = 30)
    {
        var runtime = DpaRuntime.Resolve();
        limit = Math.Max(1, Math.Min(limit, 500));

        if (runtime.Available == false)
            return DpaUnavailable("snapshot", runtime);

        return new
        {
            success = true,
            action = "snapshot",
            sortBy,
            limit,
            dpa = runtime.Describe(),
            rows = SnapshotProfiles(runtime, sortBy, limit)
        };
    }

    public object DpaReset()
    {
        var runtime = DpaRuntime.Resolve();
        if (runtime.Available == false)
            return DpaUnavailable("reset", runtime);

        runtime.ResetProfilers();
        return new
        {
            success = true,
            action = "reset",
            message = "DPA profiler buffers were reset.",
            dpa = runtime.Describe()
        };
    }

    public object DpaStop()
    {
        var runtime = DpaRuntime.Resolve();
        if (runtime.Available == false)
            return DpaUnavailable("stop", runtime);

        runtime.EndProfiling();
        runtime.ResetToSettings();
        return new
        {
            success = true,
            action = "stop",
            message = "DPA profiling was stopped without running cleanup.",
            dpa = runtime.Describe()
        };
    }

    public object DpaCleanup()
    {
        var runtime = DpaRuntime.Resolve();
        if (runtime.Available == false)
            return DpaUnavailable("cleanup", runtime);

        runtime.Cleanup();
        return new
        {
            success = true,
            action = "cleanup",
            message = "DPA cleanup was requested.",
            dpa = runtime.Describe()
        };
    }

    private static object DpaUnavailable(string action, DpaRuntime runtime)
    {
        return new
        {
            success = false,
            action,
            message = "Dubs Performance Analyzer is not available. Install and enable Dubs Performance Analyzer alongside RimBridgeServer to use DPA profiling tools.",
            dpa = runtime.Describe()
        };
    }

    private static object DescribePresets()
    {
        return new
        {
            general_tick = GeneralTickPreset,
            general_update = GeneralUpdatePreset,
            general_pawn_ai_methods = GeneralPawnAiMethodsPreset
        };
    }

    private static string[] ResolveTargetNames(string preset, string targets)
    {
        var result = new List<string>();
        switch ((preset ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "none":
                break;
            case "general_tick":
            case "vanilla_tick":
                result.AddRange(GeneralTickPreset);
                break;
            case "general_update":
            case "vanilla_update":
                result.AddRange(GeneralUpdatePreset);
                break;
            case "general_pawn_ai_methods":
            case "pawn_ai_methods":
                result.AddRange(GeneralPawnAiMethodsPreset);
                break;
            default:
                result.Add(preset);
                break;
        }

        if (string.IsNullOrWhiteSpace(targets) == false)
        {
            result.AddRange(targets
                .Split(['\n', '\r', ';', '|', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(target => target.Trim())
                .Where(target => string.IsNullOrWhiteSpace(target) == false));
        }

        return result
            .Where(target => string.IsNullOrWhiteSpace(target) == false)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool EnsureDpaProfiling(DpaRuntime runtime, out string message, out string error)
    {
        message = null;
        error = null;

        if (runtime.Available == false)
        {
            error = "DPA runtime is not available.";
            return false;
        }

        if (runtime.CurrentlyCleaningUp)
        {
            error = "DPA is currently cleaning up. Retry after cleanup completes.";
            return false;
        }

        if (runtime.CurrentlyProfiling && runtime.IsPatched)
        {
            message = "DPA is already profiling.";
            return true;
        }

        try
        {
            var window = Activator.CreateInstance(runtime.WindowAnalyzerType);
            runtime.WindowAnalyzerPreOpen.Invoke(window, Array.Empty<object>());
            message = "DPA profiling was initialized through Window_Analyzer.PreOpen.";
            return runtime.CurrentlyCleaningUp == false;
        }
        catch (Exception ex)
        {
            error = $"Failed to initialize DPA profiling: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static DpaPatchResult PatchTargets(DpaRuntime runtime, string category, string inputMode, string[] targets, int previewLimit)
    {
        var categoryValue = ParseDpaEnum(runtime.CategoryType, category, "Tick");
        var inputModeValue = ParseDpaEnum(runtime.CurrentInputType, inputMode, "Method");
        var categoryName = categoryValue?.ToString() ?? "Tick";
        var inputModeName = inputModeValue?.ToString() ?? "Method";
        var successes = new List<object>();
        var failures = new List<object>();
        var previousDisableThreading = runtime.DisableThreadedPatching;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            runtime.DisableThreadedPatching = true;

            foreach (var target in targets)
            {
                try
                {
                    var resolvedMethods = ResolvePatchTargets(runtime, inputModeName, target, out var resolveError);
                    if (string.IsNullOrWhiteSpace(resolveError) == false)
                    {
                        failures.Add(new
                        {
                            target,
                            error = resolveError
                        });
                        continue;
                    }

                    if (resolvedMethods.Length == 0)
                    {
                        failures.Add(new
                        {
                            target,
                            error = "DPA did not resolve this target string to any methods."
                        });
                        continue;
                    }

                    runtime.ExecutePatch(inputModeValue, target, categoryValue);
                    successes.Add(new
                    {
                        target,
                        inputMode = inputModeName,
                        resolvedCount = resolvedMethods.Length,
                        resolvedMethods = resolvedMethods
                            .Take(previewLimit)
                            .Select(method => method.FullDescription())
                            .ToArray()
                    });
                }
                catch (Exception ex)
                {
                    failures.Add(new
                    {
                        target,
                        error = ex.GetBaseException().Message
                    });
                }
            }
        }
        finally
        {
            runtime.DisableThreadedPatching = previousDisableThreading;
        }

        stopwatch.Stop();
        return new DpaPatchResult
        {
            Category = categoryName,
            InputMode = inputModeName,
            Successes = successes.ToArray(),
            Failures = failures.ToArray(),
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
        };
    }

    private static MethodInfo[] ResolvePatchTargets(DpaRuntime runtime, string inputMode, string target, out string error)
    {
        if (string.Equals(inputMode, "Type", StringComparison.OrdinalIgnoreCase))
            return ResolveTypeTargets(runtime, target, out error);

        if (string.Equals(inputMode, "Method", StringComparison.OrdinalIgnoreCase))
            return ResolveMethodTargets(runtime, target, out error);

        error = "Only DPA Method and Type input modes are supported by this bridge tool.";
        return Array.Empty<MethodInfo>();
    }

    private static MethodInfo[] ResolveMethodTargets(DpaRuntime runtime, string target, out string error)
    {
        error = null;
        try
        {
            return runtime.GetMethods(target).ToArray();
        }
        catch (Exception ex)
        {
            error = $"DPA method resolution failed: {ex.GetBaseException().Message}";
            return Array.Empty<MethodInfo>();
        }
    }

    private static MethodInfo[] ResolveTypeTargets(DpaRuntime runtime, string target, out string error)
    {
        error = null;
        var type = AccessTools.TypeByName(target);
        if (type == null)
        {
            error = $"Type '{target}' was not found.";
            return Array.Empty<MethodInfo>();
        }

        try
        {
            return runtime.GetTypeMethods(type, nestedClasses: false).ToArray();
        }
        catch (Exception ex)
        {
            error = $"DPA type method resolution failed: {ex.GetBaseException().Message}";
            return Array.Empty<MethodInfo>();
        }
    }

    private static object[] SnapshotProfiles(DpaRuntime runtime, string sortBy, int limit)
    {
        var rows = new List<DpaProfileRow>();
        var currentLogCount = runtime.CurrentLogCount;

        foreach (var entry in runtime.Profiles)
        {
            var profiler = GetPropertyValue<object>(entry, "Value");
            if (profiler == null)
                continue;

            var row = BuildProfileRow(runtime, profiler, currentLogCount);
            if (row.TotalCalls <= 0 && row.TotalMilliseconds <= 0)
                continue;

            rows.Add(row);
        }

        IEnumerable<DpaProfileRow> sorted = (sortBy ?? "average").Trim().ToLowerInvariant() switch
        {
            "max" => rows.OrderByDescending(row => row.MaxMilliseconds),
            "total" => rows.OrderByDescending(row => row.TotalMilliseconds),
            "calls" => rows.OrderByDescending(row => row.TotalCalls),
            "callsperentry" => rows.OrderByDescending(row => row.CallsPerEntry),
            "calls_per_entry" => rows.OrderByDescending(row => row.CallsPerEntry),
            "label" => rows.OrderBy(row => row.Label, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderByDescending(row => row.AverageMilliseconds)
        };

        return sorted
            .Take(limit)
            .Select(row => row.ToResult())
            .ToArray();
    }

    private static DpaProfileRow BuildProfileRow(DpaRuntime runtime, object profiler, int currentLogCount)
    {
        var times = runtime.GetProfilerTimes(profiler);
        var hits = runtime.GetProfilerHits(profiler);
        var entries = Math.Min(Math.Min(times.Length, hits.Length), Math.Max(0, currentLogCount));
        if (entries <= 0)
            entries = Math.Min(times.Length, hits.Length);

        var currentIndex = runtime.GetProfilerCurrentIndex(profiler);
        var total = 0d;
        var max = 0d;
        var totalCalls = 0;
        var maxCalls = 0;
        var nonZeroEntries = 0;

        for (var i = 0; i < entries; i++)
        {
            if (times.Length == 0)
                break;

            var index = currentIndex - 1 - i;
            while (index < 0)
                index += times.Length;
            index %= times.Length;

            var time = times[index];
            var calls = hits[index];
            total += time;
            totalCalls += calls;
            if (time > max)
                max = time;
            if (calls > maxCalls)
                maxCalls = calls;
            if (time > 0 || calls > 0)
                nonZeroEntries++;
        }

        var method = runtime.GetProfilerMethod(profiler);
        var type = runtime.GetProfilerType(profiler);
        var key = runtime.GetProfilerKey(profiler);
        var label = runtime.GetProfilerLabel(profiler);

        return new DpaProfileRow
        {
            Key = key,
            Label = label ?? key,
            Entries = entries,
            NonZeroEntries = nonZeroEntries,
            TotalMilliseconds = total,
            AverageMilliseconds = entries <= 0 ? 0d : total / entries,
            MaxMilliseconds = max,
            TotalCalls = totalCalls,
            CallsPerEntry = entries <= 0 ? 0d : totalCalls / (double)entries,
            MaxCalls = maxCalls,
            Method = method,
            Type = type
        };
    }

    private static object ParseDpaEnum(Type enumType, string value, string fallback)
    {
        if (enumType == null)
            return null;

        try
        {
            return Enum.Parse(enumType, value ?? fallback, ignoreCase: true);
        }
        catch
        {
            return Enum.Parse(enumType, fallback, ignoreCase: true);
        }
    }

    private static T GetPropertyValue<T>(object instance, string propertyName)
    {
        if (instance == null)
            return default;

        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
            return default;

        var value = property.GetValue(instance);
        return value is T typed ? typed : default;
    }

    private sealed class DpaPatchResult
    {
        public string Category { get; set; }

        public string InputMode { get; set; }

        public object[] Successes { get; set; }

        public object[] Failures { get; set; }

        public long ElapsedMilliseconds { get; set; }
    }

    private sealed class DpaProfileRow
    {
        public string Key { get; set; }

        public string Label { get; set; }

        public int Entries { get; set; }

        public int NonZeroEntries { get; set; }

        public double TotalMilliseconds { get; set; }

        public double AverageMilliseconds { get; set; }

        public double MaxMilliseconds { get; set; }

        public int TotalCalls { get; set; }

        public double CallsPerEntry { get; set; }

        public int MaxCalls { get; set; }

        public MethodBase Method { get; set; }

        public Type Type { get; set; }

        public object ToResult()
        {
            var declaringType = Method?.DeclaringType ?? Type;
            return new
            {
                key = Key,
                label = Label,
                entries = Entries,
                nonZeroEntries = NonZeroEntries,
                totalMilliseconds = TotalMilliseconds,
                averageMilliseconds = AverageMilliseconds,
                maxMilliseconds = MaxMilliseconds,
                totalCalls = TotalCalls,
                callsPerEntry = CallsPerEntry,
                maxCalls = MaxCalls,
                method = Method?.FullDescription(),
                declaringType = declaringType?.FullName,
                assembly = declaringType?.Assembly.GetName().Name
            };
        }
    }

    private sealed class DpaRuntime
    {
        private const string DpaPackageId = "Dubwise.DubsPerformanceAnalyzer.steam";

        private DpaRuntime()
        {
        }

        public bool Available { get; private set; }

        public bool PackageActive { get; private set; }

        public string[] ActivePackageIds { get; private set; }

        public Type AnalyzerType { get; private set; }

        public Type WindowAnalyzerType { get; private set; }

        public Type ModbaseType { get; private set; }

        public Type GuiControllerType { get; private set; }

        public Type PanelDevOptionsType { get; private set; }

        public Type CurrentInputType { get; private set; }

        public Type CategoryType { get; private set; }

        public Type ProfileControllerType { get; private set; }

        public Type SettingsType { get; private set; }

        public Type UtilityType { get; private set; }

        public Type ProfilerType { get; private set; }

        public MethodInfo WindowAnalyzerPreOpen { get; private set; }

        private MethodInfo AnalyzerEndProfiling { get; set; }

        private MethodInfo AnalyzerCleanup { get; set; }

        private MethodInfo GuiControllerResetProfilers { get; set; }

        private MethodInfo GuiControllerResetToSettings { get; set; }

        private MethodInfo PanelDevOptionsExecutePatch { get; set; }

        private MethodInfo UtilityGetMethods { get; set; }

        private MethodInfo UtilityGetTypeMethods { get; set; }

        private PropertyInfo AnalyzerCurrentlyProfiling { get; set; }

        private PropertyInfo AnalyzerCurrentlyCleaningUp { get; set; }

        private PropertyInfo AnalyzerCurrentlyPaused { get; set; }

        private PropertyInfo AnalyzerCurrentLogCount { get; set; }

        private PropertyInfo ProfileControllerProfiles { get; set; }

        private FieldInfo ModbaseIsPatched { get; set; }

        private FieldInfo SettingsDisableThreadedPatching { get; set; }

        private FieldInfo ProfilerTimes { get; set; }

        private FieldInfo ProfilerHits { get; set; }

        private FieldInfo ProfilerCurrentIndex { get; set; }

        private FieldInfo ProfilerMethod { get; set; }

        private FieldInfo ProfilerTypeField { get; set; }

        private FieldInfo ProfilerKey { get; set; }

        private FieldInfo ProfilerLabel { get; set; }

        public static DpaRuntime Resolve()
        {
            var activePackageIds = LoadedModManager.RunningModsListForReading
                .Select(mod => mod.PackageIdPlayerFacing)
                .Where(id => string.IsNullOrWhiteSpace(id) == false)
                .ToArray();

            var runtime = new DpaRuntime
            {
                ActivePackageIds = activePackageIds,
                PackageActive = activePackageIds.Any(IsDpaPackageId),
                AnalyzerType = AccessTools.TypeByName("Analyzer.Profiling.Analyzer"),
                WindowAnalyzerType = AccessTools.TypeByName("Analyzer.Window_Analyzer"),
                ModbaseType = AccessTools.TypeByName("Analyzer.Modbase"),
                GuiControllerType = AccessTools.TypeByName("Analyzer.Profiling.GUIController"),
                PanelDevOptionsType = AccessTools.TypeByName("Analyzer.Profiling.Panel_DevOptions"),
                CurrentInputType = AccessTools.TypeByName("Analyzer.Profiling.CurrentInput"),
                CategoryType = AccessTools.TypeByName("Analyzer.Profiling.Category"),
                ProfileControllerType = AccessTools.TypeByName("Analyzer.Profiling.ProfileController"),
                SettingsType = AccessTools.TypeByName("Analyzer.Settings"),
                UtilityType = AccessTools.TypeByName("Analyzer.Profiling.Utility"),
                ProfilerType = AccessTools.TypeByName("Analyzer.Profiling.Profiler")
            };
            runtime.ResolveMembers();
            return runtime;
        }

        public bool CurrentlyProfiling => GetStaticProperty<bool>(AnalyzerCurrentlyProfiling);

        public bool CurrentlyCleaningUp => GetStaticProperty<bool>(AnalyzerCurrentlyCleaningUp);

        public bool CurrentlyPaused => GetStaticProperty<bool>(AnalyzerCurrentlyPaused);

        public int CurrentLogCount => GetStaticProperty<int>(AnalyzerCurrentLogCount);

        public bool IsPatched => GetStaticField<bool>(ModbaseIsPatched);

        public bool DisableThreadedPatching
        {
            get => GetStaticField<bool>(SettingsDisableThreadedPatching);
            set => SettingsDisableThreadedPatching?.SetValue(null, value);
        }

        public IEnumerable Profiles => ProfileControllerProfiles?.GetValue(null) as IEnumerable ?? Array.Empty<object>();

        public void ExecutePatch(object inputMode, string target, object category)
        {
            PanelDevOptionsExecutePatch.Invoke(null, [inputMode, target, category]);
        }

        public IEnumerable<MethodInfo> GetMethods(string target)
        {
            return (UtilityGetMethods.Invoke(null, [target]) as IEnumerable)?
                .Cast<MethodInfo>()
                .Where(method => method != null) ?? [];
        }

        public IEnumerable<MethodInfo> GetTypeMethods(Type type, bool nestedClasses)
        {
            return (UtilityGetTypeMethods.Invoke(null, [type, nestedClasses]) as IEnumerable)?
                .Cast<MethodInfo>()
                .Where(method => method != null) ?? [];
        }

        public void ResetProfilers()
        {
            GuiControllerResetProfilers.Invoke(null, Array.Empty<object>());
        }

        public void ResetToSettings()
        {
            GuiControllerResetToSettings.Invoke(null, Array.Empty<object>());
        }

        public void EndProfiling()
        {
            AnalyzerEndProfiling.Invoke(null, Array.Empty<object>());
        }

        public void Cleanup()
        {
            AnalyzerCleanup.Invoke(null, Array.Empty<object>());
        }

        public double[] GetProfilerTimes(object profiler)
        {
            return ProfilerTimes?.GetValue(profiler) as double[] ?? Array.Empty<double>();
        }

        public int[] GetProfilerHits(object profiler)
        {
            return ProfilerHits?.GetValue(profiler) as int[] ?? Array.Empty<int>();
        }

        public int GetProfilerCurrentIndex(object profiler)
        {
            return Convert.ToInt32(ProfilerCurrentIndex?.GetValue(profiler) ?? 0);
        }

        public MethodBase GetProfilerMethod(object profiler)
        {
            return ProfilerMethod?.GetValue(profiler) as MethodBase;
        }

        public Type GetProfilerType(object profiler)
        {
            return ProfilerTypeField?.GetValue(profiler) as Type;
        }

        public string GetProfilerKey(object profiler)
        {
            return ProfilerKey?.GetValue(profiler) as string;
        }

        public string GetProfilerLabel(object profiler)
        {
            return ProfilerLabel?.GetValue(profiler) as string;
        }

        public object Describe()
        {
            return new
            {
                available = Available,
                packageActive = PackageActive,
                packageId = DpaPackageId,
                typeChecks = new
                {
                    analyzer = AnalyzerType != null,
                    windowAnalyzer = WindowAnalyzerType != null,
                    modbase = ModbaseType != null,
                    guiController = GuiControllerType != null,
                    panelDevOptions = PanelDevOptionsType != null,
                    currentInput = CurrentInputType != null,
                    category = CategoryType != null,
                    profileController = ProfileControllerType != null,
                    settings = SettingsType != null,
                    utility = UtilityType != null,
                    profiler = ProfilerType != null
                },
                memberChecks = new
                {
                    windowAnalyzerPreOpen = WindowAnalyzerPreOpen != null,
                    analyzerEndProfiling = AnalyzerEndProfiling != null,
                    analyzerCleanup = AnalyzerCleanup != null,
                    resetProfilers = GuiControllerResetProfilers != null,
                    resetToSettings = GuiControllerResetToSettings != null,
                    executePatch = PanelDevOptionsExecutePatch != null,
                    getMethods = UtilityGetMethods != null,
                    getTypeMethods = UtilityGetTypeMethods != null,
                    profiles = ProfileControllerProfiles != null,
                    disableThreadedPatching = SettingsDisableThreadedPatching != null,
                    profilerFields = ProfilerTimes != null
                        && ProfilerHits != null
                        && ProfilerCurrentIndex != null
                        && ProfilerMethod != null
                        && ProfilerTypeField != null
                        && ProfilerKey != null
                        && ProfilerLabel != null
                },
                currentlyProfiling = Available && CurrentlyProfiling,
                currentlyCleaningUp = Available && CurrentlyCleaningUp,
                currentlyPaused = Available && CurrentlyPaused,
                currentLogCount = Available ? CurrentLogCount : 0,
                isPatched = Available && IsPatched,
                activePackageIds = ActivePackageIds
            };
        }

        private void ResolveMembers()
        {
            WindowAnalyzerPreOpen = AccessTools.Method(WindowAnalyzerType, "PreOpen");
            AnalyzerEndProfiling = AccessTools.Method(AnalyzerType, "EndProfiling");
            AnalyzerCleanup = AccessTools.Method(AnalyzerType, "Cleanup");
            GuiControllerResetProfilers = AccessTools.Method(GuiControllerType, "ResetProfilers");
            GuiControllerResetToSettings = AccessTools.Method(GuiControllerType, "ResetToSettings");
            PanelDevOptionsExecutePatch = AccessTools.Method(PanelDevOptionsType, "ExecutePatch");
            UtilityGetMethods = AccessTools.Method(UtilityType, "GetMethods", [typeof(string)]);
            UtilityGetTypeMethods = AccessTools.Method(UtilityType, "GetTypeMethods", [typeof(Type), typeof(bool)]);
            AnalyzerCurrentlyProfiling = AccessTools.Property(AnalyzerType, "CurrentlyProfiling");
            AnalyzerCurrentlyCleaningUp = AccessTools.Property(AnalyzerType, "CurrentlyCleaningUp");
            AnalyzerCurrentlyPaused = AccessTools.Property(AnalyzerType, "CurrentlyPaused");
            AnalyzerCurrentLogCount = AccessTools.Property(AnalyzerType, "GetCurrentLogCount");
            ProfileControllerProfiles = AccessTools.Property(ProfileControllerType, "Profiles");
            ModbaseIsPatched = AccessTools.Field(ModbaseType, "isPatched");
            SettingsDisableThreadedPatching = AccessTools.Field(SettingsType, "disableThreadedPatching");
            ProfilerTimes = AccessTools.Field(ProfilerType, "times");
            ProfilerHits = AccessTools.Field(ProfilerType, "hits");
            ProfilerCurrentIndex = AccessTools.Field(ProfilerType, "currentIndex");
            ProfilerMethod = AccessTools.Field(ProfilerType, "meth");
            ProfilerTypeField = AccessTools.Field(ProfilerType, "type");
            ProfilerKey = AccessTools.Field(ProfilerType, "key");
            ProfilerLabel = AccessTools.Field(ProfilerType, "label");

            Available = AnalyzerType != null
                && WindowAnalyzerType != null
                && ModbaseType != null
                && GuiControllerType != null
                && PanelDevOptionsType != null
                && CurrentInputType != null
                && CategoryType != null
                && ProfileControllerType != null
                && SettingsType != null
                && UtilityType != null
                && ProfilerType != null
                && WindowAnalyzerPreOpen != null
                && AnalyzerEndProfiling != null
                && AnalyzerCleanup != null
                && GuiControllerResetProfilers != null
                && GuiControllerResetToSettings != null
                && PanelDevOptionsExecutePatch != null
                && UtilityGetMethods != null
                && UtilityGetTypeMethods != null
                && AnalyzerCurrentlyProfiling != null
                && AnalyzerCurrentlyCleaningUp != null
                && AnalyzerCurrentlyPaused != null
                && AnalyzerCurrentLogCount != null
                && ProfileControllerProfiles != null
                && ModbaseIsPatched != null
                && SettingsDisableThreadedPatching != null
                && ProfilerTimes != null
                && ProfilerHits != null
                && ProfilerCurrentIndex != null
                && ProfilerMethod != null
                && ProfilerTypeField != null
                && ProfilerKey != null
                && ProfilerLabel != null;
        }

        private static T GetStaticProperty<T>(PropertyInfo property)
        {
            if (property == null)
                return default;

            var value = property.GetValue(null);
            return value is T typed ? typed : default;
        }

        private static T GetStaticField<T>(FieldInfo field)
        {
            if (field == null)
                return default;

            var value = field.GetValue(null);
            return value is T typed ? typed : default;
        }

        private static bool IsDpaPackageId(string packageId)
        {
            return string.Equals(packageId, DpaPackageId, StringComparison.OrdinalIgnoreCase)
                || packageId?.IndexOf("dubsperformanceanalyzer", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
