using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class SelectionGizmoIdsTests
{
    [Fact]
    public void CreateSelectionFingerprint_IsDeterministic()
    {
        var first = SelectionGizmoIds.CreateSelectionFingerprint(["thing:Thing_1", "zone:Zone_2"]);
        var second = SelectionGizmoIds.CreateSelectionFingerprint(["thing:Thing_1", "zone:Zone_2"]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateSelectionFingerprint_DiffersWhenOrderChanges()
    {
        var first = SelectionGizmoIds.CreateSelectionFingerprint(["thing:Thing_1", "zone:Zone_2"]);
        var second = SelectionGizmoIds.CreateSelectionFingerprint(["zone:Zone_2", "thing:Thing_1"]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateGizmoId_EmbedsSelectionFingerprint()
    {
        var selectionFingerprint = SelectionGizmoIds.CreateSelectionFingerprint(["thing:Thing_1"]);
        var gizmoId = SelectionGizmoIds.CreateGizmoId(selectionFingerprint, 3, ["Command_Action", "Draft"]);

        Assert.True(SelectionGizmoIds.TryReadSelectionFingerprint(gizmoId, out var parsed));
        Assert.Equal(selectionFingerprint, parsed);
    }
}
