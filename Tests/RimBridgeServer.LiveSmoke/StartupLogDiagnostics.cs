using System.Text;

namespace RimBridgeServer.LiveSmoke;

internal sealed class StartupLogDiagnostic
{
    public required string Marker { get; init; }

    public required string Summary { get; init; }

    public required string Excerpt { get; init; }
}

internal sealed class StartupLogCheckResult
{
    public required string PlayerLogPath { get; init; }

    public required IReadOnlyList<StartupLogDiagnostic> Diagnostics { get; init; }

    public bool FailureDetected => Diagnostics.Count > 0;
}

internal static class StartupLogDiagnostics
{
    private static readonly string[] FailureMarkers =
    [
        "[RimBridge] STARTUP_ESSENTIAL_PATCH_FAILURE:",
        "[RimBridge] STARTUP_OPTIONAL_PATCH_FAILURE:",
        "[RimBridge] STARTUP_INIT_FAILURE:",
        "[RimBridge] Failed to initialize server:"
    ];

    public static StartupLogCheckResult Inspect(string? playerLogPath, int tailLineCount = 400, int excerptLineCount = 16)
    {
        if (string.IsNullOrWhiteSpace(playerLogPath) || File.Exists(playerLogPath) == false)
        {
            return new StartupLogCheckResult
            {
                PlayerLogPath = playerLogPath ?? string.Empty,
                Diagnostics = []
            };
        }

        var lines = File.ReadLines(playerLogPath).TakeLast(Math.Max(tailLineCount, excerptLineCount)).ToArray();
        var diagnostics = new List<StartupLogDiagnostic>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var marker = FailureMarkers.FirstOrDefault(candidate => line.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (marker == null)
                continue;

            var excerpt = BuildExcerpt(lines, index, excerptLineCount);
            diagnostics.Add(new StartupLogDiagnostic
            {
                Marker = marker,
                Summary = Truncate(line.Trim(), 240),
                Excerpt = excerpt
            });
        }

        return new StartupLogCheckResult
        {
            PlayerLogPath = playerLogPath,
            Diagnostics = diagnostics
        };
    }

    private static string BuildExcerpt(IReadOnlyList<string> lines, int startIndex, int excerptLineCount)
    {
        var builder = new StringBuilder();
        var endIndex = Math.Min(lines.Count, startIndex + Math.Max(1, excerptLineCount));
        for (var index = startIndex; index < endIndex; index++)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append(lines[index]);
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            return value;

        return value[..Math.Max(0, maxLength - 3)] + "...";
    }
}
