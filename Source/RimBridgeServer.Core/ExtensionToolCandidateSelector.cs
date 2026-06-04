using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RimBridgeServer.Core;

internal sealed class ExtensionToolDiscoveryCandidate
{
    public string CandidateId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string MethodIdentity { get; set; } = string.Empty;

    public string AssemblyIdentity { get; set; } = string.Empty;

    public string AssemblyName { get; set; } = string.Empty;

    public string AssemblyFullName { get; set; } = string.Empty;

    public string ModProviderId { get; set; } = string.Empty;

    public string ModSortKey { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public int MetadataToken { get; set; }
}

internal sealed class SelectedExtensionToolCandidate
{
    public string CandidateId { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;
}

internal static class ExtensionToolCandidateSelector
{
    public static IReadOnlyList<SelectedExtensionToolCandidate> Select(
        IEnumerable<ExtensionToolDiscoveryCandidate> candidates,
        IEnumerable<string> reservedAliases)
    {
        var candidateList = candidates?
            .Where(candidate => candidate != null)
            .ToList()
            ?? [];
        var selected = new List<SelectedExtensionToolCandidate>();
        var selectedAliases = new HashSet<string>(
            reservedAliases?.Where(alias => string.IsNullOrWhiteSpace(alias) == false) ?? [],
            StringComparer.OrdinalIgnoreCase);
        var selectedMethodIdentities = new HashSet<string>(StringComparer.Ordinal);
        var sharedAssemblyIdentities = FindSharedAssemblyIdentities(candidateList);
        var providerIdsBySharedAssembly = CreateSharedAssemblyProviderIds(candidateList, sharedAssemblyIdentities);

        foreach (var candidate in OrderCandidates(candidateList))
        {
            if (string.IsNullOrWhiteSpace(candidate.CandidateId)
                || string.IsNullOrWhiteSpace(candidate.ToolName)
                || string.IsNullOrWhiteSpace(candidate.MethodIdentity))
                continue;

            if (selectedAliases.Contains(candidate.ToolName))
                continue;

            if (!selectedMethodIdentities.Add(candidate.MethodIdentity))
                continue;

            selectedAliases.Add(candidate.ToolName);
            selected.Add(new SelectedExtensionToolCandidate
            {
                CandidateId = candidate.CandidateId,
                ProviderId = ResolveProviderId(candidate, sharedAssemblyIdentities, providerIdsBySharedAssembly)
            });
        }

        return selected;
    }

    private static IOrderedEnumerable<ExtensionToolDiscoveryCandidate> OrderCandidates(IEnumerable<ExtensionToolDiscoveryCandidate> candidates)
    {
        return candidates
            .OrderBy(candidate => candidate.ModSortKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.ModSortKey ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.AssemblyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.AssemblyName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.TypeName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.MethodName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.MetadataToken)
            .ThenBy(candidate => candidate.CandidateId ?? string.Empty, StringComparer.Ordinal);
    }

    private static HashSet<string> FindSharedAssemblyIdentities(IEnumerable<ExtensionToolDiscoveryCandidate> candidates)
    {
        var identities = candidates
            .Where(candidate => string.IsNullOrWhiteSpace(candidate.AssemblyIdentity) == false)
            .GroupBy(candidate => candidate.AssemblyIdentity, StringComparer.Ordinal)
            .Where(group => group
                .Select(candidate => candidate.ModProviderId ?? string.Empty)
                .Where(providerId => string.IsNullOrWhiteSpace(providerId) == false)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            .Select(group => group.Key);

        return new HashSet<string>(identities, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> CreateSharedAssemblyProviderIds(
        IReadOnlyCollection<ExtensionToolDiscoveryCandidate> candidates,
        ISet<string> sharedAssemblyIdentities)
    {
        var providerIdsByAssembly = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedProviderIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var assemblies = candidates
            .Where(candidate => sharedAssemblyIdentities.Contains(candidate.AssemblyIdentity))
            .GroupBy(candidate => candidate.AssemblyIdentity, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(candidate => candidate.AssemblyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.AssemblyFullName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.CandidateId ?? string.Empty, StringComparer.Ordinal)
                .First())
            .OrderBy(candidate => candidate.AssemblyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.AssemblyFullName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.AssemblyIdentity ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        foreach (var assembly in assemblies)
        {
            var providerId = CreateSharedAssemblyProviderId(assembly.AssemblyName);
            if (usedProviderIds.TryGetValue(providerId, out var existingAssemblyIdentity)
                && string.Equals(existingAssemblyIdentity, assembly.AssemblyIdentity, StringComparison.Ordinal) == false)
            {
                providerId = providerId + "-" + ComputeShortSha256(assembly.AssemblyFullName);
            }

            providerIdsByAssembly[assembly.AssemblyIdentity] = providerId;
            usedProviderIds[providerId] = assembly.AssemblyIdentity;
        }

        return providerIdsByAssembly;
    }

    private static string ResolveProviderId(
        ExtensionToolDiscoveryCandidate candidate,
        ISet<string> sharedAssemblyIdentities,
        IReadOnlyDictionary<string, string> providerIdsBySharedAssembly)
    {
        if (string.IsNullOrWhiteSpace(candidate.AssemblyIdentity) == false
            && sharedAssemblyIdentities.Contains(candidate.AssemblyIdentity)
            && providerIdsBySharedAssembly.TryGetValue(candidate.AssemblyIdentity, out var providerId))
        {
            return providerId;
        }

        return candidate.ModProviderId;
    }

    private static string CreateSharedAssemblyProviderId(string assemblyName)
    {
        return "extension.assembly/" + ToKebabSegment(assemblyName) + "/annotations";
    }

    private static string ToKebabSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "assembly";

        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = true;
        var previousWasLowerOrDigit = false;

        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (char.IsUpper(ch) && !previousWasSeparator && previousWasLowerOrDigit)
                    builder.Append('-');

                builder.Append(char.ToLowerInvariant(ch));
                previousWasSeparator = false;
                previousWasLowerOrDigit = char.IsLower(ch) || char.IsDigit(ch);
                continue;
            }

            if (builder.Length > 0 && !previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }

            previousWasLowerOrDigit = false;
        }

        while (builder.Length > 0 && builder[builder.Length - 1] == '-')
            builder.Length--;

        return builder.Length == 0 ? "assembly" : builder.ToString();
    }

    private static string ComputeShortSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return string.Concat(hash.Take(4).Select(static entry => entry.ToString("x2")));
    }
}
