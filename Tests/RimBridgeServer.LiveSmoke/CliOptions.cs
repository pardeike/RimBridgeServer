using System.Text;

namespace RimBridgeServer.LiveSmoke;

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
                Scenario = scenario ?? SmokeScenarioCatalog.DefaultScenarioName,
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
        writer.WriteLine($"  scripts/live-smoke.sh --scenario {SmokeScenarioCatalog.DefaultScenarioName} [options]");
        writer.WriteLine($"  dotnet run --project Tests/RimBridgeServer.LiveSmoke -- --scenario {SmokeScenarioCatalog.DefaultScenarioName} [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --scenario <name>                 Scenario to run");
        writer.WriteLine("  --list-scenarios                  Show available scenario names and descriptions");
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
        foreach (var scenario in SmokeScenarioCatalog.List())
            writer.WriteLine($"  {scenario.Name.PadRight(20)} {scenario.Description}");
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
