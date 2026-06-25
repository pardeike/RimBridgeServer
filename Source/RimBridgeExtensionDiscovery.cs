using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimBridgeServer.Core;
using RimBridgeServer.Sdk;
using Verse;

namespace RimBridgeServer;

internal static class RimBridgeExtensionDiscovery
{
    private sealed class DependencyScope
    {
        public string BridgeToolsRoot { get; set; }

        public string BundleDirectory { get; set; }

        public string OwnerId { get; set; }
    }

    private sealed class LoadedCompanionAssembly
    {
        public Assembly Assembly { get; set; }

        public CompanionAssemblyCandidate Candidate { get; set; }
    }

    private sealed class DiscoveredMethodCandidate
    {
        public CompanionAssemblyCandidate Candidate { get; set; }

        public Type Type { get; set; }

        public MethodInfo Method { get; set; }
    }

    private static readonly object ResolverSync = new();
    private static readonly Dictionary<string, DependencyScope> ScopesByAssemblyPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Assembly> LoadedAssembliesByPath = new(StringComparer.OrdinalIgnoreCase);
    private static bool _resolverInstalled;

    public static IReadOnlyList<AnnotatedExtensionCapabilityProvider> DiscoverProviders(IEnumerable<string> reservedAliases = null)
    {
        InstallResolver();
        var loadedAssemblies = LoadCompanionAssemblies();
        var candidates = new List<DiscoveredMethodCandidate>();

        foreach (var loaded in loadedAssemblies)
        {
            try
            {
                CollectToolCandidates(loaded, candidates);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimBridge] Failed to scan companion assembly '{loaded.Candidate.AssemblyPath}': {ex}");
            }
        }

