using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class ModSettingsIdsTests
{
    [Fact]
    public void CreatesStableIdFromPackageIdAndHandleType()
    {
        var first = ModSettingsIds.CreateId("brrainz.rimbridgeserver", "RimBridgeServer.RimBridgeServerMod");
        var second = ModSettingsIds.CreateId("brrainz.rimbridgeserver", "RimBridgeServer.RimBridgeServerMod");

        Assert.Equal(first, second);
        Assert.StartsWith("mod-settings:brrainz.rimbridgeserver:", first);
    }
}
