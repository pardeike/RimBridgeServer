using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimBridgeServer.Annotations;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgeExtensionDiscovery
{
    public static IReadOnlyList<AnnotatedExtensionCapabilityProvider> DiscoverProviders()
    {
        var providers = new List<AnnotatedExtensionCapabilityProvider>();
        var loadedModHandles = LoadedModManager.ModHandles?
            .OfType<Mod>()
            .Where(handle => handle != null)
            .ToDictionary(handle => handle.GetType(), handle => handle)
            ?? new Dictionary<Type, Mod>();
        var runningMods = LoadedModManager.RunningModsListForReading?
            .OfType<ModContentPack>()
            .Where(mod => mod != null)
            .OrderBy(mod => mod.PackageId ?? mod.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];

        foreach (var mod in runningMods)
        {
            try
            {
                var toolClasses = DiscoverToolClasses(mod, loadedModHandles);
                if (toolClasses.Count == 0)
                    continue;

                providers.Add(new AnnotatedExtensionCapabilityProvider(
                    providerId: CreateProviderId(mod),
                    category: "extension",
                    toolClasses: toolClasses));
            }
            catch (Exception ex)
            {
                Log.Error($"[RimBridge] Failed to scan annotated tools for mod '{DescribeMod(mod)}': {ex}");
            }
        }

        return providers;
    }

    private static List<AnnotatedExtensionCapabilityProvider.ToolClass> DiscoverToolClasses(ModContentPack mod, IReadOnlyDictionary<Type, Mod> loadedModHandles)
    {
        var discovered = new List<AnnotatedExtensionCapabilityProvider.ToolClass>();
        var seenAssemblies = new HashSet<Assembly>();
        var assemblies = mod.assemblies?.loadedAssemblies?
            .OfType<Assembly>()
            .Where(assembly => assembly != null && !assembly.IsDynamic)
            .ToList()
            ?? [];

        foreach (var assembly in assemblies)
        {
            if (!seenAssemblies.Add(assembly))
                continue;

            try
            {
                foreach (var type in SafeGetTypes(mod, assembly).OrderBy(type => type.FullName ?? type.Name, StringComparer.Ordinal))
                {
                    try
                    {
                        if (type == null || type.IsAbstract || type.ContainsGenericParameters)
                            continue;

                        var annotatedMethods = GetAnnotatedMethods(type);
                        if (annotatedMethods.Count == 0)
                            continue;

                        if (!TryCreateInstance(mod, type, loadedModHandles, annotatedMethods, out var instance, out var error))
                        {
                            Log.Warning($"[RimBridge] Skipping annotated tool type '{type.FullName ?? type.Name}' from mod '{DescribeMod(mod)}': {error}");
                            continue;
                        }

                        discovered.Add(new AnnotatedExtensionCapabilityProvider.ToolClass
                        {
                            Type = type,
                            Instance = instance,
                            Methods = annotatedMethods
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[RimBridge] Failed to inspect annotated tool type '{type?.FullName ?? type?.Name ?? "unknown-type"}' from mod '{DescribeMod(mod)}': {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimBridge] Failed to inspect assembly '{assembly.FullName ?? assembly.GetName().Name ?? "unknown-assembly"}' for mod '{DescribeMod(mod)}': {ex}");
            }
        }

        return discovered;
    }

    private static bool TryCreateInstance(
        ModContentPack mod,
        Type type,
        IReadOnlyDictionary<Type, Mod> loadedModHandles,
        IReadOnlyCollection<MethodInfo> annotatedMethods,
        out object instance,
        out string error)
    {
        instance = null;
        error = string.Empty;

        if (annotatedMethods.All(method => method.IsStatic))
            return true;

        if (loadedModHandles.TryGetValue(type, out var existingHandle))
        {
            instance = existingHandle;
            return true;
        }

        if (typeof(Mod).IsAssignableFrom(type))
        {
            error = $"type '{type.FullName ?? type.Name}' derives from Verse.Mod but no loaded mod handle instance was available for '{DescribeMod(mod)}'.";
            return false;
        }

        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor == null)
        {
            error = $"type '{type.FullName ?? type.Name}' has instance tool methods but no public parameterless constructor.";
            return false;
        }

        try
        {
            instance = Activator.CreateInstance(type);
            return true;
        }
        catch (Exception ex)
        {
            error = $"creating '{type.FullName ?? type.Name}' failed: {ex.Message}";
            return false;
        }
    }

    private static List<MethodInfo> GetAnnotatedMethods(Type type)
    {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.IsSpecialName == false)
            .Where(method => method.ContainsGenericParameters == false)
            .Where(method => method.GetCustomAttribute<ToolAttribute>(inherit: false) != null)
            .ToList();
    }

    private static IEnumerable<Type> SafeGetTypes(ModContentPack mod, Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            if (ex.LoaderExceptions != null)
            {
                foreach (var loaderException in ex.LoaderExceptions.Where(exception => exception != null))
                    Log.Warning($"[RimBridge] Loader exception while scanning mod '{DescribeMod(mod)}' assembly '{assembly.FullName ?? assembly.GetName().Name ?? "unknown-assembly"}': {loaderException.Message}");
            }

            return ex.Types.Where(type => type != null);
        }
    }

    private static string CreateProviderId(ModContentPack mod)
    {
        var packageId = (mod.PackageId ?? mod.Name ?? mod.FolderName ?? "unknown-mod").Trim();
        var rootDir = (mod.RootDir ?? string.Empty).Trim();
        return ModConfigurationIds.CreateId(packageId, rootDir).Replace(':', '.') + "/annotations";
    }

    private static string DescribeMod(ModContentPack mod)
    {
        return string.IsNullOrWhiteSpace(mod?.PackageId) ? mod?.Name ?? "unknown-mod" : mod.PackageId;
    }
}
