using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimBridgeServer;

internal sealed class LoadedLanguageSnapshot
{
    public int Index { get; set; }

    public string RecommendedQuery { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string FolderName { get; set; } = string.Empty;

    public string LegacyFolderName { get; set; }

    public string DisplayName { get; set; }

    public string FriendlyNameEnglish { get; set; }

    public string FriendlyNameNative { get; set; }

    public bool IsActive { get; set; }

    public bool HasErrors { get; set; }

    public int LoadErrorCount { get; set; }
}

internal static class RimWorldLanguages
{
    public static object ListLanguagesResponse()
    {
        var languages = GetLoadedLanguages();
        var active = LanguageDatabase.activeLanguage;
        var activeSnapshot = active == null ? null : DescribeLanguage(active, FindLanguageIndex(languages, active));

        return new
        {
            success = true,
            count = languages.Count,
            recommendedQueryField = "recommendedQuery",
            usageHint = "Prefer recommendedQuery when calling rimworld/switch_language. It uses the most stable ASCII-safe query when one is available.",
            activeLanguageId = active == null ? null : GetLanguageId(active),
            activeLanguageRecommendedQuery = activeSnapshot?.RecommendedQuery,
            activeLanguage = activeSnapshot == null ? null : ToToolResponse(activeSnapshot),
            languages = languages.Select((language, index) => ToToolResponse(DescribeLanguage(language, index))).ToList()
        };
    }

    public static object SwitchLanguageResponse(string language)
    {
        var beforeUi = RimWorldInput.GetUiState();
        var beforeActive = LanguageDatabase.activeLanguage;
        var beforeState = RimWorldState.ToolStateSnapshot();
        var normalizedQuery = language?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new
            {
                success = false,
                command = "switch_language",
                message = "A language id or exact language name is required.",
                before = beforeState,
                beforeUi = RimWorldInput.DescribeUiState(beforeUi)
            };
        }

        if (LongEventHandler.AnyEventNowOrWaiting)
        {
            return new
            {
                success = false,
                command = "switch_language",
                message = "RimWorld is busy with another long event. Wait for it to finish before switching language.",
                before = beforeState,
                beforeUi = RimWorldInput.DescribeUiState(beforeUi)
            };
        }

        var languages = GetLoadedLanguages();
        if (!TryResolveLanguage(languages, normalizedQuery, out var target, out var resolutionError))
        {
            return new
            {
                success = false,
                command = "switch_language",
                message = resolutionError,
                before = beforeState,
                beforeUi = RimWorldInput.DescribeUiState(beforeUi)
            };
        }

        if (LanguagesEqual(beforeActive, target) || string.Equals(Prefs.LangFolderName, target.folderName, StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                success = true,
                command = "switch_language",
                status = "noop",
                changed = false,
                message = $"RimWorld is already configured to use language '{target.DisplayName}'.",
                requestedLanguage = ToToolResponse(DescribeLanguage(target, FindLanguageIndex(languages, target))),
                activeLanguage = beforeActive == null ? null : ToToolResponse(DescribeLanguage(beforeActive, FindLanguageIndex(languages, beforeActive))),
                before = beforeState,
                beforeUi = RimWorldInput.DescribeUiState(beforeUi)
            };
        }

        LanguageDatabase.SelectLanguage(target);
        Prefs.Save();

        var afterState = RimWorldState.ToolStateSnapshot();
        var afterUi = RimWorldInput.GetUiState();

        return new
        {
            success = true,
            command = "switch_language",
            status = "queued",
            changed = true,
            requestedLanguage = ToToolResponse(DescribeLanguage(target, FindLanguageIndex(languages, target))),
            previousLanguage = beforeActive == null ? null : ToToolResponse(DescribeLanguage(beforeActive, FindLanguageIndex(languages, beforeActive))),
            activeLanguage = LanguageDatabase.activeLanguage == null ? null : ToToolResponse(DescribeLanguage(LanguageDatabase.activeLanguage, FindLanguageIndex(languages, LanguageDatabase.activeLanguage))),
            persistedFolderName = Prefs.LangFolderName,
            longEventQueued = LongEventHandler.AnyEventNowOrWaiting,
            message = $"Queued RimWorld language switch to '{target.DisplayName}'. Call rimbridge/wait_for_long_event_idle before assuming translated content is ready.",
            before = beforeState,
            after = afterState,
            beforeUi = RimWorldInput.DescribeUiState(beforeUi),
            afterUi = RimWorldInput.DescribeUiState(afterUi)
        };
    }

