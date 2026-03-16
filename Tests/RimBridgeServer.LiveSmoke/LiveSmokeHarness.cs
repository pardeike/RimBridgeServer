using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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

internal sealed class CliOptions
{
    public required string Scenario { get; init; }

    public required string GameId { get; init; }

    public required string GabsBinaryPath { get; init; }

    public string? GabsConfigDir { get; init; }

    public required string ReportDirectory { get; init; }

    public bool StopAfter { get; init; }

    public bool Verbose { get; init; }

    public bool ShowHelp { get; init; }

    public bool ListScenarios { get; init; }

    public int TotalTimeoutMs { get; init; } = 300000;

    public int WaitTimeoutMs { get; init; } = 60000;

    public int GameToolTimeoutSeconds { get; init; } = 90;

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        string? scenario = null;
        var gameId = "rimworld";
        string? gabsBinaryPath = null;
        string? gabsConfigDir = Environment.GetEnvironmentVariable("GABS_CONFIG_DIR");
        var reportDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "live-smoke"));
        var stopAfter = false;
        var verbose = false;
        var showHelp = false;
        var listScenarios = false;
        var totalTimeoutMs = 300000;
        var waitTimeoutMs = 60000;
        var gameToolTimeoutSeconds = 90;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--list-scenarios":
                    listScenarios = true;
                    break;
                case "--stop-after":
                    stopAfter = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--scenario":
                    scenario = ReadValue(args, ref index, arg);
                    break;
                case "--game-id":
                    gameId = ReadValue(args, ref index, arg);
                    break;
                case "--gabs-bin":
                    gabsBinaryPath = ReadValue(args, ref index, arg);
                    break;
                case "--config-dir":
                    gabsConfigDir = ReadValue(args, ref index, arg);
                    break;
                case "--report-dir":
                    reportDirectory = Path.GetFullPath(ReadValue(args, ref index, arg));
                    break;
                case "--total-timeout-ms":
                    totalTimeoutMs = ParsePositiveInt(ReadValue(args, ref index, arg), arg);
                    break;
                case "--wait-timeout-ms":
                    waitTimeoutMs = ParsePositiveInt(ReadValue(args, ref index, arg), arg);
                    break;
                case "--game-tool-timeout-seconds":
                    gameToolTimeoutSeconds = ParsePositiveInt(ReadValue(args, ref index, arg), arg);
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option '{arg}'.");

                    scenario ??= arg;
                    break;
            }
        }

        if (showHelp || listScenarios)
        {
            return new CliOptions
            {
                Scenario = scenario ?? SmokeHarness.DefaultScenarioName,
                GameId = gameId,
                GabsBinaryPath = ResolveGabsBinaryPath(gabsBinaryPath),
                GabsConfigDir = gabsConfigDir,
                ReportDirectory = reportDirectory,
                StopAfter = stopAfter,
                Verbose = verbose,
                ShowHelp = showHelp,
                ListScenarios = listScenarios,
                TotalTimeoutMs = totalTimeoutMs,
                WaitTimeoutMs = waitTimeoutMs,
                GameToolTimeoutSeconds = gameToolTimeoutSeconds
            };
        }

        if (string.IsNullOrWhiteSpace(scenario))
            throw new ArgumentException("A scenario is required. Use --scenario <name> or pass the scenario name as the first positional argument.");

        return new CliOptions
        {
            Scenario = scenario,
            GameId = gameId,
            GabsBinaryPath = ResolveGabsBinaryPath(gabsBinaryPath),
            GabsConfigDir = gabsConfigDir,
            ReportDirectory = reportDirectory,
            StopAfter = stopAfter,
            Verbose = verbose,
            ShowHelp = false,
            ListScenarios = false,
            TotalTimeoutMs = totalTimeoutMs,
            WaitTimeoutMs = waitTimeoutMs,
            GameToolTimeoutSeconds = gameToolTimeoutSeconds
        };
    }

    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  scripts/live-smoke.sh --scenario debug-game-load [options]");
        writer.WriteLine("  dotnet run --project Tests/RimBridgeServer.LiveSmoke -- --scenario debug-game-load [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --scenario <name>                 Scenario to run");
        writer.WriteLine("  --list-scenarios                  Show available scenario names");
        writer.WriteLine("  --game-id <id>                    GABS game id to target (default: rimworld)");
        writer.WriteLine("  --gabs-bin <path>                 Path to the GABS executable");
        writer.WriteLine("  --config-dir <path>               Optional GABS config directory override");
        writer.WriteLine("  --report-dir <path>               Directory for JSON run reports (default: artifacts/live-smoke)");
        writer.WriteLine("  --wait-timeout-ms <ms>            RimBridge wait tool timeout (default: 60000)");
        writer.WriteLine("  --game-tool-timeout-seconds <s>   Outer GABS timeout for game tools (default: 90)");
        writer.WriteLine("  --total-timeout-ms <ms>           End-to-end harness timeout (default: 300000)");
        writer.WriteLine("  --stop-after                      Stop the game after the run, but only if this harness started it");
        writer.WriteLine("  --verbose                         Print full warning/error logs and captured events");
        writer.WriteLine("  --help                            Show this help text");
    }

    public static void WriteScenarios(TextWriter writer)
    {
        writer.WriteLine("Available scenarios:");
        foreach (var scenarioName in SmokeHarness.ScenarioNames)
            writer.WriteLine($"  {scenarioName}");
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string optionName)
    {
        if (index + 1 >= args.Count)
            throw new ArgumentException($"Missing value for '{optionName}'.");

        index++;
        return args[index];
    }

    private static int ParsePositiveInt(string rawValue, string optionName)
    {
        if (!int.TryParse(rawValue, out var value) || value <= 0)
            throw new ArgumentException($"'{optionName}' requires a positive integer value.");

        return value;
    }

    private static string ResolveGabsBinaryPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;

        var envPath = Environment.GetEnvironmentVariable("GABS_BIN");
        if (!string.IsNullOrWhiteSpace(envPath))
            return envPath;

        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "GABS", "gabs")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "GABS", "gabs.exe"))
        };

        var existing = candidates.FirstOrDefault(File.Exists);
        return existing ?? "gabs";
    }
}

