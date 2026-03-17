using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class UiLayoutTargetIdsTests
{
    [Fact]
    public void ParsesSurfaceTargetIds()
    {
        var targetId = UiLayoutTargetIds.CreateSurfaceTargetId(4, 2);

        var parsed = UiLayoutTargetIds.TryParse(targetId, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(UiLayoutTargetKind.Surface, target.Kind);
        Assert.Equal(4, target.CaptureId);
        Assert.Equal(2, target.SurfaceIndex);
        Assert.Equal(0, target.ElementIndex);
    }

    [Fact]
    public void ParsesElementTargetIds()
    {
        var targetId = UiLayoutTargetIds.CreateElementTargetId(4, 2, 9);

        var parsed = UiLayoutTargetIds.TryParse(targetId, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(UiLayoutTargetKind.Element, target.Kind);
        Assert.Equal(4, target.CaptureId);
        Assert.Equal(2, target.SurfaceIndex);
        Assert.Equal(9, target.ElementIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ui-surface")]
    [InlineData("ui-surface:0:1")]
    [InlineData("ui-element:1:1")]
    [InlineData("ui-element:1:0:1")]
    [InlineData("ui-element:1:1:0")]
    public void RejectsMalformedTargetIds(string targetId)
    {
        var parsed = UiLayoutTargetIds.TryParse(targetId, out var target);

        Assert.False(parsed);
        Assert.Null(target);
    }
}