    private static List<LoadedLanguage> GetLoadedLanguages()
    {
        return (LanguageDatabase.AllLoadedLanguages as IEnumerable)?
            .OfType<LoadedLanguage>()
            .Where(language => language != null)
            .ToList()
            ?? [];
    }

    private static int FindLanguageIndex(IReadOnlyList<LoadedLanguage> languages, LoadedLanguage language)
    {
        var id = GetLanguageId(language);
        for (var i = 0; i < languages.Count; i++)
        {
            if (string.Equals(GetLanguageId(languages[i]), id, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static LoadedLanguageSnapshot DescribeLanguage(LoadedLanguage language, int index)
    {
        return new LoadedLanguageSnapshot
        {
            Index = index,
            RecommendedQuery = GetRecommendedQuery(language),
            Id = GetLanguageId(language),
            FolderName = language.folderName ?? string.Empty,
            LegacyFolderName = Normalize(language.LegacyFolderName),
            DisplayName = Normalize(language.DisplayName),
            FriendlyNameEnglish = Normalize(language.FriendlyNameEnglish),
            FriendlyNameNative = Normalize(language.FriendlyNameNative),
            IsActive = LanguagesEqual(language, LanguageDatabase.activeLanguage),
            HasErrors = language.anyError,
            LoadErrorCount = CountItems(language.loadErrors)
        };
    }

    private static bool TryResolveLanguage(
        IReadOnlyList<LoadedLanguage> languages,
        string query,
        out LoadedLanguage language,
        out string error)
    {
        language = null;
        error = string.Empty;

        language = languages.FirstOrDefault(candidate => string.Equals(GetLanguageId(candidate), query, StringComparison.Ordinal));
        if (language != null)
            return true;

        var matches = languages
            .Where(candidate => MatchesLanguage(candidate, query))
            .Distinct()
            .ToList();

        if (matches.Count == 1)
        {
            language = matches[0];
            return true;
        }

        if (matches.Count > 1)
        {
            error = $"Language query '{query}' matched multiple installed languages: {string.Join(", ", matches.Select(candidate => GetLanguageId(candidate)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}. Use the recommendedQuery value from rimworld/list_languages.";
            return false;
        }

        error = $"Could not find an installed RimWorld language matching '{query}'.";
        return false;
    }

    private static bool MatchesLanguage(LoadedLanguage language, string query)
    {
        return string.Equals(language.folderName, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language.LegacyFolderName, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language.DisplayName, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language.FriendlyNameEnglish, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language.FriendlyNameNative, query, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountItems(ICollection collection)
    {
        return collection?.Count ?? 0;
    }

    private static bool LanguagesEqual(LoadedLanguage left, LoadedLanguage right)
    {
        return string.Equals(left?.folderName, right?.folderName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLanguageId(LoadedLanguage language)
    {
        return language?.folderName?.Trim() ?? string.Empty;
    }

    private static string GetRecommendedQuery(LoadedLanguage language)
    {
        var legacyFolderName = Normalize(language?.LegacyFolderName);
        if (IsAsciiSafe(legacyFolderName))
            return legacyFolderName;

        var friendlyNameEnglish = Normalize(language?.FriendlyNameEnglish);
        if (IsAsciiSafe(friendlyNameEnglish))
            return friendlyNameEnglish;

        return GetLanguageId(language);
    }

    private static bool IsAsciiSafe(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.All(ch => ch >= 32 && ch <= 126);
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static object ToToolResponse(LoadedLanguageSnapshot language)
    {
        return new
        {
            index = language.Index,
            recommendedQuery = language.RecommendedQuery,
            id = language.Id,
            folderName = language.FolderName,
            legacyFolderName = language.LegacyFolderName,
            displayName = language.DisplayName,
            friendlyNameEnglish = language.FriendlyNameEnglish,
            friendlyNameNative = language.FriendlyNameNative,
            isActive = language.IsActive,
            hasErrors = language.HasErrors,
            loadErrorCount = language.LoadErrorCount
        };
    }
}