        return BuildProviders(candidates);
    }

    private static IReadOnlyList<LoadedCompanionAssembly> LoadCompanionAssemblies()
    {
        var result = new List<LoadedCompanionAssembly>();
        foreach (var candidate in DiscoverCompanionCandidates())
        {
            try
            {
                RegisterScope(candidate);
                WarnAboutLocalSdk(candidate);
                var assembly = LoadScopedAssembly(candidate.AssemblyPath, CreateScope(candidate));
                if (assembly != null)
                {
                    result.Add(new LoadedCompanionAssembly
                    {
                        Assembly = assembly,
                        Candidate = candidate
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimBridge] Failed to load companion assembly '{candidate.AssemblyPath}': {ex}");
            }
        }

        return result;
    }

    private static IEnumerable<CompanionAssemblyCandidate> DiscoverCompanionCandidates()
    {
        var globalRoot = TryGetGlobalBridgeToolsRoot();
        if (string.IsNullOrWhiteSpace(globalRoot) == false)
        {
            foreach (var candidate in CompanionFileDiscovery.DiscoverBridgeToolsRoot(globalRoot, CompanionRootKind.Global, "global"))
                yield return candidate;
        }

        var runningMods = LoadedModManager.RunningModsListForReading?
            .OfType<ModContentPack>()
            .Where(mod => mod != null)
            .ToList()
            ?? [];

        foreach (var mod in runningMods)
        {
            foreach (var candidate in DiscoverModBridgeTools(mod))
                yield return candidate;
        }
    }

    private static IEnumerable<CompanionAssemblyCandidate> DiscoverModBridgeTools(ModContentPack mod)
    {
        var folders = mod.foldersToLoadDescendingOrder?
            .Where(folder => string.IsNullOrWhiteSpace(folder) == false)
            .ToList()
            ?? [];
        var discovered = new List<(string RelativeKey, CompanionAssemblyCandidate Candidate)>();

        for (var i = folders.Count - 1; i >= 0; i--)
        {
            var folder = folders[i];
            var root = Path.Combine(folder, CompanionFileDiscovery.BridgeToolsFolderName);
            foreach (var candidate in CompanionFileDiscovery.DiscoverBridgeToolsRoot(root, CompanionRootKind.Mod, CreateOwnerId(mod)))
            {
                discovered.Add((CreateRelativeKey(candidate), candidate));
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = discovered.Count - 1; i >= 0; i--)
        {
            var entry = discovered[i];
            if (seen.Add(entry.RelativeKey) == false)
                discovered.RemoveAt(i);
        }

        foreach (var entry in discovered)
            yield return entry.Candidate;
    }

    private static string TryGetGlobalBridgeToolsRoot()
    {
        try
        {
            var modsFolder = GenFilePaths.ModsFolderPath;
            var parent = Directory.GetParent(modsFolder);
            return parent == null
                ? null
                : Path.Combine(parent.FullName, CompanionFileDiscovery.BridgeToolsFolderName);
        }
        catch (Exception ex)
        {
            Log.Warning($"[RimBridge] Could not resolve global BridgeTools folder: {ex.Message}");
            return null;
        }
    }

    private static string CreateRelativeKey(CompanionAssemblyCandidate candidate)
    {
        var root = candidate.BridgeToolsRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
        var path = candidate.AssemblyPath ?? string.Empty;
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return Path.GetFileName(path);
    }

    private static void CollectToolCandidates(LoadedCompanionAssembly loaded, IList<DiscoveredMethodCandidate> discovered)
    {
        foreach (var type in SafeGetTypes(loaded.Assembly, loaded.Candidate).OrderBy(type => type.FullName ?? type.Name, StringComparer.Ordinal))
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
                    discovered.Add(new DiscoveredMethodCandidate
                    {
                        Candidate = loaded.Candidate,
                        Type = type,
                        Method = method
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimBridge] Failed to inspect companion tool type '{type?.FullName ?? type?.Name ?? "unknown-type"}' from '{loaded.Candidate.AssemblyPath}': {ex}");
            }
        }
    }

    private static IReadOnlyList<AnnotatedExtensionCapabilityProvider> BuildProviders(IEnumerable<DiscoveredMethodCandidate> candidates)
    {
        var providers = new List<AnnotatedExtensionCapabilityProvider>();

        foreach (var assemblyGroup in candidates
            .GroupBy(candidate => candidate.Candidate.AssemblyPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var providerCandidates = assemblyGroup.ToList();
            var toolClasses = new List<AnnotatedExtensionCapabilityProvider.ToolClass>();

            foreach (var typeGroup in providerCandidates
                .GroupBy(candidate => candidate.Type)
                .OrderBy(group => group.Key.FullName ?? group.Key.Name, StringComparer.Ordinal))
            {
                try
                {
                    var methods = typeGroup
                        .Select(candidate => candidate.Method)
                        .OrderBy(method => method.Name, StringComparer.Ordinal)
                        .ThenBy(GetMetadataToken)
                        .ToList();

                    if (!TryCreateInstance(typeGroup.Key, methods, out var instance, out var error))
                    {
                        Log.Warning($"[RimBridge] Skipping companion tool type '{typeGroup.Key.FullName ?? typeGroup.Key.Name}' from '{assemblyGroup.Key}': {error}");
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
                    Log.Error($"[RimBridge] Failed to prepare companion tool type '{typeGroup.Key?.FullName ?? typeGroup.Key?.Name ?? "unknown-type"}': {ex}");
                }
            }

            if (toolClasses.Count == 0)
                continue;

            var representative = providerCandidates[0].Candidate;
            providers.Add(new AnnotatedExtensionCapabilityProvider(
                providerId: CreateProviderId(representative),
                category: "extension",
                toolClasses: toolClasses));
        }

        return providers;
    }

    private static bool TryCreateInstance(
        Type type,
        IReadOnlyCollection<MethodInfo> annotatedMethods,
        out object instance,
        out string error)
    {
        instance = null;
        error = string.Empty;

        if (annotatedMethods.All(method => method.IsStatic))
            return true;

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

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly, CompanionAssemblyCandidate candidate)
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
                    Log.Warning($"[RimBridge] Loader exception while scanning companion assembly '{candidate.AssemblyPath}': {loaderException.Message}");
            }

            return ex.Types.Where(type => type != null);
        }
    }

    private static void InstallResolver()
    {
        lock (ResolverSync)
        {
            if (_resolverInstalled)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += ResolveCompanionAssembly;
            _resolverInstalled = true;
        }
    }

    private static Assembly ResolveCompanionAssembly(object sender, ResolveEventArgs args)
    {
        var requestedName = new AssemblyName(args.Name);
        if (string.Equals(requestedName.Name, CompanionFileDiscovery.SdkAssemblyName, StringComparison.OrdinalIgnoreCase))
            return typeof(ToolAttribute).Assembly;

        var scope = TryGetScope(args.RequestingAssembly);
        if (scope != null)
        {
            foreach (var directory in GetResolutionDirectories(scope))
            {
                var path = Path.Combine(directory, requestedName.Name + ".dll");
                if (File.Exists(path))
                    return LoadScopedAssembly(path, scope);
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
            {
                try
                {
                    return string.Equals(assembly.GetName().FullName, args.Name, StringComparison.Ordinal);
                }
                catch
                {
                    return false;
                }
            });
    }

    private static IEnumerable<string> GetResolutionDirectories(DependencyScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.BundleDirectory) == false)
            yield return scope.BundleDirectory;
        if (string.IsNullOrWhiteSpace(scope.BridgeToolsRoot) == false)
            yield return scope.BridgeToolsRoot;
    }

    private static DependencyScope TryGetScope(Assembly requestingAssembly)
    {
        if (requestingAssembly == null || requestingAssembly.IsDynamic)
            return null;

        try
        {
            var location = requestingAssembly.Location;
            if (string.IsNullOrWhiteSpace(location))
                return null;

            lock (ResolverSync)
            {
                return ScopesByAssemblyPath.TryGetValue(Path.GetFullPath(location), out var scope)
                    ? scope
                    : null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static Assembly LoadScopedAssembly(string path, DependencyScope scope)
    {
        var fullPath = Path.GetFullPath(path);
        if (string.Equals(Path.GetFileName(fullPath), CompanionFileDiscovery.SdkAssemblyFileName, StringComparison.OrdinalIgnoreCase))
            return typeof(ToolAttribute).Assembly;

        lock (ResolverSync)
        {
            if (LoadedAssembliesByPath.TryGetValue(fullPath, out var existing))
                return existing;

            ScopesByAssemblyPath[fullPath] = scope;
        }

        var assembly = Assembly.LoadFile(fullPath);
        lock (ResolverSync)
        {
            LoadedAssembliesByPath[fullPath] = assembly;
            ScopesByAssemblyPath[fullPath] = scope;
        }

        return assembly;
    }

    private static void RegisterScope(CompanionAssemblyCandidate candidate)
    {
        var scope = CreateScope(candidate);
        lock (ResolverSync)
        {
            ScopesByAssemblyPath[Path.GetFullPath(candidate.AssemblyPath)] = scope;
        }
    }

    private static DependencyScope CreateScope(CompanionAssemblyCandidate candidate)
    {
        return new DependencyScope
        {
            BridgeToolsRoot = candidate.BridgeToolsRoot,
            BundleDirectory = candidate.BundleDirectory,
            OwnerId = candidate.OwnerId
        };
    }

    private static void WarnAboutLocalSdk(CompanionAssemblyCandidate candidate)
    {
        foreach (var directory in new[] { candidate.BundleDirectory, candidate.BridgeToolsRoot }
            .Where(directory => string.IsNullOrWhiteSpace(directory) == false)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var sdkPath = Path.Combine(directory, CompanionFileDiscovery.SdkAssemblyFileName);
            if (File.Exists(sdkPath))
                Log.Warning($"[RimBridge] Ignoring companion-local SDK copy '{sdkPath}'. Companion tools must bind to the SDK shipped by RimBridgeServer.");
        }
    }

    private static string CreateProviderId(CompanionAssemblyCandidate candidate)
    {
        var owner = ReflectedCapabilityBinding.ToKebabCase(candidate.OwnerId);
        if (string.IsNullOrWhiteSpace(owner))
            owner = candidate.RootKind == CompanionRootKind.Global ? "global" : "mod";

        var assembly = ReflectedCapabilityBinding.ToKebabCase(Path.GetFileNameWithoutExtension(candidate.AssemblyPath));
        if (string.IsNullOrWhiteSpace(assembly))
            assembly = "companion";

        return $"extension.{candidate.RootKind.ToString().ToLowerInvariant()}/{owner}/{assembly}";
    }

    private static string CreateOwnerId(ModContentPack mod)
    {
        return mod?.PackageId ?? mod?.Name ?? mod?.FolderName ?? "unknown-mod";
    }

    private static int GetMetadataToken(MethodInfo method)
    {
        try
        {
            return method.MetadataToken;
        }
        catch
        {
            return 0;
        }
    }
}
