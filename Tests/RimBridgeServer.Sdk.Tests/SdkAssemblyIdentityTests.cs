using System;
using RimBridgeServer.Sdk;
using Xunit;

namespace RimBridgeServer.Sdk.Tests;

public class SdkAssemblyIdentityTests
{
    [Fact]
    public void ServerVersionChangesDoNotChangeThePublishedCompanionSdkIdentity()
    {
        var identity = typeof(ToolAttribute).Assembly.GetName();
        Assert.Equal("RimBridgeServer.Sdk", identity.Name);
        Assert.Equal(new Version(2, 0, 0, 0), identity.Version);
        Assert.Empty(identity.GetPublicKeyToken());
    }
}
