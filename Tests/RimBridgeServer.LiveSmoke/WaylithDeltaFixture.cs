using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace RimBridgeServer.LiveSmoke;

internal static class WaylithDeltaFixture
{
    public static void Create(string output, string sdkPath)
    {
        Directory.CreateDirectory(output);
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = paths.Append(sdkPath).Distinct().Select(p => MetadataReference.CreateFromFile(p)).ToArray();
        CSharpCompilation Compile(string added) => CSharpCompilation.Create("RimBridge.DevelopmentProof",
            [CSharpSyntaxTree.ParseText("using RimBridgeServer.Sdk; public sealed class DevelopmentTests { " +
                "[Tool(\"proof/existing\")] public static int Existing() => 1; " + added + " }", encoding: Encoding.UTF8)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));
        var first = Compile("");
        using var pe = new MemoryStream();
        using var initialPdb = new MemoryStream();
        var initial = first.Emit(pe, initialPdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Require(initial.Success, initial.Diagnostics);
        var bytes = pe.ToArray();
        File.WriteAllBytes(Path.Combine(output, "baseline.bin"), bytes);
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
        {
            baselineSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            scope = "Synthetic zero-argument static test methods; no Harmony patches or existing objects."
        }));
        using var module = ModuleMetadata.CreateFromImage(bytes);
        var baseline = EmitBaseline.CreateInitialBaseline(first, module, _ => default, _ => default, true);
        var previous = first;
        for (int generation = 1; generation <= 2; generation++)
        {
            var next = Compile($"[Tool(\"proof/added\")] public static int AddedTest() => {40 + generation};");
            var oldMethod = previous.GetTypeByMetadataName("DevelopmentTests")!.GetMembers("AddedTest").SingleOrDefault();
            var newMethod = next.GetTypeByMetadataName("DevelopmentTests")!.GetMembers("AddedTest").Single();
            var edit = new SemanticEdit(oldMethod == null ? SemanticEditKind.Insert : SemanticEditKind.Update,
                oldMethod, newMethod, syntaxMap: null, runtimeRudeEdit: null, instrumentation: default);
            using var metadata = new MemoryStream();
            using var il = new MemoryStream();
            using var pdb = new MemoryStream();
            var delta = next.EmitDifference(baseline, [edit], _ => false, metadata, il, pdb);
            Require(delta.Success, delta.Diagnostics);
            foreach (var part in new[] { ("metadata", metadata), ("il", il), ("pdb", pdb) })
                File.WriteAllBytes(Path.Combine(output, $"generation-{generation}.{part.Item1}"), part.Item2.ToArray());
            baseline = delta.Baseline!;
            previous = next;
        }
    }

    private static void Require(bool success, IEnumerable<Diagnostic> diagnostics)
    {
        if (!success) throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics));
    }
}
