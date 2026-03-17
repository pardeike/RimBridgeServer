using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal static class RimWorldModSettings
{
    private sealed class SettingsCategoryResult
    {
        public string Category { get; set; }

        public string Error { get; set; }
    }

    private sealed class ModSettingsSurfaceSnapshot
    {
        public string ModId { get; set; }

        public string PackageId { get; set; }

        public string PackageIdPlayerFacing { get; set; }

        public string Name { get; set; }

        public string FolderName { get; set; }

        public string Authors { get; set; }

        public string Version { get; set; }

        public string RootDir { get; set; }

        public string HandleType { get; set; }

        public string HandleTypeName { get; set; }

        public string SettingsType { get; set; }

        public string SettingsCategory { get; set; }

        public string SettingsCategoryError { get; set; }

        public bool HasSettingsWindow { get; set; }

        public bool HasPersistentState { get; set; }

        public bool SupportsRead { get; set; }
    }

    public static object ListModSettingsSurfacesResponse(bool includeWithoutSettings = false)
    {
        var surfaces = GetLoadedModHandles()
            .Select(DescribeSurface)
            .Where(surface => includeWithoutSettings || surface.HasSettingsWindow || surface.HasPersistentState)
            .Select(ToResponseSurface)
            .ToList();

        return new
        {
            success = true,
            surfaceCount = surfaces.Count,
            surfaces
        };
    }

    public static object GetModSettingsResponse(string modId, int maxDepth = 4, int maxCollectionEntries = 32)
    {
        if (!TryResolveMod(modId, out var mod, out var normalizedQuery, out var error))
        {
            return Failure(error, normalizedQuery);
        }

        if (mod.modSettings == null)
        {
            return new
            {
                success = false,
                message = $"Mod '{normalizedQuery}' does not currently expose a persistent ModSettings instance.",
                requestedModId = normalizedQuery,
                mod = DescribeSurface(mod)
            };
        }

        var settings = SemanticValueGraph.Describe(mod.modSettings, new SemanticValueGraphOptions
        {
            MaxDepth = maxDepth,
            MaxCollectionEntries = maxCollectionEntries
        });

        return new
        {
            success = true,
            requestedModId = normalizedQuery,
            mod = ToResponseSurface(DescribeSurface(mod)),
            settingsType = mod.modSettings.GetType().FullName ?? mod.modSettings.GetType().Name,
            root = ToResponseNode(settings),
            topLevelSettingCount = settings.Children.Count
        };
    }

    public static object UpdateModSettingsResponse(string modId, Dictionary<string, object> values, bool write = true, int maxDepth = 4, int maxCollectionEntries = 32)
    {
        if (!TryResolveMod(modId, out var mod, out var normalizedQuery, out var error))
            return Failure(error, normalizedQuery);

        if (mod.modSettings == null)
        {
            return new
            {
                success = false,
                message = $"Mod '{normalizedQuery}' does not currently expose a persistent ModSettings instance.",
                requestedModId = normalizedQuery,
                mod = ToResponseSurface(DescribeSurface(mod))
            };
        }

        if (values == null || values.Count == 0)
        {
            return new
            {
                success = false,
                message = "At least one settings path/value pair is required.",
                requestedModId = normalizedQuery,
                mod = ToResponseSurface(DescribeSurface(mod))
            };
        }

        IReadOnlyList<ObjectPathUpdateResult> updates;
        try
        {
            updates = ObjectPathUpdater.Apply(mod.modSettings, values);
            if (write)
                mod.WriteSettings();
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                message = $"Updating mod settings for '{normalizedQuery}' failed: {ex.Message}",
                requestedModId = normalizedQuery,
                mod = ToResponseSurface(DescribeSurface(mod))
            };
        }

        var root = SemanticValueGraph.Describe(mod.modSettings, new SemanticValueGraphOptions
        {
            MaxDepth = maxDepth,
            MaxCollectionEntries = maxCollectionEntries
        });

        return new
        {
            success = true,
            requestedModId = normalizedQuery,
            wroteSettings = write,
            changed = updates.Any(update => update.Changed),
            updateCount = updates.Count,
            updates = updates.Select(update => new
            {
                path = update.Path,
                typeName = update.TypeName,
                changed = update.Changed,
                previous = ToResponseNode(SemanticValueGraph.Describe(update.PreviousValue)),
                current = ToResponseNode(SemanticValueGraph.Describe(update.CurrentValue))
            }).ToList(),
            mod = ToResponseSurface(DescribeSurface(mod)),
            root = ToResponseNode(root),
            topLevelSettingCount = root.Children.Count
        };
    }

    public static object ReloadModSettingsResponse(string modId, int maxDepth = 4, int maxCollectionEntries = 32)
    {
        if (!TryResolveMod(modId, out var mod, out var normalizedQuery, out var error))
            return Failure(error, normalizedQuery);

        if (mod.modSettings == null)
        {
            return new
            {
                success = false,
                message = $"Mod '{normalizedQuery}' does not currently expose a persistent ModSettings instance.",
                requestedModId = normalizedQuery,
                mod = ToResponseSurface(DescribeSurface(mod))
            };
        }

        var previous = mod.modSettings;
        try
        {
            var settingsType = previous.GetType();
            var getSettingsMethod = typeof(Mod)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .First(method => method.Name == nameof(Mod.GetSettings) && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);

            mod.modSettings = null;
            var reloaded = getSettingsMethod.MakeGenericMethod(settingsType).Invoke(mod, Array.Empty<object>()) as ModSettings;
            if (reloaded == null)
                throw new InvalidOperationException($"Reloading mod settings returned no '{settingsType.FullName ?? settingsType.Name}' instance.");
        }
        catch (Exception ex)
        {
            mod.modSettings = previous;
            return new
            {
                success = false,
                message = $"Reloading mod settings for '{normalizedQuery}' failed: {ex.InnerException?.Message ?? ex.Message}",
                requestedModId = normalizedQuery,
                mod = ToResponseSurface(DescribeSurface(mod))
            };
        }

        var root = SemanticValueGraph.Describe(mod.modSettings, new SemanticValueGraphOptions
        {
            MaxDepth = maxDepth,
            MaxCollectionEntries = maxCollectionEntries
        });

        return new
        {
            success = true,
            requestedModId = normalizedQuery,
            message = $"Reloaded mod settings for '{normalizedQuery}' from disk.",
            mod = ToResponseSurface(DescribeSurface(mod)),
            root = ToResponseNode(root),
            topLevelSettingCount = root.Children.Count
        };
    }

    public static object OpenModSettingsResponse(string modId, bool replaceExisting = true)
    {
        if (!TryResolveMod(modId, out var mod, out var normalizedQuery, out var error))
            return Failure(error, normalizedQuery);

        var settingsCategory = TryGetSettingsCategory(mod);
        if (string.IsNullOrWhiteSpace(settingsCategory.Category))
        {
            return new
            {
                success = false,
                message = $"Mod '{normalizedQuery}' does not expose a settings dialog.",
                requestedModId = normalizedQuery,
                mod = ToResponseSurface(DescribeSurface(mod))
            };
        }

        var before = RimWorldInput.GetUiState();
        var windowStack = Find.WindowStack;
        if (windowStack == null)
        {
            return new
            {
                success = false,
                message = "RimWorld window stack is not available.",
                requestedModId = normalizedQuery,
                mod = ToResponseSurface(DescribeSurface(mod)),
                before = RimWorldInput.DescribeUiState(before),
                after = RimWorldInput.DescribeUiState(before)
            };
        }

        var openDialogs = windowStack.Windows?.OfType<Dialog_ModSettings>().ToList() ?? [];
        var matchingDialog = openDialogs.LastOrDefault(dialog => string.Equals(GetSurfaceId(dialog.mod), GetSurfaceId(mod), StringComparison.Ordinal));
        if (matchingDialog != null && !replaceExisting)
        {
            return new
            {
                success = true,
                changed = false,
                requestedModId = normalizedQuery,
                message = $"Settings dialog for '{normalizedQuery}' is already open.",
                mod = ToResponseSurface(DescribeSurface(mod)),
                dialog = DescribeOpenDialog(matchingDialog),
                before = RimWorldInput.DescribeUiState(before),
                after = RimWorldInput.DescribeUiState(before)
            };
        }

        var closedDialogCount = 0;
        if (replaceExisting)
        {
            foreach (var dialog in openDialogs)
            {
                windowStack.TryRemove(dialog, doCloseSound: false);
                closedDialogCount++;
            }
        }

        var createdDialog = new Dialog_ModSettings(mod);
        windowStack.Add(createdDialog);
        var after = RimWorldInput.GetUiState();

        return new
        {
            success = true,
            changed = true,
            requestedModId = normalizedQuery,
            replaceExisting,
            closedExistingDialogCount = closedDialogCount,
            message = $"Opened mod settings dialog for '{normalizedQuery}'.",
            mod = ToResponseSurface(DescribeSurface(mod)),
            dialog = DescribeOpenDialog(createdDialog),
            before = RimWorldInput.DescribeUiState(before),
            after = RimWorldInput.DescribeUiState(after)
        };
    }

    public static bool TryDescribeWindow(Window window, out string semanticKind, out object semanticDetails)
    {
        semanticKind = string.Empty;
        semanticDetails = null;

        if (window is not Dialog_ModSettings dialog || dialog.mod == null)
            return false;

        semanticKind = "mod_settings_dialog";
        semanticDetails = DescribeOpenDialog(dialog);
        return true;
    }

    private static bool TryResolveMod(string modId, out Mod mod, out string normalizedQuery, out string error)
    {
        mod = null;
        normalizedQuery = modId?.Trim() ?? string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            error = "A mod id, package id, settings category, or handle type is required.";
            return false;
        }

        var handles = GetLoadedModHandles();
        var query = normalizedQuery;
        mod = handles.FirstOrDefault(candidate => string.Equals(GetSurfaceId(candidate), query, StringComparison.Ordinal));
        if (mod != null)
            return true;

        var matches = handles
            .Where(candidate => MatchesCandidate(candidate, query))
            .ToList();

        if (matches.Count == 1)
        {
            mod = matches[0];
            return true;
        }

        if (matches.Count > 1)
        {
            error = $"Query '{query}' matched multiple loaded mods: {string.Join(", ", matches.Select(GetSurfaceId))}. Use the exact modId from rimworld/list_mod_settings_surfaces.";
            return false;
        }

        error = $"Could not find a loaded mod settings surface matching '{query}'.";
        return false;
    }

    private static bool MatchesCandidate(Mod mod, string query)
    {
        var content = mod.Content;
        var category = TryGetSettingsCategory(mod);
        var handleType = mod.GetType();
        return string.Equals(content?.PackageId, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(content?.PackageIdPlayerFacing, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(content?.Name, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(handleType.FullName, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(handleType.Name, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(category.Category, query, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<Mod> GetLoadedModHandles()
    {
        return LoadedModManager.ModHandles?
            .OfType<Mod>()
            .Where(mod => mod != null)
            .OrderBy(mod => mod.Content?.PackageId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(mod => mod.GetType().FullName ?? mod.GetType().Name, StringComparer.Ordinal)
            .ToList()
            ?? [];
    }

    private static ModSettingsSurfaceSnapshot DescribeSurface(Mod mod)
    {
        var content = mod.Content;
        var metadata = content?.ModMetaData;
        var settingsCategory = TryGetSettingsCategory(mod);
        var settingsType = mod.modSettings?.GetType();
        var handleType = mod.GetType();

        return new ModSettingsSurfaceSnapshot
        {
            ModId = GetSurfaceId(mod),
            PackageId = content?.PackageId,
            PackageIdPlayerFacing = content?.PackageIdPlayerFacing,
            Name = content?.Name,
            FolderName = content?.FolderName,
            Authors = metadata?.AuthorsString,
            Version = metadata?.ModVersion,
            RootDir = content?.RootDir,
            HandleType = handleType.FullName ?? handleType.Name,
            HandleTypeName = handleType.Name,
            SettingsType = settingsType?.FullName ?? settingsType?.Name,
            SettingsCategory = settingsCategory.Category,
            SettingsCategoryError = settingsCategory.Error,
            HasSettingsWindow = string.IsNullOrWhiteSpace(settingsCategory.Category) == false,
            HasPersistentState = mod.modSettings != null,
            SupportsRead = mod.modSettings != null
        };
    }

    private static string GetSurfaceId(Mod mod)
    {
        return ModSettingsIds.CreateId(
            mod.Content?.PackageId ?? mod.GetType().FullName ?? mod.GetType().Name,
            mod.GetType().FullName ?? mod.GetType().Name);
    }

    private static SettingsCategoryResult TryGetSettingsCategory(Mod mod)
    {
        try
        {
            return new SettingsCategoryResult
            {
                Category = NormalizeString(mod.SettingsCategory())
            };
        }
        catch (Exception ex)
        {
            return new SettingsCategoryResult
            {
                Category = string.Empty,
                Error = ex.Message
            };
        }
    }

    private static string NormalizeString(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static object DescribeOpenDialog(Dialog_ModSettings dialog)
    {
        return new
        {
            windowType = dialog.GetType().FullName ?? dialog.GetType().Name,
            windowId = dialog.ID,
            mod = ToResponseSurface(DescribeSurface(dialog.mod))
        };
    }

    private static object ToResponseSurface(ModSettingsSurfaceSnapshot surface)
    {
        return new
        {
            modId = surface.ModId,
            packageId = surface.PackageId,
            packageIdPlayerFacing = surface.PackageIdPlayerFacing,
            name = surface.Name,
            folderName = surface.FolderName,
            authors = surface.Authors,
            version = surface.Version,
            rootDir = surface.RootDir,
            handleType = surface.HandleType,
            handleTypeName = surface.HandleTypeName,
            settingsType = surface.SettingsType,
            settingsCategory = surface.SettingsCategory,
            settingsCategoryError = surface.SettingsCategoryError,
            hasSettingsWindow = surface.HasSettingsWindow,
            hasPersistentState = surface.HasPersistentState,
            supportsRead = surface.SupportsRead
        };
    }

    private static object ToResponseNode(SemanticValueNode node)
    {
        return new
        {
            name = node.Name,
            path = node.Path,
            valueKind = node.ValueKind,
            typeName = node.TypeName,
            value = node.Value,
            count = node.Count,
            truncated = node.Truncated,
            children = node.Children.Select(ToResponseNode).ToList()
        };
    }

    private static object Failure(string message, string requestedModId)
    {
        return new
        {
            success = false,
            message,
            requestedModId
        };
    }
}
