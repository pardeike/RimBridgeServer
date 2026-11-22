using System;
using System.IO;
using System.Linq;
using RimBridgeServer.LiveSmoke;
using Xunit;

namespace RimBridgeServer.LiveSmoke.Tests;

public sealed class WaylithProofModsConfigTests : IDisposable
{
    private readonly string gameRoot = Path.Combine(Path.GetTempPath(), "rimbridge-modsconfig-" + Guid.NewGuid().ToString("N"));

    public WaylithProofModsConfigTests()
    {
        AddMod("Data/Core", "Ludeon.RimWorld");
        AddMod("Data/Biotech", "Ludeon.RimWorld.Biotech");
        AddMod("Data/Royalty", "Ludeon.RimWorld.Royalty");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Mods"));
    }

    public void Dispose() => Directory.Delete(gameRoot, recursive: true);

    [Fact]
    public void ListsWorkshopHarmonyCoreAndInstalledExpansionsInReleaseOrder()
    {
        var config = WaylithProof.CreateModsConfig(gameRoot);

        Assert.Equal(["brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.biotech", "brrainz.rimbridgeserver", "rimbridge.waylithproof"], Items(config, "activeMods"));
        Assert.Equal(["ludeon.rimworld.royalty", "ludeon.rimworld.biotech"], Items(config, "knownExpansions"));
    }

    [Fact]
    public void SelectsTheWorkshopCopyWhenALocalHarmonyHasTheSamePackageId()
    {
        AddMod("Mods/HarmonyMod", "brrainz.harmony");

        Assert.Equal("brrainz.harmony_steam", Items(WaylithProof.CreateModsConfig(gameRoot), "activeMods")[0]);
    }

    private void AddMod(string folder, string packageId)
    {
        var about = Path.Combine(gameRoot, folder, "About");
        Directory.CreateDirectory(about);
        File.WriteAllText(Path.Combine(about, "About.xml"), $"<ModMetaData><packageId>{packageId}</packageId></ModMetaData>");
    }

    private static string[] Items(System.Xml.Linq.XDocument config, string list) =>
        config.Root!.Element(list)!.Elements("li").Select(item => item.Value).ToArray();
}
