using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimBridgeServer.Annotations;
using RimBridgeServer.Core;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgeExtensionDiscovery
{
    private sealed class DiscoveredMethodCandidate
    {
        public ModContentPack Mod { get; set; }

        public Type Type { get; set; }

        public MethodInfo Method { get; set; }

        public ExtensionToolDiscoveryCandidate SelectionCandidate { get; set; }
    }

    public static IReadOnlyList<AnnotatedExtensionCapabilityProvider> DiscoverProviders(IEnumerable<string> reservedAliases = null)
    {
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
        var candidates = new List<DiscoveredMethodCandidate>();
        var nextCandidateId = 1;

        foreach (var mod in runningMods)
        {
            try
            {
                CollectToolCandidates(mod, candidates, ref nextCandidateId);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimBridge] Failed to scan annotated tools for mod '{DescribeMod(mod)}': {ex}");
            }
        }

        var selectedCandidates = ExtensionToolCandidateSelector.Select(
            candidates.Select(candidate => candidate.SelectionCandidate),
            reservedAliases);
        var candidatesById = candidates.ToDictionary(candidate => candidate.SelectionCandidate.CandidateId, StringComparer.Ordinal);
        return BuildProviders(selectedCandidates, candidatesById, loadedModHandles);
    }

    private static void CollectToolCandidates(ModContentPack mod, IList<DiscoveredMethodCandidate> discovered, ref int nextCandidateId)
    {
        var seenAssemblies = new HashSet<Assembly>();
        var assemblies = mod.assemblies?.loadedAssemblies?
            .OfType<Assembly>()
            .Where(assembly => assembly != null && !assembly.IsDynamic)
            .ToList()
            ?? [];
        var modProviderId = CreateProviderId(mod);
        var modSortKey = CreateModSortKey(mod);

        foreach (var assembly in assemblies)
        {
            if (!seenAssemblies.Add(assembly))
                continue;

            try
            {
                var assemblyName = GetAssemblyName(assembly);
                var assemblyFullName = GetAssemblyFullName(assembly);
                var assemblyIdentity = CreateAssemblyIdentity(assembly);
                foreach (var type in SafeGetTypes(mod, assembly).OrderBy(type => type.FullName ?? type.Name, StringComparer.Ordinal))
                {
                    try
                    {
                        if (type == null || type.IsAbstract || type.ContainsGenericParameters)
                            continue;

                        var annotatedMethods = GetAnnotatedMethods(type);
                        if (annotatedMethods.Count == 0)
                            continue;

                        foreach (var method in annotatedMethods)
                        {
                            var attribute = method.GetCustomAttribute<ToolAttribute>(inherit: false);
                            if (attribute == null)
                                continue;

                            var candidateId = "extension-tool-" + nextCandidateId.ToString(CultureInfo.InvariantCulture);
                            nextCandidateId++;
                            discovered.Add(new DiscoveredMethodCandidate
                            {
                                Mod = mod,
                                Type = type,
                                Method = method,
                                SelectionCandidate = new ExtensionToolDiscoveryCandidate
                                {
                                    CandidateId = candidateId,
                                    ToolName = attribute.Name,
                                    MethodIdentity = CreateMethodIdentity(method),
                                    AssemblyIdentity = assemblyIdentity,
                                    AssemblyName = assemblyName,
                                    AssemblyFullName = assemblyFullName,
                                    ModProviderId = modProviderId,
                                    ModSortKey = modSortKey,
                                    TypeName = type.FullName ?? type.Name,
                                    MethodName = method.Name,
                                    MetadataToken = GetMetadataToken(method)
                                }
                            });
                        }
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
    }

    private static IReadOnlyList<AnnotatedExtensionCapabilityProvider> BuildProviders(
        IEnumerable<SelectedExtensionToolCandidate> selectedCandidates,
        IReadOnlyDictionary<string, DiscoveredMethodCandidate> candidatesById,
        IReadOnlyDictionary<Type, Mod> loadedModHandles)
    {
        var providers = new List<AnnotatedExtensionCapabilityProvider>();

        foreach (var providerGroup in selectedCandidates
            .Where(selected => candidatesById.ContainsKey(selected.CandidateId))
            .GroupBy(selected => selected.ProviderId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var toolClasses = new List<AnnotatedExtensionCapabilityProvider.ToolClass>();
            var providerCandidates = providerGroup
                .Select(selected => candidatesById[selected.CandidateId])
                .ToList();

            foreach (var typeGroup in providerCandidates
                .GroupBy(candidate => candidate.Type)
                .OrderBy(group => group.Key.FullName ?? group.Key.Name, StringComparer.Ordinal))
            {
                try
                {
                    var representative = typeGroup
                        .OrderBy(candidate => CreateModSortKey(candidate.Mod), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(candidate => candidate.SelectionCandidate.CandidateId, StringComparer.Ordinal)
                        .First();
                    var methods = typeGroup
                        .Select(candidate => candidate.Method)
                        .OrderBy(method => method.Name, StringComparer.Ordinal)
                        .ThenBy(GetMetadataToken)
                        .ToList();

                    if (!TryCreateInstance(representative.Mod, typeGroup.Key, loadedModHandles, methods, out var instance, out var error))
                    {
                        Log.Warning($"[RimBridge] Skipping annotated tool type '{typeGroup.Key.FullName ?? typeGroup.Key.Name}' from mod '{DescribeMod(representative.Mod)}': {error}");
                        continue;
                    }

                    toolClasses.Add(new AnnotatedExtensionCapabilityProvider.ToolClass
                    {
                        Type = typeGroup.Key,
                        Instance = instance,
                        Methods = methods
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimBridge] Failed to prepare annotated tool type '{typeGroup.Key?.FullName ?? typeGroup.Key?.Name ?? "unknown-type"}': {ex}");
                }
            }

            if (toolClasses.Count == 0)
                continue;

            providers.Add(new AnnotatedExtensionCapabilityProvider(
                providerId: providerGroup.Key,
                category: "extension",
                toolClasses: toolClasses));
        }

        return providers;
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

    private static string CreateModSortKey(ModContentPack mod)
    {
        return string.Join("\n", mod?.PackageId ?? mod?.Name ?? mod?.FolderName ?? "unknown-mod", mod?.RootDir ?? string.Empty);
    }

    private static string CreateAssemblyIdentity(Assembly assembly)
    {
        try
        {
            return GetAssemblyFullName(assembly) + "|" + CreateModuleIdentity(assembly.ManifestModule);
        }
        catch
        {
            return GetAssemblyFullName(assembly) + "|unknown-module";
        }
    }

    private static string CreateMethodIdentity(MethodInfo method)
    {
        var moduleIdentity = CreateModuleIdentity(method.Module);
        var metadataToken = GetMetadataToken(method);
        if (metadataToken != 0)
            return moduleIdentity + ":" + metadataToken.ToString(CultureInfo.InvariantCulture);

        var parameters = string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name));
        return moduleIdentity + ":" + (method.DeclaringType?.FullName ?? method.DeclaringType?.Name ?? "unknown-type") + "." + method.Name + "(" + parameters + ")";
    }

    private static string CreateModuleIdentity(Module module)
    {
        try
        {
            return module.ModuleVersionId.ToString("N");
        }
        catch
        {
            return module.Name ?? "unknown-module";
        }
    }

    private static string GetAssemblyName(Assembly assembly)
    {
        try
        {
            return assembly.GetName().Name ?? "unknown-assembly";
        }
        catch
        {
            return "unknown-assembly";
        }
    }

    private static string GetAssemblyFullName(Assembly assembly)
    {
        try
        {
            return assembly.GetName().FullName ?? assembly.FullName ?? GetAssemblyName(assembly);
        }
        catch
        {
            return assembly.FullName ?? GetAssemblyName(assembly);
        }
    }

    private static int GetMetadataToken(MemberInfo member)
    {
        try
        {
            return member.MetadataToken;
        }
        catch
        {
            return 0;
        }
    }

    private static string DescribeMod(ModContentPack mod)
    {
        return string.IsNullOrWhiteSpace(mod?.PackageId) ? mod?.Name ?? "unknown-mod" : mod.PackageId;
    }
}
