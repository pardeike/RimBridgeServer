using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace RimBridgeServer.LiveSmoke;

// Uses the existing live-smoke MCP client and wrapper; no additional MCP server or launcher.
internal sealed class WaylithProof
{
    private readonly string root;
    private readonly string gameRoot;
    private readonly string gabs;
    private readonly List<object> evidence = [];
    private McpStdioClient? client;
    private string gameId = "rimbridge-waylith-proof";
    private bool ownsGame;
    private bool startAttempted;
    private string? currentProfile;
    private bool ownsBundle, ownsMod;
    private FileStream? deploymentLock;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Dictionary<string, string> discovered = new(StringComparer.Ordinal);

    private WaylithProof(string root, string gameRoot, string gabs)
    { this.root = root; this.gameRoot = gameRoot; this.gabs = gabs; }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--game-root" || args[2] != "--gabs-bin")
        {
            Console.Error.WriteLine("usage: scripts/live-smoke.sh --waylith-poc --game-root PATH --gabs-bin PATH");
            return 2;
        }
        var repo = FindRepo();
        var output = Path.Combine(repo, "artifacts", "waylith-proof", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(output);
        var run = new WaylithProof(output, Path.GetFullPath(args[1]), Path.GetFullPath(args[3]));
        var log = Path.Combine(output, "workflow.log");
        using var writer = File.CreateText(log); writer.AutoFlush = true;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(writer); Console.SetError(writer);
        using var signal = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM,
            context => { context.Cancel = true; run.shutdown.Cancel(); });
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; run.shutdown.Cancel(); };
        Console.CancelKeyPress += cancel;
        Exception? failure = null;
        try { await run.ExecuteAsync(repo); }
        catch (Exception ex) { failure = ex; Console.Error.WriteLine(ex); }
        finally
        {
            try { await run.CleanupAsync(); }
            catch (Exception ex) { failure ??= ex; Console.Error.WriteLine(ex); }
            await writer.FlushAsync();
            Console.SetOut(originalOut); Console.SetError(originalError);
            Console.CancelKeyPress -= cancel;
        }
        await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        { success = failure == null, failure = failure?.Message, evidence = run.evidence }, new JsonSerializerOptions { WriteIndented = true }));
        if (failure == null) Console.WriteLine("ok");
        else Console.Error.WriteLine($"waylith proof failed: {failure.Message}\nFull log: {log}");
        return failure == null ? 0 : 1;
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RimBridgeServer.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Run from the RimBridgeServer repository.");
    }

    private string Managed => Path.Combine(gameRoot, "RimWorld.app", "Contents", "MacOS");
    private string Bundle => Path.Combine(gameRoot, "BridgeTools", "RimBridgeWaylithProof");
    private string Mod => Path.Combine(gameRoot, "Mods", "RimBridgeWaylithProof");

    private async Task ExecuteAsync(string repo)
    {
        if (Process.GetProcessesByName("RimWorld").Any() || Process.GetProcessesByName("RimWorldMac").Any())
            throw new InvalidOperationException("An existing game is running; leave it alone and rerun after it stops.");
        if (!File.Exists(Path.Combine(Managed, "Waylith.dll"))) throw new InvalidOperationException("This proof currently supports the macOS RimWorld.app 1.7 layout only.");
        Directory.CreateDirectory(Path.Combine(gameRoot, "BridgeTools"));
        deploymentLock = new FileStream(Path.Combine(gameRoot, "BridgeTools", ".waylith-proof.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Directory.Exists(Bundle) || Directory.Exists(Mod)) throw new InvalidOperationException("Proof deployment already exists; inspect it rather than overwriting.");
        var sdk = Path.Combine(gameRoot, "Mods", "RimBridgeServer", "1.7", "Assemblies", "RimBridgeServer.Sdk.dll");
        await ProcessAsync("dotnet", ["build", Path.Combine(repo, "Companions", "Waylith", "Waylith.BridgeTools.csproj"), "-c", "Release",
            "-o", Path.Combine(root, "companion"), "-p:RimWorld17ManagedDir=" + Managed]);
        WaylithDeltaFixture.Create(Path.Combine(root, "deltas"), sdk);
        Directory.CreateDirectory(Bundle); ownsBundle = true;
        File.Copy(Path.Combine(root, "companion", "Waylith.BridgeTools.dll"), Path.Combine(Bundle, "Waylith.BridgeTools.dll"));
        ownsMod = true; PrepareShaderMod();
        var userData = Path.Combine(root, "UserData");
        Directory.CreateDirectory(Path.Combine(userData, "Config"));
        new XDocument(new XElement("PrefsData", new XElement("pauseOnLoad", true), new XElement("devMode", false)))
            .Save(Path.Combine(userData, "Config", "Prefs.xml"));
        CreateModsConfig(gameRoot).Save(Path.Combine(userData, "Config", "ModsConfig.xml"));
        var configDir = Path.Combine(root, "gabs"); Directory.CreateDirectory(configDir);
        await File.WriteAllTextAsync(Path.Combine(configDir, "config.json"), JsonSerializer.Serialize(new
        {
            version = "1.0", games = new Dictionary<string, object> { [gameId] = new
            {
                id = gameId, name = "Owned Waylith proof", launchMode = "SteamManaged", target = "294100", stopProcessName = "RimWorld", gabpMode = "local",
                args = new[] { "-savedatafolder=" + userData, "-WindowSize=960,640", "-WatchShaders" },
                defaultProfile = "hidden", profiles = new Dictionary<string, object>
                { ["hidden"] = new { args = new[] { "-NoWindow" } }, ["headless"] = new { args = new[] { "-Headless" } } }
            } }
        }));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(8));
        client = await McpStdioClient.StartAsync(gabs, configDir, timeout.Token);
        await StartAsync("hidden", timeout.Token);
        var mods = await GameAsync("rimworld/get_mod_configuration_status", new { }, timeout.Token);
        var harmonyRoot = Path.GetFullPath(Path.Combine(gameRoot, "../../workshop/content/294100/2009463077"));
        Require(mods["activeMods"]!.AsArray().Any(m => m?["rootDir"]?.GetValue<string>() == harmonyRoot && m["loadedInSession"]?.GetValue<bool>() == true), "Workshop Harmony root not loaded.");
        Require(mods["activeMods"]!.AsArray().Any(m => m?["packageId"]?.GetValue<string>() == "brrainz.rimbridgeserver"), "Bridge missing from resolved loadout.");
        await GameAsync("rimworld/start_debug_game_ready", new { readiness = "playable", pauseIfNeeded = false, timeoutMs = 120000 }, timeout.Token);
        var status = await GameAsync("waylith/status", new { }, timeout.Token);
        Require(status["pauseOnLoad"]?.GetValue<bool>() == true, "Native Pause on load setting is not enabled.");
        await GameAsync("rimworld/pause_game", new { pause = true }, timeout.Token);
        var save = await GameAsync("rimworld/save_game", new { saveName = "WaylithProofCheckpoint" }, timeout.Token);
        var savedTick = save["state"]!["tick"]!.GetValue<int>();
        for (int i = 0; i < 2; i++)
        {
            await GameAsync("rimworld/step_game_ticks", new { ticks = 10 }, timeout.Token);
            var loaded = await GameAsync("rimworld/load_game_ready", new { saveName = "WaylithProofCheckpoint", readiness = "playable", pauseIfNeeded = false }, timeout.Token);
            Require(loaded["state"]?["tick"]?.GetValue<int>() == savedTick + 1 && loaded["state"]?["paused"]?.GetValue<bool>() == true, "Native pause-on-load invariant failed.");
            evidence.Add(new { proof = "native-pause-on-load", savedTick, actualTick = loaded["state"]?["tick"]?.GetValue<int>(),
                paused = loaded["state"]?["paused"]?.GetValue<bool>(), bridgePauseRequested = false,
                exact = loaded["state"]?["tick"]?.GetValue<int>() == savedTick, expectedNativeTick = savedTick + 1, nativeBehaviorCorrect = loaded["state"]?["tick"]?.GetValue<int>() == savedTick + 1 && loaded["state"]?["paused"]?.GetValue<bool>() == true });
        }
        await GameAsync("rimworld/press_key", new { key = "Space" }, timeout.Token);
        var running = await GameAsync("rimworld/get_ui_state", new { }, timeout.Token);
        evidence.Add(new { proof = "hidden-keyboard-after-load", spaceUnpaused = running["state"]?["paused"]?.GetValue<bool>() == false });
        await GameAsync("rimworld/pause_game", new { pause = true }, timeout.Token);
        await CaptureAsync("hidden-map", timeout.Token);
        var beforeProfile = await GameAsync("waylith/status", new { }, timeout.Token);
        var profile = await GameAsync("waylith/profile_capture", new { frames = 30 }, timeout.Token);
        Require(profile["frames"]?.AsArray().Count > 0, "No native profile records.");
        var afterProfile = await GameAsync("waylith/status", new { }, timeout.Token);
        Require(beforeProfile["profilerEnabled"]!.GetValue<bool>() == afterProfile["profilerEnabled"]!.GetValue<bool>() &&
            beforeProfile["profilerPaused"]!.GetValue<bool>() == afterProfile["profilerPaused"]!.GetValue<bool>(), "Profiler collection settings were not restored.");
        await ProveAddedTestsAsync(timeout.Token);
        await ProveCoordinatesAsync(timeout.Token);
        await ProveShaderAsync(timeout.Token);
        await StopAsync(timeout.Token);
        await BakeAsync("cpu", "-Headless");
        await BakeAsync("gpu", "-NoWindow");
        await StartAsync("headless", timeout.Token);
        await GameAsync("rimworld/load_game_ready", new { saveName = "WaylithProofCheckpoint", readiness = "playable", pauseIfNeeded = false }, timeout.Token);
        var headless = await GameAsync("waylith/status", new { }, timeout.Token);
        Require(headless["headless"]?.GetValue<bool>() == true, "Headless launch was not observed by the game.");
        await GameAsync("rimworld/step_game_ticks", new { ticks = 60 }, timeout.Token);
        // This is intentionally a negative/limited-feature probe, not an assumption that capture cannot work.
        var capture = await GameAsync("rimworld/take_screenshot", new { fileName = "headless-probe", outputDirectory = root, includeTargets = false }, timeout.Token, allowFailure: true);
        Require(capture["success"]?.GetValue<bool>() == false && capture["message"]?.GetValue<string>()?.Contains("-NoWindow") == true, "Headless capture did not return the explicit rendering-mode error.");
        var headlessCells = await GameAsync("rimworld/screenshot_cell_rect", new { x = 120, z = 120, fileName = "headless-cells", outputDirectory = root }, timeout.Token, allowFailure: true);
        Require(headlessCells["success"]?.GetValue<bool>() == false && headlessCells["message"]?.GetValue<string>()?.Contains("-NoWindow") == true, "Headless map crop did not reject before framing.");
        evidence.Add(new { proof = "headless-screenshot", path = capture["path"]?.GetValue<string>(), result = capture.DeepClone(),
            imageExists = File.Exists(Path.Combine(root, "headless-probe.png")) });
        await ProveAddedTestsAsync(timeout.Token);
        await StopAsync(timeout.Token);
        File.Copy(Path.Combine(root, "bake-cpu", "Assets.zip"), Path.Combine(Mod, "Assets.zip"));
        await StartAsync("hidden", timeout.Token);
        await GameAsync("rimworld/load_game_ready", new { saveName = "WaylithProofCheckpoint", readiness = "visual", pauseIfNeeded = false }, timeout.Token);
        var asset = await GameAsync("waylith/shader_proof", new { open = true, assetTexture = true }, timeout.Token);
        Require(asset["textureWidth"]?.GetValue<int>() > 100 && asset["textureHeight"]?.GetValue<int>() > 100, "Packed-only texture did not load.");
        Require(!Directory.Exists(Path.Combine(Mod, "Textures")), "Packed-only proof unexpectedly has loose textures.");
        await CaptureAsync("packed-texture", timeout.Token);
        evidence.Add(new { proof = "packed-only-texture-runtime", asset = asset.DeepClone(), looseTextureDirectoryExists = false });
        await GameAsync("waylith/shader_proof", new { open = false }, timeout.Token);
        await StopAsync(timeout.Token);
    }

    private async Task ProveAddedTestsAsync(CancellationToken ct)
    {
        var dir = Path.Combine(root, "deltas");
        var baseline = await GameAsync("waylith/development_tests", new { action = "load", directory = dir }, ct);
        Require(!baseline["tests"]!.AsArray().Any(n => n?.GetValue<string>() == "AddedTest"), "Added test already exists in baseline.");
        var original = await GameAsync("waylith/development_tests", new { action = "run", method = "Existing" }, ct);
        Require(original["value"]?.GetValue<int>() == 1, "Baseline test failed.");
        await GameAsync("waylith/development_tests", new { action = "apply", directory = dir, expectedGeneration = 0 }, ct);
        var added = await GameAsync("waylith/development_tests", new { action = "run", method = "AddedTest" }, ct);
        Require(added["value"]?.GetValue<int>() == 41, "Added method was not discovered/callable.");
        await GameAsync("waylith/development_tests", new { action = "apply", directory = dir, expectedGeneration = 1 }, ct);
        var changed = await GameAsync("waylith/development_tests", new { action = "run", method = "AddedTest" }, ct);
        Require(changed["value"]?.GetValue<int>() == 42, "Updating the new method failed.");
        var unchanged = await GameAsync("waylith/development_tests", new { action = "run", method = "Existing" }, ct);
        Require(unchanged["value"]?.GetValue<int>() == 1, "Adding a test changed an existing test.");
    }

    private async Task ProveCoordinatesAsync(CancellationToken ct)
    {
        var invalid = await GameAsync("rimworld/take_screenshot", new { clipX = 1, outputDirectory = root, fileName = "invalid-crop" }, ct, allowFailure: true);
        Require(invalid["success"]?.GetValue<bool>() == false, "Partial coordinate crop was accepted.");
        var camera = await GameAsync("rimworld/get_camera_state", new { }, ct);
        var map = await GameAsync("rimworld/screenshot_cell_rect", new { x = 120, z = 120, width = 6, height = 4, paddingCells = 0, fileName = "cells-6x4", outputDirectory = root }, ct);
        Require(map["cameraWasReset"]?.GetValue<bool>() == true, "Cell capture did not restore camera.");
        VerifyCrop(map);
        var window = await GameAsync("waylith/shader_proof", new { open = true }, ct);
        var targets = await GameAsync("rimworld/get_screen_targets", new { }, ct);
        var target = targets["targets"]!["windows"]!.AsArray().Single(w => w?["type"]?.GetValue<string>()?.Contains("ShaderProofWindow") == true);
        var id = target!["windowTargetId"]!.GetValue<string>();
        var whole = await GameAsync("rimworld/take_screenshot", new { fileName = "dialog-whole", clipTargetId = id, clipPadding = 0, outputDirectory = root }, ct);
        VerifyCrop(whole);
        var wx = window["x"]!.GetValue<float>(); var wy = window["y"]!.GetValue<float>();
        var relative = await GameAsync("rimworld/take_screenshot", new { fileName = "dialog-relative", clipTargetId = id, clipX = 50, clipY = 50, clipWidth = 60, clipHeight = 40, clipPadding = 0, outputDirectory = root }, ct);
        VerifyCrop(relative);
        var absolute = await GameAsync("rimworld/take_screenshot", new { fileName = "dialog-screen", clipX = wx + 50, clipY = wy + 50, clipWidth = 60, clipHeight = 40, clipPadding = 0, outputDirectory = root }, ct);
        VerifyCrop(absolute);
        foreach (var crop in new[] { relative, absolute })
        {
            var color = PngProof.Sample(crop["path"]!.GetValue<string>(), 0.5, 0.5);
            Require(color.r > 200 && color.g < 60, "Coordinate crop missed the red proof region: " + color);
        }
        evidence.Add(new { proof = "screenshot-coordinates", map = map.DeepClone(), window = whole.DeepClone(), relative = relative.DeepClone(), absolute = absolute.DeepClone(), cameraBefore = camera.DeepClone() });
        await GameAsync("waylith/shader_proof", new { open = false }, ct);
    }

    private static void VerifyCrop(JsonNode result)
    {
        Require(result["clipped"]?.GetValue<bool>() == true, "Screenshot was not cropped.");
        var path = result["path"]!.GetValue<string>();
        var source = result["sourcePath"]!.GetValue<string>();
        var rect = result["clipRect"]!;
        var bytes = File.ReadAllBytes(path);
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        Require(width == rect["width"]!.GetValue<int>() && height == rect["height"]!.GetValue<int>(), "Crop dimensions differ from reported pixel rect.");
        var full = File.ReadAllBytes(source);
        var fullWidth = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(full.AsSpan(16, 4));
        var fullHeight = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(full.AsSpan(20, 4));
        foreach (var (fx, fy) in new[] { (0.17, 0.23), (0.53, 0.61), (0.81, 0.77) })
        {
            var px = (int)(fx * width); var py = (int)(fy * height);
            var a = PngProof.Sample(path, (px + 0.5) / width, (py + 0.5) / height);
            var b = PngProof.Sample(source, (rect["x"]!.GetValue<int>() + px + 0.5) / fullWidth, (rect["y"]!.GetValue<int>() + py + 0.5) / fullHeight);
            Require(a == b, $"Crop pixel differs from source at {px},{py}: {a} versus {b}");
        }
    }

    private async Task ProveShaderAsync(CancellationToken ct)
    {
        var window = await GameAsync("waylith/shader_proof", new { open = true }, ct);
        var redPath = await CaptureAsync("shader-red", ct);
        var red = PngProof.Sample(redPath, window["centerX"]!.GetValue<double>(), window["centerY"]!.GetValue<double>());
        Require(red.r > 200 && red.g < 60, $"Proof shader is not red: {red}");
        SetShader(true);
        // Bounded observation of rendered pixels, not an arbitrary sleep standing in for proof.
        (byte r, byte g, byte b) green = default;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(12))
        {
            var path = await CaptureAsync("shader-green", ct);
            green = PngProof.Sample(path, window["centerX"]!.GetValue<double>(), window["centerY"]!.GetValue<double>());
            if (green.g > 200 && green.r < 60) break;
            await Task.Delay(200, ct);
        }
        Require(green.g > 200 && green.r < 60, $"Watcher did not produce a green rendered result: {green}");
        var overwritten = await CaptureAsync("shader-red", ct);
        var fresh = PngProof.Sample(overwritten, window["centerX"]!.GetValue<double>(), window["centerY"]!.GetValue<double>());
        Require(fresh.g > 200 && fresh.r < 60, "Reusing a screenshot name returned a stale red image.");
        evidence.Add(new { proof = "shader-watch", red = red.ToString(), green = green.ToString(), sameProcess = true });
        await GameAsync("waylith/shader_proof", new { open = false }, ct);
    }

    private async Task<string> CaptureAsync(string name, CancellationToken ct)
    {
        var result = await GameAsync("rimworld/take_screenshot", new { fileName = name, outputDirectory = root, includeTargets = false }, ct);
        var path = result["path"]!.GetValue<string>();
        Require(new FileInfo(path).Length > 100, "Screenshot is missing/empty.");
        _ = PngProof.Sample(path, 0.5, 0.5);
        return path;
    }

    private async Task BakeAsync(string name, string mode)
    {
        var fixture = Path.Combine(root, "bake-" + name); Directory.CreateDirectory(Path.Combine(fixture, "Textures", "RimBridgeProof"));
        Directory.CreateDirectory(Path.Combine(fixture, "About"));
        File.Copy(Path.Combine(Mod, "About", "About.xml"), Path.Combine(fixture, "About", "About.xml"));
        File.Copy(Path.Combine(root, "hidden-map.png"), Path.Combine(fixture, "Textures", "RimBridgeProof", "PackOnly.png"));
        await ProcessAsync(Path.Combine(Managed, "RimWorld"), [mode, "-NoFileLog", "-savedatafolder=" + Path.Combine(root, "BakeData-" + name), "-BakeModAssetPacks=" + fixture]);
        using var pack = System.IO.Compression.ZipFile.OpenRead(Path.Combine(fixture, "Assets.zip"));
        Require(pack.Entries.Any(e => e.FullName.EndsWith(".dds", StringComparison.Ordinal)), "Baked texture missing from pack.");
        evidence.Add(new { proof = "asset-bake", mode = name, entries = pack.Entries.Select(e => new { e.FullName, e.Length, e.CompressedLength }).ToArray() });
    }

    // Build 5014 ignores -mod entries outside the local Mods folder, so the profile lists its mods. A local Harmony copy
    // takes the package ID brrainz.harmony and the Workshop item becomes brrainz.harmony_steam. Do not list the Workshop
    // folder name 2009463077: on 5014 that entry broke Core's XML loading and RimWorld reset the profile to vanilla.
    internal static XDocument CreateModsConfig(string gameRoot)
    {
        string[] releaseOrder = ["ludeon.rimworld.royalty", "ludeon.rimworld.ideology", "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey"];
        var expansions = PackageIds(Path.Combine(gameRoot, "Data")).Where(id => id != "ludeon.rimworld")
            .OrderBy(id => Array.IndexOf(releaseOrder, id) is var index and >= 0 ? index : int.MaxValue).ThenBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var harmony = PackageIds(Path.Combine(gameRoot, "Mods")).Contains("brrainz.harmony") ? "brrainz.harmony_steam" : "brrainz.harmony";
        string[] mods = [harmony, "ludeon.rimworld", .. expansions, "brrainz.rimbridgeserver", "rimbridge.waylithproof"];
        return new XDocument(new XElement("ModsConfigData",
            new XElement("activeMods", mods.Select(id => new XElement("li", id))),
            new XElement("knownExpansions", expansions.Select(id => new XElement("li", id)))));
    }

    private static IEnumerable<string> PackageIds(string modsRoot) => Directory.GetDirectories(modsRoot)
        .Select(dir => Path.Combine(dir, "About", "About.xml")).Where(File.Exists)
        .Select(about => { try { return XDocument.Load(about).Root?.Element("packageId")?.Value.Trim().ToLowerInvariant(); } catch (System.Xml.XmlException) { return null; } })
        .OfType<string>();

    private void PrepareShaderMod()
    {
        Directory.CreateDirectory(Path.Combine(Mod, "About")); Directory.CreateDirectory(Path.Combine(Mod, "Defs")); Directory.CreateDirectory(Path.Combine(Mod, "Shaders"));
        File.WriteAllText(Path.Combine(Mod, "About", "About.xml"), "<ModMetaData><name>RimBridge Waylith proof</name><author>Local development</author><packageId>rimbridge.waylithproof</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
        File.WriteAllText(Path.Combine(Mod, "Defs", "Shader.xml"), "<Defs><ShaderDef><defName>RimBridgeWaylithProof</defName><shaderProgram><vertexShaderFileName>SceneDefault.vert.glsl</vertexShaderFileName><fragmentShaderFileName>RimBridgeProof.frag.glsl</fragmentShaderFileName><useTexture>false</useTexture><blendMode>Opaque</blendMode><depthTest>false</depthTest><depthWrite>false</depthWrite><cullMode>None</cullMode></shaderProgram></ShaderDef></Defs>");
        SetShader(false);
    }

    private void SetShader(bool green) => File.WriteAllText(Path.Combine(Mod, "Shaders", "RimBridgeProof.frag.glsl"),
        "#version 450\nlayout(location = 0) out vec4 outColor;\nvoid main() { outColor = " + (green ? "vec4(0,1,0,1)" : "vec4(1,0,0,1)") + "; }\n");

    private async Task<JsonNode> RpcAsync(string tool, object args, CancellationToken ct, bool allowFailure = false)
    {
        var response = await client!.CallToolAsync(tool, args, ct);
        Console.WriteLine($"{tool}: {(response.Success ? "ok" : "failed")} ({response.DurationMs} ms)");
        evidence.Add(new { tool, response.Success, response.DurationMs, response = response.StructuredContent?.DeepClone() });
        if (!allowFailure && !response.Success) throw new InvalidOperationException(tool + ": " + response.Message);
        var payload = response.StructuredContent ?? new JsonObject();
        if (!response.Success && payload["success"] == null) payload["success"] = false;
        return payload;
    }

    private async Task<JsonNode> GameAsync(string tool, object args, CancellationToken ct, bool allowFailure = false)
    {
        if (!discovered.TryGetValue(tool, out var fullName))
        {
            var names = await RpcAsync("games_tool_names", new { gameId, query = tool }, ct);
            fullName = names["tools"]!.AsArray().Single(t => t?["localName"]?.GetValue<string>() == tool)!["name"]!.GetValue<string>();
            await RpcAsync("games_tool_detail", new { gameId, tool = fullName }, ct);
            discovered[tool] = fullName;
        }
        var result = await RpcAsync("games_call_tool", new { gameId, tool = fullName, arguments = args, timeout = 30 }, ct, true);
        if (currentProfile == "headless" && result["status"]?.GetValue<string>() == "blocked_by_attention")
        {
            var attention = await RpcAsync("games_get_attention", new { gameId }, ct);
            var summary = attention["attention"]?["summary"]?.GetValue<string>() ?? "";
            Require(summary.Contains("Resolution too small (0x0), resetting UI scale to 1.", StringComparison.Ordinal), "Unexpected headless attention: " + summary);
            await RpcAsync("games_ack_attention", new { gameId, attentionId = attention["attention"]!["attentionId"]!.GetValue<string>() }, ct);
            result = await RpcAsync("games_call_tool", new { gameId, tool = fullName, arguments = args, timeout = 30 }, ct, allowFailure);
        }
        var success = result["success"]?.GetValue<bool>();
        if (!allowFailure && (success == false || result["status"]?.GetValue<string>() == "blocked_by_attention"))
            throw new InvalidOperationException(tool + ": " + result.ToJsonString());
        return result;
    }

    private async Task StartAsync(string profile, CancellationToken ct)
    {
        startAttempted = true;
        var start = await RpcAsync("games_start", new { gameId, profile, timeout = 90 }, ct);
        ownsGame = true; currentProfile = profile; discovered.Clear();
        // GABS can return while attachment proceeds; one bounded connect follows only if not connected.
        if (start["gabpConnected"]?.GetValue<bool>() != true) await RpcAsync("games_connect", new { gameId, timeout = 90 }, ct);
        await RpcAsync("games_tool_names", new { gameId, query = "waylith/", brief = true }, ct);
    }

    private async Task StopAsync(CancellationToken ct)
    {
        var stopped = await RpcAsync("games_stop", new { gameId }, ct);
        Require(stopped["code"]?.GetValue<string>() == "terminated", "Termination not verified.");
        ownsGame = false;
    }

    private async Task CleanupAsync()
    {
        if (client != null && ownsGame)
        {
            var stopped = await RpcAsync("games_stop", new { gameId }, CancellationToken.None);
            if (stopped["code"]?.GetValue<string>() != "terminated") throw new InvalidOperationException("Termination unverified; preserve deployed proof files.");
            ownsGame = false;
        }
        if (client != null) await client.DisposeAsync();
        if (ownsGame || (startAttempted && Process.GetProcesses().Any(p => p.ProcessName is "RimWorld" or "RimWorldMac")))
            throw new InvalidOperationException("A game may still be running; preserve deployed proof files.");
        if (ownsBundle) Directory.Delete(Bundle, recursive: true);
        if (ownsMod)
        {
            var archive = Path.Combine(root, "shader-source-final"); Directory.CreateDirectory(archive);
            File.Copy(Path.Combine(Mod, "Shaders", "RimBridgeProof.frag.glsl"), Path.Combine(archive, "RimBridgeProof.frag.glsl"));
            Directory.Delete(Mod, recursive: true);
        }
        if (deploymentLock != null)
        {
            deploymentLock.Dispose();
            File.Delete(Path.Combine(gameRoot, "BridgeTools", ".waylith-proof.lock"));
        }
    }

    private async Task ProcessAsync(string file, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + file);
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var childTimeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        childTimeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(childTimeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        Console.WriteLine(await output); Console.Error.WriteLine(await error);
        if (process.ExitCode != 0) throw new InvalidOperationException(file + " failed.");
    }

    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