internal static class SmokeHarness
{
    public const string DefaultScenarioName = "debug-game-load";

    public static readonly IReadOnlyList<string> ScenarioNames = [DefaultScenarioName];

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<SmokeRunReport> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.ReportDirectory);

        var report = new SmokeRunReport
        {
            Scenario = options.Scenario,
            GameId = options.GameId,
            GabsBinaryPath = options.GabsBinaryPath,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();
        var startedGame = false;
        var attemptedStop = false;

        await using var client = await McpStdioClient.StartAsync(options.GabsBinaryPath, options.GabsConfigDir, cancellationToken);

        try
        {
            var status = await RecordServerToolAsync(report, client, "check_game_status", "games.status", new { gameId = options.GameId }, cancellationToken);
            EnsureToolSucceeded(status, "Checking game status");

            if (IsStoppedStatus(status.Text))
            {
                var start = await RecordServerToolAsync(report, client, "start_game", "games.start", new { gameId = options.GameId }, cancellationToken);
                if (start.IsError && start.Text.Contains("already running", StringComparison.OrdinalIgnoreCase))
                {
                    start.IsError = false;
                    start.Message = start.Text;
                }

                EnsureToolSucceeded(start, "Starting game");
                startedGame = start.Text.Contains("started successfully", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                report.Notes.Add("Reused an already running RimWorld instance.");
            }

            var connect = await RecordServerToolAsync(report, client, "connect_game", "games.connect", new { gameId = options.GameId }, cancellationToken);
            EnsureToolSucceeded(connect, "Connecting to GABP");

            switch (options.Scenario)
            {
                case DefaultScenarioName:
                    await RunDebugGameLoadScenarioAsync(report, client, options, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown scenario '{options.Scenario}'.");
            }

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
            if (options.StopAfter && startedGame)
            {
                attemptedStop = true;
                try
                {
                    var stop = await RecordServerToolAsync(report, client, "stop_game", "games.stop", new { gameId = options.GameId }, cancellationToken);
                    if (stop.IsError)
                        report.Notes.Add($"Cleanup warning: {stop.Text}");
                }
                catch (Exception ex)
                {
                    report.Notes.Add($"Cleanup warning: {ex.Message}");
                }
            }

            report.DurationMs = stopwatch.ElapsedMilliseconds;
            report.GabsStderrTail = client.GetStderrTail().ToList();
            if (options.StopAfter && attemptedStop == false)
                report.Notes.Add("Requested stop-after cleanup was skipped because this harness did not start the game.");

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

        foreach (var step in report.Steps)
        {
            var status = step.Success ? "ok" : "failed";
            writer.WriteLine($"- {step.Name}: {status} ({step.DurationMs}ms) {step.Message}".TrimEnd());
        }

        if (report.ColonistCount.HasValue)
            writer.WriteLine($"Colonists on current map: {report.ColonistCount.Value}");

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

        var notableLogs = report.LogEntries
            .Where(entry => IsNotableLog(entry))
            .Take(verbose ? int.MaxValue : 5)
            .ToList();

        if (notableLogs.Count > 0)
        {
            writer.WriteLine("Notable logs:");
            foreach (var log in notableLogs)
                writer.WriteLine($"  [{ReadString(log, "Level")}] {ReadString(log, "Message")}");
        }

        if (verbose && report.OperationEvents.Count > 0)
        {
            writer.WriteLine("Operation events:");
            foreach (var entry in report.OperationEvents)
            {
                writer.WriteLine($"  [{ReadString(entry, "EventType")}] {ReadString(entry, "OperationId")} ({ReadString(entry, "CapabilityId")})");
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

    private static async Task RunDebugGameLoadScenarioAsync(SmokeRunReport report, McpStdioClient client, CliOptions options, CancellationToken cancellationToken)
    {
        var waitForIdle = await RecordGameToolAsync(report, client, "wait_for_long_event_idle", options, "rimbridge/wait_for_long_event_idle", new
        {
            timeoutMs = options.WaitTimeoutMs,
            pollIntervalMs = 100
        }, cancellationToken);
        EnsureToolSucceeded(waitForIdle, "Waiting for RimWorld to become idle");

        var baselineStatus = await RecordGameToolAsync(report, client, "snapshot_bridge_status", options, "rimbridge/get_bridge_status", new { }, cancellationToken);
        EnsureToolSucceeded(baselineStatus, "Reading initial bridge status");

        report.InitialBridgeState = CloneNode(GetPath(baselineStatus.StructuredContent, "state"));
        var baselineLogSequence = ReadInt64(baselineStatus.StructuredContent, "latestLogSequence");
        var baselineEventSequence = ReadInt64(baselineStatus.StructuredContent, "latestOperationEventSequence");

        if (baselineLogSequence is null || baselineEventSequence is null)
            throw new InvalidOperationException("Bridge status did not include the expected log and operation event cursors.");

        var startDebugGame = await RecordGameToolAsync(report, client, "start_debug_game", options, "rimworld/start_debug_game", new { }, cancellationToken);
        EnsureToolSucceeded(startDebugGame, "Starting RimWorld debug game");

        var operationId = ReadString(startDebugGame.StructuredContent, "operation", "OperationId");
        if (string.IsNullOrWhiteSpace(operationId))
            throw new InvalidOperationException("Start debug game did not return operation metadata.");

        var waitForOperation = await RecordGameToolAsync(report, client, "wait_for_operation", options, "rimbridge/wait_for_operation", new
        {
            operationId,
            timeoutMs = options.WaitTimeoutMs,
            pollIntervalMs = 50
        }, cancellationToken);
        EnsureToolSucceeded(waitForOperation, $"Waiting for operation '{operationId}'");

        var waitForGameLoaded = await RecordGameToolAsync(report, client, "wait_for_game_loaded", options, "rimbridge/wait_for_game_loaded", new
        {
            timeoutMs = options.WaitTimeoutMs,
            pollIntervalMs = 100
        }, cancellationToken);
        EnsureToolSucceeded(waitForGameLoaded, "Waiting for RimWorld to load a playable game");

        var colonists = await RecordGameToolAsync(report, client, "list_colonists", options, "rimworld/list_colonists", new
        {
            currentMapOnly = true
        }, cancellationToken);
        EnsureToolSucceeded(colonists, "Listing current-map colonists");

        report.ColonistCount = ReadInt32(colonists.StructuredContent, "count");
        if (report.ColonistCount.GetValueOrDefault() <= 0)
            throw new InvalidOperationException("The debug game loaded but did not expose any colonists on the current map.");

        var finalStatus = await RecordGameToolAsync(report, client, "final_bridge_status", options, "rimbridge/get_bridge_status", new { }, cancellationToken);
        EnsureToolSucceeded(finalStatus, "Reading final bridge status");
        report.FinalBridgeState = CloneNode(GetPath(finalStatus.StructuredContent, "state"));

        var eventWindow = await RecordGameToolAsync(report, client, "collect_operation_events", options, "rimbridge/list_operation_events", new
        {
            afterSequence = baselineEventSequence.Value,
            limit = 200
        }, cancellationToken);
        EnsureToolSucceeded(eventWindow, "Collecting operation events");
        report.OperationEvents = ReadArray(eventWindow.StructuredContent, "events");

        var logWindow = await RecordGameToolAsync(report, client, "collect_logs", options, "rimbridge/list_logs", new
        {
            afterSequence = baselineLogSequence.Value,
            minimumLevel = "info",
            limit = 200
        }, cancellationToken);
        EnsureToolSucceeded(logWindow, "Collecting logs");
        report.LogEntries = ReadArray(logWindow.StructuredContent, "logs");
    }

    private static async Task<ToolInvocationResult> RecordServerToolAsync(SmokeRunReport report, McpStdioClient client, string stepName, string toolName, object arguments, CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(toolName, arguments, cancellationToken);
        report.Steps.Add(new SmokeStepReport
        {
            Name = stepName,
            ToolName = toolName,
            Success = result.Success,
            DurationMs = result.DurationMs,
            Message = result.Message,
            OperationId = ReadString(result.StructuredContent, "operation", "OperationId"),
            Response = CloneNode(result.StructuredContent)
        });

        return result;
    }

    private static Task<ToolInvocationResult> RecordGameToolAsync(SmokeRunReport report, McpStdioClient client, string stepName, CliOptions options, string toolName, object arguments, CancellationToken cancellationToken)
    {
        return RecordServerToolAsync(
            report,
            client,
            stepName,
            "games.call_tool",
            new
            {
                gameId = options.GameId,
                tool = toolName,
                arguments,
                timeout = options.GameToolTimeoutSeconds
            },
            cancellationToken);
    }

    private static void EnsureToolSucceeded(ToolInvocationResult result, string actionDescription)
    {
        if (!result.Success)
            throw new InvalidOperationException($"{actionDescription} failed. {result.Message}".Trim());
    }

    private static bool IsStoppedStatus(string text)
    {
        return text.Contains(": stopped", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SaveReportAsync(SmokeRunReport report, string reportDirectory, CancellationToken cancellationToken)
    {
        var fileName = $"{report.StartedAtUtc:yyyyMMdd_HHmmss}_{report.Scenario}.json";
        var reportPath = Path.Combine(reportDirectory, fileName);
        report.ReportPath = reportPath;
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, ReportJsonOptions), cancellationToken);
        return reportPath;
    }

    private static int CountLogs(IReadOnlyList<JsonNode?> logs, string level)
    {
        return logs.Count(entry => string.Equals(ReadString(entry, "Level"), level, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNotableLog(JsonNode? logEntry)
    {
        var level = ReadString(logEntry, "Level");
        return level is not null
            && (level.Equals("warning", StringComparison.OrdinalIgnoreCase)
                || level.Equals("error", StringComparison.OrdinalIgnoreCase)
                || level.Equals("fatal", StringComparison.OrdinalIgnoreCase));
    }

    private static string SummarizeState(JsonNode state)
    {
        return $"programState={ReadString(state, "programState")}, hasCurrentGame={ReadBoolean(state, "hasCurrentGame")}, longEventPending={ReadBoolean(state, "longEventPending")}";
    }

    private static JsonNode? CloneNode(JsonNode? node)
    {
        return node?.DeepClone();
    }

    private static JsonNode? GetPath(JsonNode? node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                return null;
        }

        return current;
    }

    private static string ReadString(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is null)
            return string.Empty;

        if (valueNode is JsonValue value)
        {
            if (value.TryGetValue(out string? stringValue) && stringValue is not null)
                return stringValue;

            return value.ToJsonString().Trim('"');
        }

        return valueNode.ToJsonString();
    }

    private static bool? ReadBoolean(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is not JsonValue value)
            return null;

        if (value.TryGetValue(out bool boolValue))
            return boolValue;

        if (value.TryGetValue(out string? stringValue) && bool.TryParse(stringValue, out var parsed))
            return parsed;

        return null;
    }

    private static int? ReadInt32(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is not JsonValue value)
            return null;

        if (value.TryGetValue(out int intValue))
            return intValue;

        if (value.TryGetValue(out long longValue))
            return checked((int)longValue);

        if (value.TryGetValue(out string? stringValue) && int.TryParse(stringValue, out var parsed))
            return parsed;

        return null;
    }

    private static long? ReadInt64(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is not JsonValue value)
            return null;

        if (value.TryGetValue(out long longValue))
            return longValue;

        if (value.TryGetValue(out int intValue))
            return intValue;

        if (value.TryGetValue(out string? stringValue) && long.TryParse(stringValue, out var parsed))
            return parsed;

        return null;
    }

    private static List<JsonNode?> ReadArray(JsonNode? node, params string[] path)
    {
        return GetPath(node, path) is JsonArray array
            ? array.Select(CloneNode).ToList()
            : [];
    }
}

internal sealed class McpStdioClient : IAsyncDisposable
{
    private const string ProtocolVersion = "2024-11-05";
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Task _stderrPump;
    private readonly Queue<string> _stderrTail = new();
    private readonly object _stderrGate = new();
    private readonly SemaphoreSlim _rpcGate = new(1, 1);
    private int _nextId = 1;

    private McpStdioClient(Process process)
    {
        _process = process;
        _input = process.StandardInput.BaseStream;
        _output = process.StandardOutput.BaseStream;
        _stderrPump = PumpStandardErrorAsync(process.StandardError);
    }

    public static async Task<McpStdioClient> StartAsync(string gabsBinaryPath, string? configDir, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = gabsBinaryPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory
        };

        startInfo.ArgumentList.Add("server");
        startInfo.ArgumentList.Add("stdio");
        startInfo.ArgumentList.Add("--log-level");
        startInfo.ArgumentList.Add("warn");

        if (!string.IsNullOrWhiteSpace(configDir))
        {
            startInfo.ArgumentList.Add("--configDir");
            startInfo.ArgumentList.Add(configDir);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{gabsBinaryPath}'.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not start GABS using '{gabsBinaryPath}'. {ex.Message}", ex);
        }

        var client = new McpStdioClient(process);
        await client.InitializeAsync(cancellationToken);
        return client;
    }

    public async Task<ToolInvocationResult> CallToolAsync(string toolName, object arguments, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var resultNode = await SendRequestAsync("tools/call", new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = JsonSerializer.SerializeToNode(arguments)
        }, cancellationToken);

        var isError = ReadBoolean(resultNode, "isError") ?? false;
        var structuredContent = CloneNode(GetPath(resultNode, "structuredContent"));
        var text = ReadTextContent(resultNode);
        var message = ReadString(structuredContent, "message");
        if (string.IsNullOrWhiteSpace(message))
            message = string.IsNullOrWhiteSpace(text) ? toolName : text;

        if (!isError)
        {
            var successFlag = ReadBoolean(structuredContent, "success");
            if (successFlag.HasValue && successFlag.Value == false)
                isError = true;
        }

        return new ToolInvocationResult
        {
            ToolName = toolName,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Success = !isError,
            IsError = isError,
            Text = text,
            Message = message,
            StructuredContent = structuredContent
        };
    }

    public IReadOnlyList<string> GetStderrTail()
    {
        lock (_stderrGate)
        {
            return _stderrTail.ToList();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _input.FlushAsync();
        }
        catch
        {
        }

        try
        {
            _process.StandardInput.Close();
        }
        catch
        {
        }

        if (!_process.HasExited)
        {
            try
            {
                if (!_process.WaitForExit(1000))
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        try
        {
            await _stderrPump;
        }
        catch
        {
        }

        _process.Dispose();
        _rpcGate.Dispose();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "rimbridge-live-smoke",
                ["version"] = "0.1.0"
            }
        }, cancellationToken);

        await SendNotificationAsync("notifications/initialized", new JsonObject(), cancellationToken);
    }

    private async Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        await _rpcGate.WaitAsync(cancellationToken);
        try
        {
            var id = Interlocked.Increment(ref _nextId);
            await WriteMessageAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = @params
            }, cancellationToken);

            while (true)
            {
                var message = await ReadMessageAsync(cancellationToken)
                    ?? throw new InvalidOperationException("GABS closed the MCP stream before replying.");

                var responseId = ReadInt32(message, "id");
                if (responseId is null)
                    continue;

                if (responseId.Value != id)
                    continue;

                var errorNode = GetPath(message, "error");
                if (errorNode is not null)
                    throw new InvalidOperationException($"MCP request '{method}' failed. {ReadString(errorNode, "message")}");

                return GetPath(message, "result");
            }
        }
        finally
        {
            _rpcGate.Release();
        }
    }

    private async Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        await _rpcGate.WaitAsync(cancellationToken);
        try
        {
            await WriteMessageAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = @params
            }, cancellationToken);
        }
        finally
        {
            _rpcGate.Release();
        }
    }

    private async Task WriteMessageAsync(JsonNode message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        await _input.WriteAsync(header, cancellationToken);
        await _input.WriteAsync(payload, cancellationToken);
        await _input.FlushAsync(cancellationToken);
    }

    private async Task<JsonNode?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>(128);
        var singleByte = new byte[1];

        while (true)
        {
            var read = await _output.ReadAsync(singleByte.AsMemory(0, 1), cancellationToken);
            if (read == 0)
                return null;

            headerBytes.Add(singleByte[0]);
            if (headerBytes.Count >= 4
                && headerBytes[^4] == '\r'
                && headerBytes[^3] == '\n'
                && headerBytes[^2] == '\r'
                && headerBytes[^1] == '\n')
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var contentLength = ParseContentLength(headerText);
        var payload = new byte[contentLength];
        await ReadExactlyAsync(_output, payload, cancellationToken);
        return JsonNode.Parse(payload);
    }

    private async Task PumpStandardErrorAsync(StreamReader reader)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
                break;

            lock (_stderrGate)
            {
                _stderrTail.Enqueue(line);
                while (_stderrTail.Count > 40)
                    _stderrTail.Dequeue();
            }
        }
    }

    private static int ParseContentLength(string headerText)
    {
        foreach (var line in headerText.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "Content-Length:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var rawValue = line[prefix.Length..].Trim();
                if (int.TryParse(rawValue, out var contentLength) && contentLength >= 0)
                    return contentLength;
            }
        }

        throw new InvalidOperationException($"Missing Content-Length header in MCP response: {headerText}");
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading an MCP frame.");

            offset += read;
        }
    }

    private static JsonNode? CloneNode(JsonNode? node)
    {
        return node?.DeepClone();
    }

    private static JsonNode? GetPath(JsonNode? node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                return null;
        }

        return current;
    }

    private static bool? ReadBoolean(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is not JsonValue value)
            return null;

        if (value.TryGetValue(out bool boolValue))
            return boolValue;

        if (value.TryGetValue(out string? stringValue) && bool.TryParse(stringValue, out var parsed))
            return parsed;

        return null;
    }

    private static int? ReadInt32(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is not JsonValue value)
            return null;

        if (value.TryGetValue(out int intValue))
            return intValue;

        if (value.TryGetValue(out long longValue))
            return checked((int)longValue);

        return null;
    }

    private static string ReadString(JsonNode? node, params string[] path)
    {
        var valueNode = GetPath(node, path);
        if (valueNode is null)
            return string.Empty;

        if (valueNode is JsonValue value)
        {
            if (value.TryGetValue(out string? stringValue) && stringValue is not null)
                return stringValue;

            return value.ToJsonString().Trim('"');
        }

        return valueNode.ToJsonString();
    }

    private static string ReadTextContent(JsonNode? resultNode)
    {
        if (GetPath(resultNode, "content") is not JsonArray contentArray)
            return string.Empty;

        var lines = contentArray
            .Select(entry => ReadString(entry, "text"))
            .Where(text => string.IsNullOrWhiteSpace(text) == false)
            .ToList();

        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed class ToolInvocationResult
{
    public required string ToolName { get; init; }

    public required bool Success { get; set; }

    public required bool IsError { get; set; }

    public required long DurationMs { get; init; }

    public required string Text { get; init; }

    public required string Message { get; set; }

    public JsonNode? StructuredContent { get; init; }
}

internal sealed class SmokeStepReport
{
    public required string Name { get; init; }

    public required string ToolName { get; init; }

    public required bool Success { get; init; }

    public required long DurationMs { get; init; }

    public required string Message { get; init; }

    public string OperationId { get; init; } = string.Empty;

    public JsonNode? Response { get; init; }
}

internal sealed class SmokeRunReport
{
    public required string Scenario { get; init; }

    public required string GameId { get; init; }

    public required string GabsBinaryPath { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public bool Success { get; set; }

    public long DurationMs { get; set; }

    public string FailureMessage { get; set; } = string.Empty;

    public string Exception { get; set; } = string.Empty;

    public string ReportPath { get; set; } = string.Empty;

    public List<SmokeStepReport> Steps { get; set; } = [];

    public List<string> Notes { get; set; } = [];

    public List<string> GabsStderrTail { get; set; } = [];

    public JsonNode? InitialBridgeState { get; set; }

    public JsonNode? FinalBridgeState { get; set; }

    public int? ColonistCount { get; set; }

    public List<JsonNode?> OperationEvents { get; set; } = [];

    public List<JsonNode?> LogEntries { get; set; } = [];
}
