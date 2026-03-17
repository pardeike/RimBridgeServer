using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class ModConfigurationIdsTests
{
    [Fact]
    public void CreatesStableIdFromPackageIdAndRootDir()
    {
        var first = ModConfigurationIds.CreateId("brrainz.rimbridgeserver", "/tmp/RimBridgeServer");
        var second = ModConfigurationIds.CreateId("brrainz.rimbridgeserver", "/tmp/RimBridgeServer");

        Assert.Equal(first, second);
        Assert.StartsWith("mod-config:brrainz.rimbridgeserver:", first);
    }
}
