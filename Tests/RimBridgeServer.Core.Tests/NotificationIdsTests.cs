using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class NotificationIdsTests
{
    [Fact]
    public void CreateAlertSnapshotFingerprint_IsDeterministic()
    {
        var first = NotificationIds.CreateAlertSnapshotFingerprint(["Alert_Fire", "Alert_BreakRisk"]);
        var second = NotificationIds.CreateAlertSnapshotFingerprint(["Alert_Fire", "Alert_BreakRisk"]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateAlertSnapshotFingerprint_DiffersWhenOrderChanges()
    {
        var first = NotificationIds.CreateAlertSnapshotFingerprint(["Alert_Fire", "Alert_BreakRisk"]);
        var second = NotificationIds.CreateAlertSnapshotFingerprint(["Alert_BreakRisk", "Alert_Fire"]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateAlertId_EmbedsSnapshotFingerprint()
    {
        var snapshotFingerprint = NotificationIds.CreateAlertSnapshotFingerprint(["Alert_Fire"]);
        var alertId = NotificationIds.CreateAlertId(snapshotFingerprint, 2, ["RimWorld.Alert_FireInHomeArea", "critical"]);

        Assert.True(NotificationIds.TryReadAlertSnapshotFingerprint(alertId, out var parsed));
        Assert.Equal(snapshotFingerprint, parsed);
    }
}
