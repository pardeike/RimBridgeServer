using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using RimBridgeServer.Sdk;
using UnityEngine;
using Verse;
using Waylith.Profiling;

namespace RimBridgeServer.WaylithTools;

// Explicit development companion, excluded from the player mod and ordinary 1.6 build.
public sealed class WaylithTools
{
    private static readonly SemaphoreSlim ProfileGate = new(1);
    private static Assembly developmentAssembly;
    private static int generation;
    private static string baselineHash;
    private static bool updateFailed;
    private static ShaderProofWindow shaderWindow;

    [Tool("waylith/status", Description = "Report actual rendering dimensions and managed update support. A loaded map alone does not establish screenshot support.")]
    public static Task<object> Status(IRimBridgeContext ctx) => ctx.MainThread.InvokeAsync<object>(() => new
    {
        success = true, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        headless = Environment.GetCommandLineArgs().Any(a => a.Equals("-Headless", StringComparison.OrdinalIgnoreCase)),
        noWindow = Environment.GetCommandLineArgs().Any(a => a.Equals("-NoWindow", StringComparison.OrdinalIgnoreCase)),
        width = Screen.width, height = Screen.height, pauseOnLoad = Prefs.PauseOnLoad,
        profilerEnabled = FrameProfiler.Enabled, profilerPaused = FrameProfiler.Paused, profilerCapturing = FrameProfiler.IsCapturing,
        metadataUpdates = MetadataUpdater.IsSupported, developmentGeneration = generation, updateFailed
    });

