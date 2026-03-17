using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldModConfiguration
{
    private sealed class ModConfigurationEntrySnapshot
    {
        public string ModId { get; set; }

        public string PackageId { get; set; }

        public string PackageIdPlayerFacing { get; set; }

        public string PackageIdNonUnique { get; set; }

        public string Name { get; set; }

        public string ShortName { get; set; }

        public string FolderName { get; set; }

        public string RootDir { get; set; }

        public string Source { get; set; }

        public string Authors { get; set; }

        public string Version { get; set; }

        public bool Official { get; set; }

        public bool OnSteamWorkshop { get; set; }

        public bool IsCoreMod { get; set; }

        public bool Enabled { get; set; }

        public bool LoadedInSession { get; set; }

        public int? ActiveLoadOrder { get; set; }

        public int? LoadedSessionOrder { get; set; }

        public bool VersionCompatible { get; set; }

        public bool MadeForNewerVersion { get; set; }

        public bool HasConfigurationWarning { get; set; }

        public string ConfigurationWarning { get; set; }

        public bool HasVersionWarning { get; set; }

        public bool HasOrderingIssues { get; set; }

        public bool MatchesLoadedSession { get; set; }

        public List<string> UnsatisfiedDependencies { get; set; }

        public List<string> LoadBefore { get; set; }

        public List<string> LoadAfter { get; set; }

        public List<string> ForceLoadBefore { get; set; }

        public List<string> ForceLoadAfter { get; set; }

        public List<string> IncompatibleWith { get; set; }
    }

    private sealed class ModConfigurationSnapshot
    {
        public string ConfigPath { get; set; }

        public string CurrentConfigurationHash { get; set; }

        public string LoadedSessionHash { get; set; }

        public bool RestartRequired { get; set; }

        public List<string> RestartReasons { get; set; }

        public List<ModConfigurationEntrySnapshot> Mods { get; set; }

        public List<ModConfigurationEntrySnapshot> ActiveMods { get; set; }

        public List<ModConfigurationEntrySnapshot> LoadedSessionMods { get; set; }

        public int ConfigurationIssueCount { get; set; }

        public int VersionWarningCount { get; set; }

        public int OrderingIssueCount { get; set; }

        public int UnsatisfiedDependencyCount { get; set; }

        public int SessionMismatchCount { get; set; }
    }

    public static object ListModsResponse(bool includeInactive = true)
    {
        var snapshot = DescribeConfiguration();
        var mods = includeInactive ? snapshot.Mods : snapshot.ActiveMods;

        return new
        {
            success = true,
            includeInactive,
            modCount = mods.Count,
            activeCount = snapshot.ActiveMods.Count,
            loadedSessionModCount = snapshot.LoadedSessionMods.Count,
            restartRequired = snapshot.RestartRequired,
            restartReasonCount = snapshot.RestartReasons.Count,
            restartReasons = snapshot.RestartReasons,
            sessionMismatchCount = snapshot.SessionMismatchCount,
            mods = mods.Select(ToResponseEntry).ToList()
        };
    }

    public static object GetModConfigurationStatusResponse()
    {
        var snapshot = DescribeConfiguration();
        return ToResponseStatus(snapshot);
    }

    public static object SetModEnabledResponse(string modId, bool enabled, bool save = true, bool allowDisableCore = false)
    {
        if (!TryResolveMod(modId, out var mod, out var normalizedQuery, out var error))
            return Failure(error, normalizedQuery, includeStatus: true);

        var before = DescribeConfiguration();
        var beforeEntry = before.Mods.FirstOrDefault(candidate => string.Equals(candidate.ModId, GetModId(mod), StringComparison.Ordinal));
        var previousEnabled = beforeEntry?.Enabled ?? ModsConfig.IsActive(mod);

        if (!enabled && mod.IsCoreMod && !allowDisableCore)
        {
            return Failure(
                $"Refusing to disable core mod '{mod.Name}'. Pass allowDisableCore=true to override this guard.",
                normalizedQuery,
                beforeEntry,
                before);
        }

        if (enabled && TryFindConflictingActiveMod(mod, out var conflictingMod))
        {
            return Failure(
                $"Cannot enable '{mod.Name}' while '{conflictingMod.Name}' with the same package id is already active.",
                normalizedQuery,
                beforeEntry,
                before);
        }

        var changed = previousEnabled != enabled;
        if (changed)
        {
            try
            {
                ModsConfig.SetActive(mod, enabled);
                if (save)
                    ModsConfig.Save();
            }
            catch (Exception ex)
            {
                return Failure(
                    $"Updating enabled state for '{normalizedQuery}' failed: {ex.Message}",
                    normalizedQuery,
                    beforeEntry,
                    before);
            }
        }

        var after = DescribeConfiguration();
        var afterEntry = after.Mods.FirstOrDefault(candidate => string.Equals(candidate.ModId, GetModId(mod), StringComparison.Ordinal));
        var currentEnabled = afterEntry?.Enabled ?? enabled;

        return new
        {
            success = true,
            requestedModId = normalizedQuery,
            changed,
            saved = changed && save,
            previousEnabled,
            currentEnabled,
            message = changed
                ? $"{(enabled ? "Enabled" : "Disabled")} mod '{afterEntry?.Name ?? mod.Name}'."
                : $"Mod '{afterEntry?.Name ?? mod.Name}' was already {(enabled ? "enabled" : "disabled")}.",
            mod = afterEntry == null ? null : ToResponseEntry(afterEntry),
            configurationStatus = ToResponseStatus(after)
        };
    }

    public static object ReorderModResponse(string modId, int targetIndex, bool save = true)
    {
        if (!TryResolveMod(modId, out var mod, out var normalizedQuery, out var error))
            return Failure(error, normalizedQuery, includeStatus: true);

        var before = DescribeConfiguration();
        var beforeEntry = before.Mods.FirstOrDefault(candidate => string.Equals(candidate.ModId, GetModId(mod), StringComparison.Ordinal));
        if (beforeEntry?.Enabled != true || !beforeEntry.ActiveLoadOrder.HasValue)
        {
            return Failure(
                $"Mod '{normalizedQuery}' is not currently enabled, so it has no active load-order position to reorder.",
                normalizedQuery,
                beforeEntry,
                before);
        }

        if (targetIndex < 0 || targetIndex >= before.ActiveMods.Count)
        {
            return Failure(
                $"Target index {targetIndex} is out of range for the current active mod list (0-{Math.Max(0, before.ActiveMods.Count - 1)}).",
                normalizedQuery,
                beforeEntry,
                before);
        }

        bool changed;
        string reorderError;
        try
        {
            changed = ModsConfig.TryReorder(beforeEntry.ActiveLoadOrder.Value, targetIndex, out reorderError);
            if (changed && save)
                ModsConfig.Save();
        }
        catch (Exception ex)
        {
            return Failure(
                $"Reordering mod '{normalizedQuery}' failed: {ex.Message}",
                normalizedQuery,
                beforeEntry,
                before);
        }

        if (!changed && string.IsNullOrWhiteSpace(reorderError) == false)
            return Failure(reorderError, normalizedQuery, beforeEntry, before);

        var after = DescribeConfiguration();
        var afterEntry = after.Mods.FirstOrDefault(candidate => string.Equals(candidate.ModId, GetModId(mod), StringComparison.Ordinal));

        return new
        {
            success = true,
            requestedModId = normalizedQuery,
            changed,
            saved = changed && save,
            previousIndex = beforeEntry.ActiveLoadOrder,
            targetIndex,
            currentIndex = afterEntry?.ActiveLoadOrder,
            message = changed
                ? $"Moved mod '{afterEntry?.Name ?? mod.Name}' to active load-order index {afterEntry?.ActiveLoadOrder ?? targetIndex}."
                : $"Mod '{afterEntry?.Name ?? mod.Name}' was already at active load-order index {targetIndex}.",
            mod = afterEntry == null ? null : ToResponseEntry(afterEntry),
            configurationStatus = ToResponseStatus(after)
        };
    }

    private static ModConfigurationSnapshot DescribeConfiguration()
    {
        var installedMods = GetInstalledMods();
        var activeMods = ModsConfig.ActiveModsInLoadOrder?
            .OfType<ModMetaData>()
            .Where(mod => mod != null)
            .ToList()
            ?? [];
        var loadedSessionMods = LoadedModManager.RunningModsListForReading?
            .OfType<ModContentPack>()
            .Where(mod => mod != null)
            .ToList()
            ?? [];
        var warningsByPackageId = ModsConfig.GetModWarnings() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var activeOrderByModId = activeMods
            .Select((mod, index) => new { ModId = GetModId(mod), Index = index })
            .ToDictionary(item => item.ModId, item => item.Index, StringComparer.Ordinal);
        var loadedOrderByModId = loadedSessionMods
            .Select((mod, index) => new { ModId = GetModId(mod), Index = index })
            .ToDictionary(item => item.ModId, item => item.Index, StringComparer.Ordinal);

        var snapshots = installedMods
            .Select(mod => DescribeMod(mod, warningsByPackageId, activeOrderByModId, loadedOrderByModId))
            .OrderBy(mod => mod.Enabled ? 0 : 1)
            .ThenBy(mod => mod.ActiveLoadOrder ?? int.MaxValue)
            .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var activeSnapshots = snapshots
            .Where(mod => mod.Enabled)
            .OrderBy(mod => mod.ActiveLoadOrder ?? int.MaxValue)
            .ToList();
        var loadedSnapshots = snapshots
            .Where(mod => mod.LoadedInSession)
            .OrderBy(mod => mod.LoadedSessionOrder ?? int.MaxValue)
            .ToList();

        var currentConfigurationIds = activeSnapshots.Select(mod => mod.ModId).ToList();
        var loadedSessionIds = loadedSnapshots.Select(mod => mod.ModId).ToList();
        var restartReasons = DescribeRestartReasons(currentConfigurationIds, loadedSessionIds);

        return new ModConfigurationSnapshot
        {
            ConfigPath = GenFilePaths.ModsConfigFilePath,
            CurrentConfigurationHash = ComputeFingerprint(currentConfigurationIds),
            LoadedSessionHash = ComputeFingerprint(loadedSessionIds),
            RestartRequired = restartReasons.Count > 0,
            RestartReasons = restartReasons,
            Mods = snapshots,
            ActiveMods = activeSnapshots,
            LoadedSessionMods = loadedSnapshots,
            ConfigurationIssueCount = activeSnapshots.Count(mod => mod.HasConfigurationWarning),
            VersionWarningCount = activeSnapshots.Count(mod => mod.HasVersionWarning),
            OrderingIssueCount = activeSnapshots.Count(mod => mod.HasOrderingIssues),
            UnsatisfiedDependencyCount = activeSnapshots.Count(mod => mod.UnsatisfiedDependencies.Count > 0),
            SessionMismatchCount = snapshots.Count(mod => !mod.MatchesLoadedSession)
        };
    }

    private static ModConfigurationEntrySnapshot DescribeMod(
        ModMetaData mod,
        IReadOnlyDictionary<string, string> warningsByPackageId,
        IReadOnlyDictionary<string, int> activeOrderByModId,
        IReadOnlyDictionary<string, int> loadedOrderByModId)
    {
        var modId = GetModId(mod);
        var configurationWarning = NormalizeString(
            warningsByPackageId.TryGetValue(mod.PackageId ?? string.Empty, out var warning)
                ? warning
                : string.Empty);
        activeOrderByModId.TryGetValue(modId, out var activeOrder);
        loadedOrderByModId.TryGetValue(modId, out var loadedOrder);
        var hasActiveOrder = activeOrderByModId.ContainsKey(modId);
        var hasLoadedOrder = loadedOrderByModId.ContainsKey(modId);
        var enabled = hasActiveOrder || ModsConfig.IsActive(mod);
        var matchesLoadedSession = enabled == hasLoadedOrder
            && (!enabled || activeOrder == loadedOrder);

        return new ModConfigurationEntrySnapshot
        {
            ModId = modId,
            PackageId = NormalizeString(mod.PackageId),
            PackageIdPlayerFacing = NormalizeString(mod.PackageIdPlayerFacing),
            PackageIdNonUnique = NormalizeString(mod.PackageIdNonUnique),
            Name = NormalizeString(mod.Name),
            ShortName = NormalizeString(mod.ShortName),
            FolderName = NormalizeString(mod.FolderName),
            RootDir = NormalizeRootDir(mod),
            Source = mod.Source.ToString(),
            Authors = NormalizeString(mod.AuthorsString),
            Version = NormalizeString(mod.ModVersion),
            Official = mod.Official,
            OnSteamWorkshop = mod.OnSteamWorkshop,
            IsCoreMod = mod.IsCoreMod,
            Enabled = enabled,
            LoadedInSession = hasLoadedOrder,
            ActiveLoadOrder = hasActiveOrder ? activeOrder : null,
            LoadedSessionOrder = hasLoadedOrder ? loadedOrder : null,
            VersionCompatible = mod.VersionCompatible,
            MadeForNewerVersion = mod.MadeForNewerVersion,
            HasConfigurationWarning = string.IsNullOrWhiteSpace(configurationWarning) == false,
            ConfigurationWarning = configurationWarning,
            HasVersionWarning = !mod.VersionCompatible,
            HasOrderingIssues = hasActiveOrder && ModsConfig.ModHasAnyOrderingIssues(mod),
            MatchesLoadedSession = matchesLoadedSession,
            UnsatisfiedDependencies = NormalizeList(mod.UnsatisfiedDependencies()),
            LoadBefore = NormalizeList(mod.LoadBefore),
            LoadAfter = NormalizeList(mod.LoadAfter),
            ForceLoadBefore = NormalizeList(mod.ForceLoadBefore),
            ForceLoadAfter = NormalizeList(mod.ForceLoadAfter),
            IncompatibleWith = NormalizeList(mod.IncompatibleWith)
        };
    }

    private static IReadOnlyList<ModMetaData> GetInstalledMods()
    {
        ModLister.EnsureInit();
        return ModLister.AllInstalledMods?
            .OfType<ModMetaData>()
            .Where(mod => mod != null)
            .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];
    }

    private static bool TryResolveMod(string modId, out ModMetaData mod, out string normalizedQuery, out string error)
    {
        mod = null;
        normalizedQuery = modId?.Trim() ?? string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            error = "A mod id, package id, package id (player-facing), name, folder name, or root path is required.";
            return false;
        }

        var installedMods = GetInstalledMods();
        var query = normalizedQuery;
        mod = installedMods.FirstOrDefault(candidate => string.Equals(GetModId(candidate), query, StringComparison.Ordinal));
        if (mod != null)
            return true;

        var matches = installedMods
            .Where(candidate => MatchesCandidate(candidate, query))
            .ToList();

        if (matches.Count == 1)
        {
            mod = matches[0];
            return true;
        }

        if (matches.Count > 1)
        {
            error = $"Query '{normalizedQuery}' matched multiple installed mods: {string.Join(", ", matches.Select(GetModId))}. Use the exact modId from rimworld/list_mods.";
            return false;
        }

        error = $"Could not find an installed mod matching '{normalizedQuery}'.";
        return false;
    }

    private static bool MatchesCandidate(ModMetaData mod, string query)
    {
        return string.Equals(mod.PackageId, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mod.PackageIdPlayerFacing, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mod.PackageIdNonUnique, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mod.Name, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mod.ShortName, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mod.FolderName, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeRootDir(mod), query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindConflictingActiveMod(ModMetaData mod, out ModMetaData conflictingMod)
    {
        conflictingMod = GetInstalledMods().FirstOrDefault(candidate =>
            string.Equals(GetModId(candidate), GetModId(mod), StringComparison.Ordinal) == false
            && candidate.Active
            && candidate.SamePackageId(mod.PackageId, ignorePostfix: true));
        return conflictingMod != null;
    }

    private static string GetModId(ModMetaData mod)
    {
        return ModConfigurationIds.CreateId(
            mod.PackageId ?? mod.Name ?? mod.FolderName ?? "unknown-mod",
            NormalizeRootDir(mod));
    }

    private static string GetModId(ModContentPack mod)
    {
        return ModConfigurationIds.CreateId(
            mod.PackageId ?? mod.Name ?? mod.FolderName ?? "unknown-mod",
            NormalizeString(mod.RootDir));
    }

    private static string NormalizeRootDir(ModMetaData mod)
    {
        return mod.RootDir?.FullName?.Trim() ?? string.Empty;
    }

    private static string NormalizeString(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static List<string> NormalizeList(IEnumerable<string> values)
    {
        return values?
            .Where(value => string.IsNullOrWhiteSpace(value) == false)
            .Select(value => value.Trim())
            .ToList()
            ?? [];
    }

    private static List<string> DescribeRestartReasons(IReadOnlyList<string> currentConfigurationIds, IReadOnlyList<string> loadedSessionIds)
    {
        var reasons = new List<string>();
        if (currentConfigurationIds.SequenceEqual(loadedSessionIds, StringComparer.Ordinal))
            return reasons;

        var currentSet = new HashSet<string>(currentConfigurationIds, StringComparer.Ordinal);
        var loadedSet = new HashSet<string>(loadedSessionIds, StringComparer.Ordinal);
        if (!currentSet.SetEquals(loadedSet))
        {
            reasons.Add("active_mod_set_differs_from_loaded_session");
            return reasons;
        }

        reasons.Add("active_mod_order_differs_from_loaded_session");
        return reasons;
    }

    private static string ComputeFingerprint(IEnumerable<string> values)
    {
        var payload = string.Join("\n", values ?? []);
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return string.Concat(hash.Take(8).Select(static value => value.ToString("x2")));
    }

    private static object ToResponseStatus(ModConfigurationSnapshot snapshot)
    {
        return new
        {
            success = true,
            configPath = snapshot.ConfigPath,
            installedModCount = snapshot.Mods.Count,
            activeModCount = snapshot.ActiveMods.Count,
            loadedSessionModCount = snapshot.LoadedSessionMods.Count,
            configurationIssueCount = snapshot.ConfigurationIssueCount,
            versionWarningCount = snapshot.VersionWarningCount,
            orderingIssueCount = snapshot.OrderingIssueCount,
            unsatisfiedDependencyCount = snapshot.UnsatisfiedDependencyCount,
            sessionMismatchCount = snapshot.SessionMismatchCount,
            restartRequired = snapshot.RestartRequired,
            restartReasonCount = snapshot.RestartReasons.Count,
            restartReasons = snapshot.RestartReasons,
            currentConfigurationHash = snapshot.CurrentConfigurationHash,
            loadedSessionHash = snapshot.LoadedSessionHash,
            currentActiveModIds = snapshot.ActiveMods.Select(mod => mod.ModId).ToList(),
            loadedSessionModIds = snapshot.LoadedSessionMods.Select(mod => mod.ModId).ToList(),
            activeMods = snapshot.ActiveMods.Select(ToResponseEntry).ToList(),
            loadedSessionMods = snapshot.LoadedSessionMods.Select(ToResponseEntry).ToList()
        };
    }

    private static object ToResponseEntry(ModConfigurationEntrySnapshot snapshot)
    {
        return new
        {
            modId = snapshot.ModId,
            packageId = snapshot.PackageId,
            packageIdPlayerFacing = snapshot.PackageIdPlayerFacing,
            packageIdNonUnique = snapshot.PackageIdNonUnique,
            name = snapshot.Name,
            shortName = snapshot.ShortName,
            folderName = snapshot.FolderName,
            rootDir = snapshot.RootDir,
            source = snapshot.Source,
            authors = snapshot.Authors,
            version = snapshot.Version,
            official = snapshot.Official,
            onSteamWorkshop = snapshot.OnSteamWorkshop,
            isCoreMod = snapshot.IsCoreMod,
            enabled = snapshot.Enabled,
            loadedInSession = snapshot.LoadedInSession,
            activeLoadOrder = snapshot.ActiveLoadOrder,
            loadedSessionOrder = snapshot.LoadedSessionOrder,
            versionCompatible = snapshot.VersionCompatible,
            madeForNewerVersion = snapshot.MadeForNewerVersion,
            hasConfigurationWarning = snapshot.HasConfigurationWarning,
            configurationWarning = snapshot.ConfigurationWarning,
            hasVersionWarning = snapshot.HasVersionWarning,
            hasOrderingIssues = snapshot.HasOrderingIssues,
            matchesLoadedSession = snapshot.MatchesLoadedSession,
            unsatisfiedDependencies = snapshot.UnsatisfiedDependencies,
            loadBefore = snapshot.LoadBefore,
            loadAfter = snapshot.LoadAfter,
            forceLoadBefore = snapshot.ForceLoadBefore,
            forceLoadAfter = snapshot.ForceLoadAfter,
            incompatibleWith = snapshot.IncompatibleWith
        };
    }

    private static object Failure(
        string message,
        string requestedModId,
        ModConfigurationEntrySnapshot mod = null,
        ModConfigurationSnapshot snapshot = null,
        bool includeStatus = false)
    {
        var effectiveSnapshot = snapshot ?? (includeStatus ? DescribeConfiguration() : null);
        return new
        {
            success = false,
            message,
            requestedModId,
            mod = mod == null ? null : ToResponseEntry(mod),
            configurationStatus = effectiveSnapshot == null ? null : ToResponseStatus(effectiveSnapshot)
        };
    }
}
