using RimBridgeServer.Core;
using Xunit;

namespace RimBridgeServer.Core.Tests;

public class ScreenTargetIdsTests
{
    [Fact]
    public void ParsesWindowDismissTargetIds()
    {
        var targetId = ScreenTargetIds.CreateWindowDismissTargetId(7, "Verse.FloatMenu");

        var parsed = ScreenTargetIds.TryParse(targetId, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(ScreenTargetKind.WindowDismiss, target.Kind);
        Assert.Equal(7, target.WindowId);
        Assert.Equal("Verse.FloatMenu", target.WindowType);
    }

    [Fact]
    public void ParsesContextMenuOptionTargetIds()
    {
        var targetId = ScreenTargetIds.CreateContextMenuOptionTargetId(3, 2);

        var parsed = ScreenTargetIds.TryParse(targetId, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(ScreenTargetKind.ContextMenuOption, target.Kind);
        Assert.Equal(3, target.MenuId);
        Assert.Equal(2, target.OptionIndex);
    }

    [Fact]
    public void ParsesMainTabTargetIds()
    {
        var targetId = ScreenTargetIds.CreateMainTabTargetId("Work");

        var parsed = ScreenTargetIds.TryParse(targetId, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(ScreenTargetKind.MainTab, target.Kind);
        Assert.Equal("Work", target.MainTabDefName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("window")]
    [InlineData("window-dismiss:abc:Verse.FloatMenu")]
    [InlineData("context-menu-option:0:1")]
    [InlineData("context-menu-option:1:0")]
    [InlineData("context-menu-option:1")]
    [InlineData("main-tab")]
    public void RejectsMalformedTargetIds(string targetId)
    {
        var parsed = ScreenTargetIds.TryParse(targetId, out var target);

        Assert.False(parsed);
        Assert.Null(target);
    }
}
