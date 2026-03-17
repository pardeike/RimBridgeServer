using System.Diagnostics;
using System.Text.Json;

namespace RimBridgeServer.LiveSmoke;

internal static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliOptions options;

        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            CliOptions.WriteUsage(Console.Error);
            return 1;
        }

        if (options.ShowHelp)
        {
            CliOptions.WriteUsage(Console.Out);
            return 0;
        }

        if (options.ListScenarios)
        {
            CliOptions.WriteScenarios(Console.Out);
            return 0;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(options.TotalTimeoutMs));
        SmokeRunReport report;
        try
        {
            report = await SmokeHarness.RunAsync(options, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"Timed out after {options.TotalTimeoutMs}ms.");
            return 1;
        }

        SmokeHarness.PrintSummary(report, options.Verbose, Console.Out);
        return report.Success ? 0 : 1;
    }
}

internal static class SmokeHarness
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true
    };
    private const int ConnectRetryDelayMs = 1000;

    public static async Task<SmokeRunReport> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.ReportDirectory);
        if (options.HumanVerify)
            Directory.CreateDirectory(options.HumanVerifyDirectory);
        var scenario = SmokeScenarioCatalog.GetOrThrow(options.Scenario);

        var report = new SmokeRunReport
        {
            Scenario = scenario.Name,
            ScenarioDescription = scenario.Description,
            GameId = options.GameId,
            GabsBinaryPath = options.GabsBinaryPath,
            StartedAtUtc = DateTimeOffset.UtcNow,
            PlayerLogPath = options.PlayerLogPath ?? string.Empty
        };

        var stopwatch = Stopwatch.StartNew();

        await using var client = await McpStdioClient.StartAsync(options.GabsBinaryPath, options.GabsConfigDir, cancellationToken);
        var context = new SmokeScenarioContext(options, client, report);

        try
        {
            var status = await context.CallServerToolAsync("check_game_status", "games.status", new { gameId = options.GameId }, cancellationToken);
            context.EnsureSucceeded(status, "Checking game status");

            if (IsStoppedStatus(status.Text))
            {
                var start = await context.CallServerToolAsync("start_game", "games.start", new { gameId = options.GameId }, cancellationToken);
                if (start.IsError && start.Text.Contains("already running", StringComparison.OrdinalIgnoreCase))
                {
                    start.IsError = false;
                    start.Message = start.Text;
                    start.Success = true;
                }

                context.EnsureSucceeded(start, "Starting game");
                context.StartedGameByHarness = start.Text.Contains("started successfully", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                context.Note("Reused an already running RimWorld instance.");
            }

            await ConnectGameAsync(context, options.GameId, options.PlayerLogPath, cancellationToken);

            await scenario.RunAsync(context, cancellationToken);
            report.Success = true;
        }
        catch (Exception ex)
        {
            report.Success = false;
            report.FailureMessage = ex.Message;
            report.Exception = ex.ToString();
        }
        finally
        {
            if (options.StopAfter && context.StartedGameByHarness)
            {
                try
                {
                    report.Notes.Add("Stopping RimWorld after verification because --stop-after was requested and this harness started the game.");
                    var stop = await context.CallServerToolAsync("stop_game", "games.stop", new { gameId = options.GameId }, cancellationToken);
                    if (stop.IsError)
                        report.Notes.Add($"Cleanup warning: {stop.Text}");
                }
                catch (Exception ex)
                {
                    report.Notes.Add($"Cleanup warning: {ex.Message}");
                }
            }
            else if (options.StopAfter)
            {
                report.Notes.Add("Requested stop-after cleanup was skipped because this harness did not start the game.");
            }

            report.DurationMs = stopwatch.ElapsedMilliseconds;
            report.GabsStderrTail = context.GetGabsStderrTail().ToList();
            report.ReportPath = await SaveReportAsync(report, options.ReportDirectory, cancellationToken);
        }

        return report;
    }

    public static void PrintSummary(SmokeRunReport report, bool verbose, TextWriter writer)
    {
        writer.WriteLine($"Scenario: {report.Scenario}");
        writer.WriteLine($"Result: {(report.Success ? "PASS" : "FAIL")} ({report.DurationMs}ms)");
        writer.WriteLine($"Game: {report.GameId}");
        writer.WriteLine($"Report: {report.ReportPath}");
        if (string.IsNullOrWhiteSpace(report.PlayerLogPath) == false)
            writer.WriteLine($"Player.log: {report.PlayerLogPath}");

        foreach (var step in report.Steps)
        {
            var status = step.Success ? "ok" : "failed";
            writer.WriteLine($"- {step.Name}: {status} ({step.DurationMs}ms) {step.Message}".TrimEnd());
        }

        if (report.ColonistCount.HasValue)
            writer.WriteLine($"Colonists on current map: {report.ColonistCount.Value}");

        if (report.HumanVerificationArtifacts.Count > 0)
        {
            writer.WriteLine("Human verification artifacts:");
            foreach (var artifact in report.HumanVerificationArtifacts)
                writer.WriteLine($"  {artifact.Label}: {artifact.ImagePath}");
        }

        foreach (var pair in report.SummaryValues.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            writer.WriteLine($"{pair.Key}: {pair.Value}");

        if (report.InitialBridgeState is not null)
            writer.WriteLine($"Initial bridge state: {SummarizeState(report.InitialBridgeState)}");

        if (report.FinalBridgeState is not null)
            writer.WriteLine($"Final bridge state: {SummarizeState(report.FinalBridgeState)}");

        if (report.OperationEvents.Count > 0)
            writer.WriteLine($"Captured operation events: {report.OperationEvents.Count}");

        if (report.LogEntries.Count > 0)
        {
            var warningCount = CountLogs(report.LogEntries, "warning");
            var errorCount = CountLogs(report.LogEntries, "error") + CountLogs(report.LogEntries, "fatal");
            writer.WriteLine($"Captured log entries: {report.LogEntries.Count} (warning: {warningCount}, error/fatal: {errorCount})");
        }

        if (report.StartupDiagnostics.Count > 0)
        {
            writer.WriteLine($"Startup diagnostics: {report.StartupDiagnostics.Count} failure marker(s)");
            foreach (var diagnostic in report.StartupDiagnostics.Take(verbose ? int.MaxValue : 3))
                writer.WriteLine($"  [{diagnostic.Marker}] {diagnostic.Summary}");
        }

        var notableLogs = report.LogEntries
            .Where(IsNotableLog)
            .Take(verbose ? int.MaxValue : 5)
            .ToList();

        if (notableLogs.Count > 0)
        {
            writer.WriteLine("Notable logs:");
            foreach (var log in notableLogs)
                writer.WriteLine($"  [{JsonNodeHelpers.ReadString(log, "Level")}] {JsonNodeHelpers.ReadString(log, "Message")}");
        }

        if (verbose && report.OperationEvents.Count > 0)
        {
            writer.WriteLine("Operation events:");
            foreach (var entry in report.OperationEvents)
            {
                writer.WriteLine($"  [{JsonNodeHelpers.ReadString(entry, "EventType")}] {JsonNodeHelpers.ReadString(entry, "OperationId")} ({JsonNodeHelpers.ReadString(entry, "CapabilityId")})");
            }
        }

        if (report.Notes.Count > 0)
        {
            writer.WriteLine("Notes:");
            foreach (var note in report.Notes)
                writer.WriteLine($"  {note}");
        }

        if (!report.Success && report.GabsStderrTail.Count > 0)
        {
            writer.WriteLine("GABS stderr tail:");
            foreach (var line in report.GabsStderrTail)
                writer.WriteLine($"  {line}");
        }
    }

    private static bool IsStoppedStatus(string text)
    {
        return text.Contains(": stopped", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ConnectGameAsync(SmokeScenarioContext context, string gameId, string? playerLogPath, CancellationToken cancellationToken)
    {
        var connectDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30);
        var attempt = 0;

        while (true)
        {
            CaptureStartupDiagnostics(context, playerLogPath);
            attempt++;
            var stepName = attempt == 1 ? "connect_game" : $"connect_game_retry_{attempt}";
            var connect = await context.CallServerToolAsync(stepName, "games.connect", new { gameId }, cancellationToken);
            if (connect.Success)
            {
                CaptureStartupDiagnostics(context, playerLogPath);
                return;
            }

            if (!ShouldRetryConnect(connect.Text, connect.Message) || DateTimeOffset.UtcNow >= connectDeadlineUtc)
            {
                CaptureStartupDiagnostics(context, playerLogPath);
                context.EnsureSucceeded(connect, "Connecting to GABP");
                return;
            }

            context.Note($"Waiting for the RimWorld GABP server to finish opening before connect retry {attempt + 1}.");
            await Task.Delay(ConnectRetryDelayMs, cancellationToken);
        }
    }

    private static bool ShouldRetryConnect(string text, string message)
    {
        var combined = string.Join(" ", new[] { text, message }.Where(value => string.IsNullOrWhiteSpace(value) == false));
        return combined.Contains("Failed to connect to GABP server", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("timed out", StringComparison.OrdinalIgnoreCase);
    }

    private static void CaptureStartupDiagnostics(SmokeScenarioContext context, string? playerLogPath)
    {
        var startupCheck = StartupLogDiagnostics.Inspect(playerLogPath);
        if (startupCheck.FailureDetected == false)
            return;

        context.Report.StartupFailureDetected = true;
        context.Report.PlayerLogPath = startupCheck.PlayerLogPath;
        context.Report.StartupDiagnostics = startupCheck.Diagnostics
            .Select(diagnostic => new StartupDiagnosticReport
            {
                Marker = diagnostic.Marker,
                Summary = diagnostic.Summary,
                Excerpt = diagnostic.Excerpt
            })
            .ToList();

        var fatalDiagnostic = startupCheck.Diagnostics.FirstOrDefault(diagnostic => IsFatalStartupMarker(diagnostic.Marker));
        if (fatalDiagnostic != null)
            throw new InvalidOperationException($"RimWorld startup reported a bridge initialization failure before the harness could connect. {fatalDiagnostic.Summary}");
    }

    private static bool IsFatalStartupMarker(string marker)
    {
        return string.Equals(marker, "[RimBridge] STARTUP_ESSENTIAL_PATCH_FAILURE:", StringComparison.Ordinal)
            || string.Equals(marker, "[RimBridge] STARTUP_INIT_FAILURE:", StringComparison.Ordinal)
            || string.Equals(marker, "[RimBridge] Failed to initialize server:", StringComparison.Ordinal);
    }

    private static async Task<string> SaveReportAsync(SmokeRunReport report, string reportDirectory, CancellationToken cancellationToken)
    {
        var fileName = $"{report.StartedAtUtc:yyyyMMdd_HHmmss}_{report.Scenario}.json";
        var reportPath = Path.Combine(reportDirectory, fileName);
        report.ReportPath = reportPath;
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, ReportJsonOptions), cancellationToken);
        return reportPath;
    }

    private static int CountLogs(IReadOnlyList<System.Text.Json.Nodes.JsonNode?> logs, string level)
    {
        return logs.Count(entry => string.Equals(JsonNodeHelpers.ReadString(entry, "Level"), level, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNotableLog(System.Text.Json.Nodes.JsonNode? logEntry)
    {
        var level = JsonNodeHelpers.ReadString(logEntry, "Level");
        return level.Equals("warning", StringComparison.OrdinalIgnoreCase)
            || level.Equals("error", StringComparison.OrdinalIgnoreCase)
            || level.Equals("fatal", StringComparison.OrdinalIgnoreCase);
    }

    private static string SummarizeState(System.Text.Json.Nodes.JsonNode state)
    {
        return $"programState={JsonNodeHelpers.ReadString(state, "programState")}, hasCurrentGame={JsonNodeHelpers.ReadBoolean(state, "hasCurrentGame")}, longEventPending={JsonNodeHelpers.ReadBoolean(state, "longEventPending")}, paused={JsonNodeHelpers.ReadBoolean(state, "paused")}, screenFading={JsonNodeHelpers.ReadBoolean(state, "screenFading")}, automationReady={JsonNodeHelpers.ReadBoolean(state, "automationReady")}";
    }
}