    [Tool("waylith/profile_capture", Description = "Capture a bounded number of native profiler frames without opening its window or enabling instrumentation. Restore collection settings on completion/cancellation. Existing active collection is refused.")]
    public static async Task<object> Profile(IRimBridgeContext ctx, int frames = 60, CancellationToken cancellationToken = default)
    {
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("-Headless", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("This frame-capture proof requires rendering; use -NoWindow.");
        if (frames < 2 || frames > 240) throw new ArgumentOutOfRangeException(nameof(frames), "Use 2..240 frames.");
        if (!await ProfileGate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("Another native capture is active.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        bool oldEnabled = false, oldPaused = true, started = false;
        try
        {
            await ctx.MainThread.InvokeAsync(() =>
            {
                if (FrameProfiler.IsCapturing) throw new InvalidOperationException("Existing profiler collection is active; stop it explicitly before this capture.");
                oldEnabled = FrameProfiler.Enabled;
                oldPaused = FrameProfiler.Paused;
                FrameProfiler.SetEnabled(true);
                FrameProfiler.SetPaused(false);
                started = true;
            }, timeout.Token);
            var before = FrameProfiler.CompletedFrameCount;
            await ctx.Game.FramesAsync(frames, timeout.Token);
            return await ctx.MainThread.InvokeAsync<object>(() =>
            {
                FrameProfiler.SetPaused(true);
                var rows = new List<object>();
                using (FrameProfiler.BeginRead())
                {
                    for (int i = 0; i < Math.Min(frames, FrameProfiler.HistoryCount); i++)
                    {
                        if (!FrameProfiler.TryGetHistorySnapshot(i, out var f)) continue;
                        rows.Add(new { f.frameIndex, f.totalMs, f.tickDelta, f.allocatedBytes, f.pauseMs,
                            f.gen0Collections, f.gen1Collections, f.gen2Collections, f.publishedNodeCount,
                            f.droppedEvents, f.unbalancedEnds });
                    }
                }
                return new { success = rows.Count > 0, requestedFrames = frames,
                    completedFrames = FrameProfiler.CompletedFrameCount - before, frames = rows,
                    instrumentationChanged = false, historyCleared = false };
            }, timeout.Token);
        }
        finally
        {
            try
            {
                if (started) await ctx.MainThread.InvokeAsync(() =>
                {
                    FrameProfiler.SetPaused(oldPaused);
                    FrameProfiler.SetEnabled(oldEnabled);
                });
            }
            finally { ProfileGate.Release(); }
        }
    }

    [Tool("waylith/development_tests", Description = "Opt-in development test dispatcher: load a baseline, apply compiler deltas in generation order, list fresh public annotated test methods, or run one. This does not rebuild the normal MCP registry. Failed updates require restart.")]
    public static async Task<object> DevelopmentTests(IRimBridgeContext ctx, string action = "list", string directory = null,
        string method = null, int expectedGeneration = 0, CancellationToken cancellationToken = default)
    {
        var immediate = await ctx.MainThread.InvokeAsync<object>(() =>
        {
            if (updateFailed) throw new InvalidOperationException("A prior metadata update failed. Restart before continuing.");
            if (action == "load")
            {
                if (developmentAssembly != null) throw new InvalidOperationException("A development baseline is already loaded. Restart to select another.");
                var bytes = File.ReadAllBytes(Path.Combine(directory, "baseline.bin"));
                baselineHash = Convert.ToHexString(SHA256.HashData(bytes));
                developmentAssembly = Assembly.Load(bytes);
                generation = 0;
            }
            else if (action == "apply")
            {
                if (!MetadataUpdater.IsSupported || developmentAssembly == null) throw new InvalidOperationException("No supported development baseline.");
                if (generation != expectedGeneration) throw new InvalidOperationException("Unexpected generation; do not replay an update.");
                var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json"))).RootElement;
                if (!manifest.GetProperty("baselineSha256").GetString().Equals(baselineHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Delta baseline hash mismatch.");
                var prefix = Path.Combine(directory, $"generation-{generation + 1}");
                // Read and validate all payloads before the mutation. Never retry an uncertain partial update.
                var metadata = File.ReadAllBytes(prefix + ".metadata");
                var il = File.ReadAllBytes(prefix + ".il");
                var pdb = File.ReadAllBytes(prefix + ".pdb");
                var types = developmentAssembly.GetTypes();
                if (types.Any(t => !t.IsSealed)) throw new NotSupportedException("This limited dispatcher updates sealed test types only; it must not clear reflection caches for unrelated game types.");
                var clearCache = typeof(object).Assembly.GetType("System.Reflection.Metadata.RuntimeTypeMetadataUpdateHandler")?
                    .GetMethod("ClearCache", BindingFlags.Public | BindingFlags.Static, null, [typeof(Type[])], null)
                    ?? throw new NotSupportedException("The runtime provides no known scoped reflection-cache refresh.");
                try
                {
                    MetadataUpdater.ApplyUpdate(developmentAssembly, metadata, il, pdb);
                    // ApplyUpdate alone does not run the normal hot-reload agent's ClearCache handlers.
                    // The sealed test classes have no derived types outside this development assembly.
                    clearCache.Invoke(null, [types]);
                    generation++;
                }
                catch { updateFailed = true; throw; }
            }
            else if (action == "run")
            {
                var target = TestMethods().Single(m => m.Name == method);
                if (target.GetParameters().Length != 0) throw new InvalidOperationException("This limited dispatcher runs zero-argument static tests only.");
                if (typeof(Task).IsAssignableFrom(target.ReturnType)) throw new NotSupportedException("This limited dispatcher supports synchronous tests only.");
                var value = target.Invoke(null, null);
                return new { success = true, generation, method, value, assemblyMvid = developmentAssembly.ManifestModule.ModuleVersionId };
            }
            else if (action != "list") throw new ArgumentException("Use load/apply/list/run.", nameof(action));
            return new { success = true, generation, baselineHash, assemblyMvid = developmentAssembly?.ManifestModule.ModuleVersionId,
                tests = TestMethods().Select(m => m.Name).ToArray(), normalMcpRegistryRefreshed = false };
        }, cancellationToken);
        return immediate;
    }

    private static MethodInfo[] TestMethods() => developmentAssembly == null ? [] : developmentAssembly.GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .Where(m => !m.ContainsGenericParameters && m.GetCustomAttribute<ToolAttribute>() != null).ToArray();

    [Tool("waylith/shader_proof", Description = "Open/close the temporary proof shader window. Requires the isolated rimbridge.waylithproof fixture mod. Does not edit shader files.")]
    public static Task<object> ShaderProof(IRimBridgeContext ctx, bool open = true, bool assetTexture = false) => ctx.MainThread.InvokeAsync<object>(() =>
    {
        if (open && Environment.GetCommandLineArgs().Any(a => a.Equals("-Headless", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("The visual proof requires -NoWindow or a visible window.");
        if (shaderWindow != null) { shaderWindow.Close(); shaderWindow = null; }
        if (open)
        {
            shaderWindow = new ShaderProofWindow(assetTexture);
            Find.WindowStack.Add(shaderWindow);
        }
        return new { success = true, open, assetTexture, textureWidth = shaderWindow?.TextureWidth ?? 0, textureHeight = shaderWindow?.TextureHeight ?? 0, x = shaderWindow?.windowRect.x ?? 0, y = shaderWindow?.windowRect.y ?? 0, centerX = shaderWindow == null ? 0.5 : shaderWindow.windowRect.center.x / UI.screenWidth, centerY = shaderWindow == null ? 0.5 : shaderWindow.windowRect.center.y / UI.screenHeight };
    });
}

public sealed class ShaderProofWindow : Window
{
    private readonly Material material;
    private readonly Texture2D texture;
    public int TextureWidth => texture.width;
    public int TextureHeight => texture.height;
    public override Vector2 InitialSize => new(320, 320);
    public ShaderProofWindow(bool assetTexture)
    {
        doCloseX = true; forcePause = true;
        texture = assetTexture ? ContentFinder<Texture2D>.Get("RimBridgeProof/PackOnly") : Texture2D.whiteTexture;
        if (!assetTexture) material = new Material(DefDatabase<ShaderDef>.GetNamed("RimBridgeWaylithProof").Shader);
    }
    public override void DoWindowContents(Rect rect)
    {
        if (Event.current.type != EventType.Repaint) return;
        if (material == null) GUI.DrawTexture(new Rect(20, 20, 200, 200), texture);
        else Graphics.DrawTexture(new Rect(20, 20, 200, 200), texture,
            new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, material, -1);
    }
    public override void PostClose() { if (material != null) UnityEngine.Object.Destroy(material); base.PostClose(); }
}
